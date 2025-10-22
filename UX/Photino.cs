using System;
using System.IO;
using System.Text;
using System.Linq;
using Photino.NET;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Channels;

public sealed class PhotinoUi : CUiBase
{
	private class BoolBox { public bool Answer { get; set; } }
	private PhotinoWindow? _win;
	private Thread? _uiThread;
	// Outbound and inbound message channels
	private readonly Channel<string> _tx = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
	private readonly Channel<string> _rx = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
	private Task? _txPumpTask;
	private Task? _rxPumpTask;
	private int _txStarted = 0; // 0 = not started, 1 = started

	private TaskCompletionSource<ConsoleKeyInfo>? _tcsKey;

	private readonly IFilePicker _picker = FilePicker.Create();
	private PhotinoInputRouter? _inputRouter;

	private int _width = 120;
	private int _height = 40;
	private ConsoleColor _fg = ConsoleColor.Gray;
	private ConsoleColor _bg = ConsoleColor.Black;
	private string? _lastInput;

	public override Task<bool> ConfirmAsync(string question, bool defaultAnswer = false)
	{
		var model = new BoolBox { Answer = defaultAnswer };
		var form = UiForm.Create(question, model);
		form.AddBool<BoolBox>("Answer", m => m.Answer, (m, v) => m.Answer = v)
			.WithHelp("True = Yes, False = No. ESC to cancel.");
		return ShowFormAsync(form).ContinueWith(t => !t.Result ? defaultAnswer : ((BoolBox)form.Model!).Answer);
	}

	public PhotinoUi(string title = "CSChat") { _title = title; }
	private string IndexHtmlPath => Path.Combine(AppContext.BaseDirectory, "UX/wwwroot", "index.html");
	private readonly string _title;

	private void NudgeWaitersAndStop()
	{
		_tcsKey?.TrySetResult(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

		// Complete channels to stop pumps
		try { _tx.Writer.TryComplete(); } catch { }
		try { _rx.Writer.TryComplete(); } catch { }
	}

	public override async Task RunAsync(Func<Task> appMain)
	{
		if (OperatingSystem.IsWindows())
		{
			var uiReady = new ManualResetEventSlim(false);
			var uiExited = new ManualResetEventSlim(false);

			_uiThread = new Thread(async () =>
			{
				await Init(uiReady, uiExited);
			});
			_uiThread.IsBackground = false;
			// Important: Set STA **before** Start on Windows to avoid races
			_uiThread.SetApartmentState(ApartmentState.STA);
			_uiThread.Start();
			uiReady.Wait();
			var appTask = Task.Run(appMain);

			uiExited.Wait();
			await appTask;
		}
		else
		{
			// --- macOS/Linux: run Photino on the **main thread** ---
			// We are already on the process' main thread here (Program.Main).
			// Move your app loop to a worker so the UI thread can block in WaitForClose().
			var appTask = Task.Run(appMain);
			await Init(appTask: appTask);
		}
	}

	private async Task Init(ManualResetEventSlim? uiReady = null, ManualResetEventSlim? uiExited = null, Task? appTask = null)
	{
		try
		{
			if (OperatingSystem.IsWindows())
			{
				Thread.CurrentThread.Name = "Photino UI (Windows STA)";
				// STA required for COM-based UI plumbing on Windows
				// (We are already running on the brand-new thread here.)
				if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
				{
					Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
				}
			}
			else
			{
				Thread.CurrentThread.Name = "Photino UI (Main thread)";
			}

			_win = new PhotinoWindow()
					.SetTitle(_title)
					.SetUseOsDefaultSize(true)
					.SetResizable(true)
					.SetDevToolsEnabled(true)  // Enable F12 developer tools
					.Center();

			_win.RegisterWindowClosingHandler((s, e) => { NudgeWaitersAndStop(); return false; });

			// Start inbound pump and register handler BEFORE loading HTML so window.external is available
			_rxPumpTask = Task.Run(PumpInboundAsync);
			_win.RegisterWebMessageReceivedHandler((sender, raw) =>
			{
				// Enqueue inbound messages for processing on background pump
				_rx.Writer.TryWrite(raw);
			});

			if (File.Exists(IndexHtmlPath))
			{
				_win.Load(IndexHtmlPath);
			}

			if (OperatingSystem.IsWindows()) { uiReady!.Set(); }
			// Block the main thread here
			_win.WaitForClose();
		}
		finally
		{
			NudgeWaitersAndStop();
			if (OperatingSystem.IsWindows())
			{
				uiExited!.Set();
			}
			else
			{
				await appTask!;
			}
		}
		return;
	}

	public override Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt)
	{
		var tcs = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

		void RunOnUi()
		{
			_ = _picker.ShowAsync(opt).ContinueWith(t =>
			{
				if (t.IsFaulted) tcs.TrySetException(t.Exception!.InnerException ?? t.Exception!);
				else tcs.TrySetResult(t.Result);
			}, TaskScheduler.Default);
		}

		var win = _win; // local capture
		if (win is not null)
		{
			try { win.Invoke(RunOnUi); }
			catch { RunOnUi(); }
		}
		else
		{
			RunOnUi();
		}

		return tcs.Task;
	}

