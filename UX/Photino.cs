using System;
using System.IO;
using System.Linq;
using Photino.NET;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public sealed class PhotinoUi : IUi
{
	private PhotinoWindow? _win;
	private Thread? _uiThread;
	private readonly Queue<string> _outbox = new();
	private readonly object _gate = new();

	private volatile bool _ready = false;      // page JS has started and pinged us
	private volatile bool _uiAlive = false;    // UI thread is up

	private TaskCompletionSource<string?>? _tcsReadLine;
	private TaskCompletionSource<string?>? _tcsMenu;
	private TaskCompletionSource<ConsoleKeyInfo>? _tcsKey;

	private int _width = 120;
	private int _height = 40;
	private ConsoleColor _fg = ConsoleColor.Gray;
	private ConsoleColor _bg = ConsoleColor.Black;
	private string? _lastInput;

	public PhotinoUi(string title = "CSChat") { _title = title; }
	private string IndexHtmlPath => Path.Combine(AppContext.BaseDirectory, "UX/wwwroot", "index.html");
	private readonly string _title;

	private void NudgeWaitersAndStop()
	{
		_uiAlive = false;
		_tcsReadLine?.TrySetResult(null);
		_tcsMenu?.TrySetResult(null);
		_tcsKey?.TrySetResult(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
	}

	// ----------------- IUi.RunAsync: spin up UI thread and pump Photino -----------------
	public async Task RunAsync(Func<Task> appMain)
	{
		if (OperatingSystem.IsWindows())
		{
			// --- Windows: run Photino on a dedicated STA UI thread ---
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
				case "UserText":
					_tcsReadLine?.TrySetResult(S("text", "Text"));
					if (!string.IsNullOrWhiteSpace(S("text", "Text")))
						_lastInput = S("text", "Text");
					break;

				case "MenuResult":
					_tcsMenu?.TrySetResult(S("text", "Text"));
					break;

				case "Key":
					{
						var key = S("key", "Key");
						var chr = S("char", "Char");
						var cki = MapToConsoleKeyInfo(key, chr, B("shift", "Shift"), B("ctrl", "Ctrl"), B("alt", "Alt"));
						_tcsKey?.TrySetResult(cki);
						break;
					}

				case "Resize":
					var w = I("width", "Width"); if (w > 0) _width = w;
					var h = I("height", "Height"); if (h > 0) _height = h;
					break;
			}

			ctx.Succeeded();
		}
		catch (Exception ex)
		{
			// swallow, but log malformed payloads
			ctx.Failed($"malformed payload: {raw ?? "<null>"}", ex);
		}
	});

	// ----------------- Input -----------------

	public async Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager)
	{
		_tcsReadLine = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Post(new { type = "FocusInput" });

		// ESC opens menu (non-blocking)
		_ = Task.Run(async () =>
		{
			var keyWait = ReadKeyInternalAsync(intercept: true);
			while (_uiAlive)
			{
				var k = await keyWait;
				if (k.Key == ConsoleKey.Escape)
				{
					var result = await commandManager.Action();
					if (result == Command.Result.Failed) WriteLine("Command failed.");
					Post(new { type = "FocusInput" });
				}
				keyWait = ReadKeyInternalAsync(intercept: true);
			}
		});

		var text = await _tcsReadLine.Task;  // null when window closes
		return string.IsNullOrWhiteSpace(text) ? null : text;
	}

	public async Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory)
	{
		var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_tcsReadLine = tcs;
		Post(new { type = "PromptPath", dir = isDirectory });
		return await tcs.Task;
	}

	public string? RenderMenu(string header, List<string> choices, int selected = 0)
	{
		_tcsMenu = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Post(new { type = "ShowMenu", header, choices, selected });
		return _tcsMenu.Task.GetAwaiter().GetResult();
	}

	public string? ReadLineWithHistory()
	{
		_tcsReadLine = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Post(new { type = "PromptLine", placeholder = _lastInput ?? "" });
		var s = _tcsReadLine.Task.GetAwaiter().GetResult();
		if (!string.IsNullOrWhiteSpace(s)) _lastInput = s;
		return string.IsNullOrWhiteSpace(s) ? null : s;
	}

	public string ReadLine()
	{
		_tcsReadLine = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Post(new { type = "PromptLine", placeholder = "" });
		var s = _tcsReadLine.Task.GetAwaiter().GetResult() ?? "";
		if (!string.IsNullOrWhiteSpace(s)) _lastInput = s;
		return s;
	}

	public ConsoleKeyInfo ReadKey(bool intercept)
			=> ReadKeyInternalAsync(intercept).GetAwaiter().GetResult();

	private Task<ConsoleKeyInfo> ReadKeyInternalAsync(bool intercept)
	{
		_tcsKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Post(new { type = "CaptureKey", intercept });
		return _tcsKey.Task;
	}

	// ----------------- Output -----------------

	public void RenderChatMessage(ChatMessage message)
			=> Post(new { type = "AppendMessage", role = message.Role.ToString(), content = message.Content, timestamp = message.CreatedAt });

	public void RenderChatHistory(IEnumerable<ChatMessage> messages)
			=> Post(new
			{
				type = "RenderHistory",
				items = messages.Select(m => new { role = m.Role.ToString(), content = m.Content, timestamp = m.CreatedAt }).ToList()
			});

	public void Write(string text) => Post(new { type = "ConsoleWrite", text = text ?? "" });
	public void WriteLine(string? text = null) => Post(new { type = "ConsoleWriteLine", text = text ?? "" });
	public void Clear() => Post(new { type = "Clear" });
	public void BeginUpdate() { /* no-op */}
	public void EndUpdate() => SafeFlush();

	// ----------------- Console-ish props -----------------
	public int CursorTop => 0;
	public int CursorLeft => 0;
	public int Width => _width;
	public int Height => _height;
	public bool KeyAvailable => false;
	public bool IsOutputRedirected => true;
	public bool CursorVisible { set { } }
	public void SetCursorPosition(int left, int top) { }
	public ConsoleColor ForegroundColor { get => _fg; set => _fg = value; }
	public ConsoleColor BackgroundColor { get => _bg; set => _bg = value; }
	public void ResetColor() { _fg = ConsoleColor.Gray; _bg = ConsoleColor.Black; }

	// ----------------- Internals -----------------

	private void Post(object payload)
	{
		var json = payload.ToJson();
		lock (_gate) { _outbox.Enqueue(json); }
		SafeFlush();
	}

	private void SafeFlush()
	{
		if (!_uiAlive || _win is null || !_ready) return;

		// Always marshal to the UI thread before touching _win.
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
			// If UI is going down, leave messages in queue; read operations will be nudged to finish.
		}
	}

	private static ConsoleKeyInfo MapToConsoleKeyInfo(string? key, string? ch, bool shift, bool ctrl, bool alt)
	{
		char c = default;
		if (!string.IsNullOrEmpty(ch)) c = ch[0];

		var parsed = (key ?? string.Empty) switch
		{
			"Enter"                 => ConsoleKey.Enter,
			"Escape"                => ConsoleKey.Escape,
			"Backspace"             => ConsoleKey.Backspace,
			"Tab"                   => ConsoleKey.Tab,
			"Up" or "ArrowUp"       => ConsoleKey.UpArrow,
			"Down" or "ArrowDown"   => ConsoleKey.DownArrow,
			"Left" or "ArrowLeft"   => ConsoleKey.LeftArrow,
			"Right" or "ArrowRight" => ConsoleKey.RightArrow,
			"Home"                  => ConsoleKey.Home,
			"End"                   => ConsoleKey.End,
			"PageUp"                => ConsoleKey.PageUp,
			"PageDown"              => ConsoleKey.PageDown,
			"Delete"                => ConsoleKey.Delete,
			_ when !string.IsNullOrEmpty(key) && key!.Length == 1 &&
					Enum.TryParse<ConsoleKey>(key.ToUpper(), out var pk) => pk,
			_ => default
		};

		return new ConsoleKeyInfo(c, parsed, shift, alt, ctrl);
	}
}