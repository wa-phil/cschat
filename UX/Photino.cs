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

public sealed class PhotinoUi : CUiBase
{
	private class BoolBox { public bool Answer { get; set; } }
	private PhotinoWindow? _win;
	private Thread? _uiThread;
	private readonly Queue<string> _outbox = new();
	private readonly object _gate = new();

	private volatile bool _ready = false;
	private volatile bool _uiAlive = false;

	private TaskCompletionSource<ConsoleKeyInfo>? _tcsKey;
	private TaskCompletionSource<Dictionary<string, string?>?>? _tcsForm;

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
		_uiAlive = false;
		_tcsKey?.TrySetResult(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
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
					.Center();

			_win.RegisterWindowClosingHandler((s, e) => { NudgeWaitersAndStop(); return false; });

			if (File.Exists(IndexHtmlPath))
			{
				_win.Load(IndexHtmlPath);
			}

			_win.RegisterWebMessageReceivedHandler((sender, raw) => HandleInbound(raw));

			_uiAlive = true;
			if (OperatingSystem.IsWindows()) { uiReady!.Set(); }
			// Block the main thread here
			_win.WaitForClose();
		}
		finally
		{
			NudgeWaitersAndStop();
			_uiAlive = false;
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

	private readonly ConcurrentDictionary<string, CancellationTokenSource> _progressMap = new();

	public override string StartProgress(string title, CancellationTokenSource cts)
	{
		var id = Guid.NewGuid().ToString("n");
		Post(new { type = "StartProgress", id, title, cancellable = true });
		_progressMap[id] = cts;

		return id;
	}

	public override void UpdateProgress(string id, ProgressSnapshot snapshot)
	{
		Post(new
		{
			type = "UpdateProgress",
			id,
			items = snapshot.Items.Select(x => new { name = x.name, percent = x.percent, state = x.state.ToString(), note = x.note, done = x.steps.done, total = x.steps.total }).ToList(),
			stats = new { snapshot.Stats.running, snapshot.Stats.queued, snapshot.Stats.completed, snapshot.Stats.failed, snapshot.Stats.canceled },
			eta = snapshot.EtaHint,
			active = snapshot.IsActive
		});
	}

	public override void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown)
	{
		Post(new { type = "CompleteProgress", id, artifact = artifactMarkdown });
		_progressMap.TryRemove(id, out _);
	}

	private void HandleInbound(string raw) => Log.Method(ctx =>
	{
		ctx.OnlyEmitOnFailure();
		try
		{
			if (!_ready)
			{
				_ready = true; SafeFlush();
				SafeFlush();
			}

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

				case "FormResult":
					{
						// map: { ok: bool, values?: { "Label": "stringValue", ... } }
						bool ok = B("ok", "OK");
						if (!ok) { _tcsForm?.TrySetResult(null); break; }

						var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
						if (map.TryGetValue("values", out var v) && v is Dictionary<string, object?> dv)
						{
							foreach (var kv in dv) dict[kv.Key] = Convert.ToString(kv.Value);
						}
						_tcsForm?.TrySetResult(dict);
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
						if (_inputRouter != null && (key == "input" || key == "send-btn"))
						{
							_inputRouter.HandleControlEvent(key, name, value);
							break;
						}

						// (e.g., ReadInputWithFeaturesAsync before full InputRouter migration)
						if ((key == "input" && name == "enter") || (key == "send-btn" && name == "click"))
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

	public override string? RenderMenu(string header, List<string> choices, int selected = 0)
	{
		// Use MenuOverlay for UiNode-based menu rendering
		// This is a synchronous wrapper around the async ShowAsync method
		return MenuOverlay.ShowAsync(this, header, choices, selected).GetAwaiter().GetResult();
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
	public override void RenderChatMessage(ChatMessage message)
	{
		// Use ChatSurface to render the message via patch
		// Get current message count to determine the index
		var currentMessages = Program.Context?.Messages(InluceSystemMessage: false).ToList() ?? new List<ChatMessage>();
		var index = currentMessages.Count > 0 ? currentMessages.Count - 1 : 0;
		
		// Apply patch to append the message
		var patch = ChatSurface.AppendMessage(message, index);
		PatchAsync(patch).GetAwaiter().GetResult();
	}

	public override void RenderChatHistory(IEnumerable<ChatMessage> messages)
	{
		// Use ChatSurface to render all messages via patch
		var messageList = messages.ToList();
		
		// Apply patch to update all messages
		var patch = ChatSurface.UpdateMessages(messageList);
		PatchAsync(patch).GetAwaiter().GetResult();
	}

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
		var json = payload.ToJson();
		lock (_gate) { _outbox.Enqueue(json); }
		SafeFlush();
	}

	private void SafeFlush()
	{
		if (!_uiAlive || _win is null || !_ready) return;
		try
		{
			_win.Invoke(() =>
			{
				lock (_gate)
				{
					while (_outbox.Count > 0)
					{
						var json = _outbox.Dequeue();
						_win.SendWebMessage(json);
					}
				}
			});
		}
		catch
		{
			// UI going down
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
		if (!_ready)
			throw new PlatformNotReadyException("Photino UI is not initialized");

		// Send mount message to the web view
		Post(new
		{
			type = "MountControl",
			tree = SerializeNode(root),
			options = new
			{
				trapKeys = options.TrapKeys,
				initialFocusKey = options.InitialFocusKey
			}
		});

		return Task.CompletedTask;
	}

	protected override Task PostPatchAsync(UiPatch patch)
	{
		if (!_ready)
			throw new PlatformNotReadyException("Photino UI is not initialized");

		// Serialize patch operations for the web view
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
					ops.Add(new { type = "updateProps", key = updatePropsOp.Key, props = updatePropsOp.Props });
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
		Post(new { type = "PatchControl", patch = new { ops } });

		return Task.CompletedTask;
	}

	protected override Task PostFocusAsync(string key)
	{
		if (!_ready)
			throw new PlatformNotReadyException("Photino UI is not initialized");

		// Send focus message to the web view
		Post(new { type = "FocusControl", key });

		return Task.CompletedTask;
	}

	/// <summary>
	/// Serializes a UiNode to an object suitable for JSON transmission
	/// </summary>
	private object SerializeNode(UiNode node)
	{
		// Build children list manually to avoid any LINQ generic inference edge cases (CS0411)
		var childList = new List<object>(node.Children.Count);
		foreach (var c in node.Children)
		{
			childList.Add(SerializeNode(c));
		}
		return new
		{
			key = node.Key,
			kind = node.Kind.ToString(),
			props = node.Props,
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
        else if ((key == "input" && eventName == "enter") || (key == "send-btn" && eventName == "click"))
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