	// Progress is now implemented in CUiBase using UiNodes
	// Photino renders Progress UiKind in SerializeNode method

	private void HandleInbound(string raw) => Log.Method(ctx =>
	{
		ctx.OnlyEmitOnFailure();
		try
		{
			var map = JSONParser.FromJson<Dictionary<string, object?>>(raw);
			if (map is null) return;

			string? S(params string[] keys)
			{
				foreach (var k in keys) if (map.TryGetValue(k, out var v) && v is not null) return Convert.ToString(v);
				return null;
			}
			bool B(params string[] keys)
			{
				foreach (var k in keys)
				{
					if (map.TryGetValue(k, out var v) && v is not null)
					{
						if (v is bool b) return b;
						if (bool.TryParse(Convert.ToString(v), out var bb)) return bb;
					}
				}
				return false;
			}
			int I(params string[] keys)
			{
				foreach (var k in keys)
				{
					if (map.TryGetValue(k, out var v) && v is not null &&
							int.TryParse(Convert.ToString(v), out var ii)) return ii;
				}
				return 0;
			}

			var type = S("type", "Type");
			if (string.IsNullOrWhiteSpace(type)) return;

			switch (type)
			{
				case "Ready":
					{
						Log.Method(ctx =>
						{
							ctx.Append(Log.Data.Message, "Received Ready from JS");
							ctx.Succeeded();
						});
						// Start outbound pump exactly once, then send an immediate ack and ticks
						if (System.Threading.Interlocked.Exchange(ref _txStarted, 1) == 0)
						{
							_txPumpTask = Task.Run(PumpAsync);
						}
						Post(new { type = "Debug", message = "Host Ack Ready" });
						_ = Task.Run(async () =>
						{
							for (int i = 1; i <= 3; i++)
							{
								await Task.Delay(500);
								Post(new { type = "Debug", message = $"Host Tick {i}" });
							}
						});
						break;
					}
				case "OpenExternal":
					{
						var url = S("url", "href", "Url", "Href");
						if (!string.IsNullOrWhiteSpace(url))
						{
							try
							{
								// Launch with the default handler (browser, mail client, etc.)
								Process.Start(new ProcessStartInfo
								{
									FileName = url,
									UseShellExecute = true
								});
							}
							catch (Exception ex)
							{
								// Optional: surface a tiny error toast/log if you want
								WriteLine($"Failed to open link: {ex.Message}");
							}
						}
						break;
					}

				case "CancelProgress":
					{
						var pid = S("id", "Id");
						if (!string.IsNullOrWhiteSpace(pid) && _progressMap.TryGetValue(pid, out var cts))
							try { cts.Cancel(); } catch { }
						break;
					}

				case "UserText":
					if (!string.IsNullOrWhiteSpace(S("text", "Text")))
						_lastInput = S("text", "Text");
					break;

				case "Key":
					{
						var key = S("key", "Key");
						var chr = S("char", "Char");
						var cki = MapToConsoleKeyInfo(key, chr, B("shift", "Shift"), B("ctrl", "Ctrl"), B("alt", "Alt"));
						_tcsKey?.TrySetResult(cki);
						
						// Also enqueue to the input router's key queue for TryReadKey
						_inputRouter?.EnqueueKey(cki);
						break;
					}

				case "Resize":
					var w = I("width", "Width"); if (w > 0) _width = w;
					var h = I("height", "Height"); if (h > 0) _height = h;
					break;

				case "Debug":
					{
						var msg = S("message", "msg", "text") ?? "<no message>";
						Log.Method(ctx =>
						{
							ctx.Append(Log.Data.Message, msg);
							ctx.Succeeded();
						});
						break;
					}

				case "PickFiles":
					{
						// payload: { type:"PickFiles", requestId, options:{ multi, filters, pathMode } }
						var reqId = S("requestId", "reqId") ?? Guid.NewGuid().ToString("n");
						var multi = B("multi");
						var mode = PathPickerMode.OpenExisting;
						List<string>? filters = null;
						if (map.TryGetValue("options", out var o) && o is Dictionary<string, object?> od)
						{
							if (od.TryGetValue("multi", out var mv) && mv is not null) multi = Convert.ToBoolean(mv);
							if (od.TryGetValue("filters", out var fv) && fv is IEnumerable<object?> arr)
								filters = arr.Select(x => Convert.ToString(x))
										.Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!;
							if (od.TryGetValue("pathMode", out var pv) && pv is not null)
							{
								var parsed = Convert.ToString(pv);
								if (!string.IsNullOrWhiteSpace(parsed) && Enum.TryParse<PathPickerMode>(parsed, ignoreCase: true, out var pm))
									mode = pm;
							}
						}

						var opt = new FilePickerOptions(multi, filters?.ToArray(), mode);

						void RunOnUi()
						{
							_ = _picker.ShowAsync(opt).ContinueWith(t =>
							{
								if (t.IsFaulted)
								{
									var ex = t.Exception!.InnerException ?? t.Exception!;
									Post(new { type = "PickFilesResult", requestId = reqId, error = ex.Message });
								}
								else
								{
									Post(new { type = "PickFilesResult", requestId = reqId, files = t.Result });
								}
							}, TaskScheduler.Default);
						}

						var win = _win;
						if (win is not null)
						{
							try { win.Invoke(RunOnUi); }
							catch { RunOnUi(); }
						}
						else
						{
							RunOnUi();
						}

						break;
					}

				case "ControlEvent":
					{
						// payload: { type:"ControlEvent", key:"node-key", name:"click"|"change"|"enter", value?:"..." }
						var key = S("key", "Key");
						var name = S("name", "Name");
						var value = S("value", "Value");

						if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
							break;

						// Route composer input/send-btn events to InputRouter if active
						// This handles the unified I/O stack for ChatSurface integration
						if (_inputRouter != null && (key == "input" || key == UiFrameKeys.SendButton))
						{
							_inputRouter.HandleControlEvent(key, name, value);
							break;
						}

						// (e.g., ReadInputWithFeaturesAsync before full InputRouter migration)
						if ((key == "input" && name == "enter") || (key == UiFrameKeys.SendButton && name == "click"))
						{
							if (!string.IsNullOrWhiteSpace(value))
								_lastInput = value;
							break;
						}

						// For all other ControlEvents, find and invoke the UiNode event handler
						var node = _uiTree.FindNode(key);
						if (node == null)
							break;

						// Map event name to handler property key (UiProperty)
						UiProperty? handlerProp = name switch
						{
							"click" => UiProperty.OnClick,
							"change" => UiProperty.OnChange,
							"enter" => UiProperty.OnEnter,
							"toggle" => UiProperty.OnToggle,
							"itemActivated" => UiProperty.OnItemActivated,
							_ => null
						};

						if (handlerProp.HasValue && node.Props.TryGetValue(handlerProp.Value, out var handlerObj) && handlerObj is UiHandler handler)
						{
							var evt = new UiEvent(key, name, value, null);
							// Invoke the handler asynchronously
							_ = Task.Run(async () =>
							{
								try
								{
									await handler(evt);
								}
								catch (Exception ex)
								{
									Log.Method(ctx => ctx.Failed($"Error in UiEvent handler for {key}.{name}", ex));
								}
							});
						}

						break;
					}
			}

			ctx.Succeeded();
		}
		catch (Exception ex)
		{
			// swallow, but log malformed payloads
			ctx.Failed($"malformed payload: {raw ?? "<null>"}", ex);
		}
	});

	// ---------- Input ----------
	public override IInputRouter GetInputRouter()
	{
		if (_inputRouter == null)
		{
			_inputRouter = new PhotinoInputRouter();
		}
		return _inputRouter;
	}

	public override void RenderTable(Table table, string? title = null)
	{
		var sb = new StringBuilder();

		// headers
		if (table.Headers.Count > 0)
		{
			sb.Append("| ");
			foreach (var h in table.Headers) sb.Append(Escape(h)).Append(" | ");
			sb.AppendLine();
			sb.Append("| ");
			foreach (var _ in table.Headers) sb.Append("--- | ");
			sb.AppendLine();
		}
		// rows
		foreach (var r in table.Rows)
		{
			sb.Append("| ");
			foreach (var c in r) sb.Append(Escape(c)).Append(" | ");
			sb.AppendLine();
		}

		var md = sb.ToString();
		if (!string.IsNullOrWhiteSpace(title)) md = $"### {Escape(title)}\n\n" + md;
		var message = new ChatMessage { Role = Roles.Tool, Content = md };
		Program.Context.AddToolMessage(md);
		RenderChatMessage(message);
		return;

		string Escape(string s) => s?.Replace("\n", " ").Replace("\r", " ").Replace("|", "\\|") ?? string.Empty;
	}

	public override void RenderReport(Report report)
	{
		// Photino renders as markdown tool bubble
		var md = report?.ToMarkdown() ?? "";
		var message = new ChatMessage { Role = Roles.Tool, Content = md };
		Program.Context.AddToolMessage(md);
		RenderChatMessage(message);
	}

	public override ConsoleKeyInfo ReadKey(bool intercept)
			=> ReadKeyInternalAsync(intercept).GetAwaiter().GetResult();

	internal Task<ConsoleKeyInfo> ReadKeyInternalAsync(bool intercept)
	{
		_tcsKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Post(new { type = "CaptureKey", intercept });
		return _tcsKey.Task;
	}

	// ---------- Output ----------

	public override void Write(string text) => Post(new { type = "ConsoleWrite", text = text ?? "" });
	public override void WriteLine(string? text = null) => Post(new { type = "ConsoleWriteLine", text = text ?? "" });
	public override void Clear() => Post(new { type = "Clear" });

	// ---------- Console-ish ----------
	public override int CursorTop => 0;
	public override int Width => _width;
	public override int Height => _height;
	public override bool KeyAvailable => false;
	public override bool IsOutputRedirected => true;
	public override void SetCursorPosition(int left, int top) { }
	public override ConsoleColor ForegroundColor { get => _fg; set => _fg = value; }
	public override ConsoleColor BackgroundColor { get => _bg; set => _bg = value; }
	public override void ResetColor() { _fg = ConsoleColor.Gray; _bg = ConsoleColor.Black; }

	// ---------- Internals ----------
	private void Post(object payload)
	{
		// Serialize and enqueue; the pump will deliver when the webview is ready
		var json = payload.ToJson();
		_tx.Writer.TryWrite(json);
	}

	private async Task PumpAsync()
	{
		await foreach (var msg in _tx.Reader.ReadAllAsync())
		{
			var win = _win;
			if (win is null) continue;
			try
			{
				win.Invoke(() => win.SendWebMessage(msg));
			}
			catch
			{
				// UI going down or unavailable
			}
		}
	}

	private async Task PumpInboundAsync()
	{
		await foreach (var raw in _rx.Reader.ReadAllAsync())
		{
			HandleInbound(raw);
		}
	}

	private static ConsoleKeyInfo MapToConsoleKeyInfo(string? key, string? ch, bool shift, bool ctrl, bool alt)
	{
		char c = default;
		if (!string.IsNullOrEmpty(ch)) c = ch[0];

		var parsed = (key ?? string.Empty) switch
		{
			"Enter" => ConsoleKey.Enter,
			"Escape" => ConsoleKey.Escape,
			"Backspace" => ConsoleKey.Backspace,
			"Tab" => ConsoleKey.Tab,
			"Up" or "ArrowUp" => ConsoleKey.UpArrow,
			"Down" or "ArrowDown" => ConsoleKey.DownArrow,
			"Left" or "ArrowLeft" => ConsoleKey.LeftArrow,
			"Right" or "ArrowRight" => ConsoleKey.RightArrow,
			"Home" => ConsoleKey.Home,
			"End" => ConsoleKey.End,
			"PageUp" => ConsoleKey.PageUp,
			"PageDown" => ConsoleKey.PageDown,
			"Delete" => ConsoleKey.Delete,
			_ when !string.IsNullOrEmpty(key) && key!.Length == 1 &&
					Enum.TryParse<ConsoleKey>(key.ToUpper(), out var pk) => pk,
			_ => default
		};

		return new ConsoleKeyInfo(c, parsed, shift, alt, ctrl);
	}

	// Declarative control layer - override base implementations for Photino-specific rendering
	
	protected override Task PostSetRootAsync(UiNode root, UiControlOptions options)
	{
		// Instead of sending MountControl (which has JSONWriter serialization issues),
		// we'll convert the tree into a series of ReplaceOp patches
		// JavaScript already has frame.root created in initializeRootContainer()
		
		// Replace frame.root with the new root tree
		var patch = new UiPatch(new ReplaceOp(UiFrameKeys.Root, root));
		
		// Send the patch to mount the tree
		var result = PostPatchAsync(patch);

		return result;
	}

	protected override Task PostPatchAsync(UiPatch patch)
	{
		// Queue patches - they will be flushed once _ready is true
		// Build ops list manually to avoid any LINQ generic inference issues (previous CS0411)
		var ops = new List<object>(patch.Ops.Length);
		foreach (var op in patch.Ops)
		{
			switch (op)
			{
				case ReplaceOp replaceOp:
					ops.Add(new { type = "replace", key = replaceOp.Key, node = SerializeNode(replaceOp.Node) });
					break;
				case UpdatePropsOp updatePropsOp:
					// Convert UiProperty enum keys to strings
					var propsDict = new Dictionary<string, object?>();
					foreach (var kvp in updatePropsOp.Props)
					{
						propsDict[kvp.Key.ToString()] = kvp.Value;
					}
					ops.Add(new { type = "updateProps", key = updatePropsOp.Key, props = propsDict });
					break;
				case InsertChildOp insertChildOp:
					ops.Add(new { type = "insertChild", parentKey = insertChildOp.ParentKey, index = insertChildOp.Index, node = SerializeNode(insertChildOp.Node) });
					break;
				case RemoveOp removeOp:
					ops.Add(new { type = "remove", key = removeOp.Key });
					break;
				default:
					throw new InvalidOperationException($"Unknown operation type: {op.GetType().Name}");
			}
		}

		// Send patch message to the web view
		var json = new { type = "PatchControl", patch = new { ops } };
		Post(json);
		return Task.CompletedTask;
	}

	protected override Task PostFocusAsync(string key)
	{
		// Queue the focus message - it will be flushed once _ready is true
		// Send focus message to the web view
		Post(new { type = "FocusControl", key });

		return Task.CompletedTask;
	}

	/// <summary>
	/// Serializes a UiNode to an object suitable for JSON transmission to the web view
	/// </summary>
	private object SerializeNode(UiNode node)
	{
		// Build children list
		var childList = new List<object>(node.Children.Count);
		foreach (var c in node.Children)
		{
			childList.Add(SerializeNode(c));
		}

		// Serialize props as a dictionary with string keys (property names)
		var propsDict = new Dictionary<string, object?>();
		foreach (var kvp in node.Props)
		{
			var propName = kvp.Key.ToString(); // Convert enum to string
			propsDict[propName] = kvp.Value;
		}

		// Serialize styles if present
		var stylesDict = new Dictionary<string, object?>();
		if (node.Styles != null && node.Styles != UiStyles.Empty)
		{
			foreach (var kvp in node.Styles.Values)
			{
				var styleName = kvp.Key.ToString();
				stylesDict[styleName] = kvp.Value;
			}
		}

		return new
		{
			key = node.Key,
			kind = node.Kind.ToString(),
			props = propsDict,
			styles = stylesDict.Count > 0 ? stylesDict : null,
			children = childList
		};
	}
}

/// <summary>
/// Photino implementation of IInputRouter
/// </summary>
public sealed class PhotinoInputRouter : IInputRouter
{
    private string _currentInputText = "";
    private readonly Queue<ConsoleKeyInfo> _keyQueue = new();
    private ConsoleKeyInfo _lastKey;

	/// <summary>
	/// Non-blocking poll for key input. For Photino, this returns the next synthetic key from the queue
	/// or null if no keys are available.
	/// </summary>
	public ConsoleKeyInfo? TryReadKey()
	{
		lock (_keyQueue)
		{
			if (_keyQueue.Count > 0)
			{
				_lastKey = _keyQueue.Dequeue();
				return _lastKey;
			}
		}
		return null;
	}

    /// <summary>
    /// Enqueues a synthetic key (called from web view events)
    /// </summary>
    internal void EnqueueKey(ConsoleKeyInfo key)
    {
        lock (_keyQueue)
        {
            _keyQueue.Enqueue(key);
        }
    }

    /// <summary>
    /// Called when a ControlEvent is received from the web view
    /// This is wired up in PhotinoUi.HandleInbound (ControlEvent case)
    /// </summary>
    public void HandleControlEvent(string key, string eventName, string? value)
    {
        // Handle input change events
        if (eventName == "change" && key == "input")
        {
            _currentInputText = value ?? "";
        }
        
        // Handle submit events (Enter key or Send button click)
		else if ((key == "input" && eventName == "enter") || (key == UiFrameKeys.SendButton && eventName == "click"))
        {
            var textToSubmit = _currentInputText;
            
            // Also enqueue synthetic Enter key for TryReadKey consumers
            EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        }
        
        // Handle cancel/escape
        else if (eventName == "escape")
        {
            EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        }
    }
}
