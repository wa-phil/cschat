using System;
using System.IO;
using System.Linq;
using Photino.NET;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
    private readonly string _title;
    private string IndexHtmlPath => Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
    
    private void NudgeWaitersAndStop()
    {
        _uiAlive = false;
        _tcsReadLine?.TrySetResult(null);
        _tcsMenu?.TrySetResult(null);
        _tcsKey?.TrySetResult(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
    }

    // ----------------- IUi.RunAsync: spin up STA UI thread and pump Photino -----------------
    public async Task RunAsync(Func<Task> appMain)
    {
      // Start the UI thread (STA) where we will both create the window and call WaitForClose().
      var uiReady = new ManualResetEventSlim(false);
      var uiExited = new ManualResetEventSlim(false);

      _uiThread = new Thread(() =>
      {
        try
        {
          Thread.CurrentThread.Name = "Photino UI";
          // STA is required by Photino
          if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

          _win = new PhotinoWindow()
                  .SetTitle(_title)
                  .SetUseOsDefaultSize(true)
                  .SetResizable(true)
                  .Center();

          _win.RegisterWindowClosingHandler((s, e) =>
          {
              // Do not cancel; just let close proceed, and unblock any awaits.
              NudgeWaitersAndStop();
              return false; // do not cancel;
          });

          // Load HTML
          if (File.Exists(IndexHtmlPath))
            _win.Load(new Uri("file://" + IndexHtmlPath).ToString());
          else
            _win.LoadRawString(MinimalHtml);

          // Inbound messages from JS
          _win.RegisterWebMessageReceivedHandler((sender, raw) =>
          {
            try
            {
                if (!_ready) { _ready = true; SafeFlush(); }

                // raw is a JSON string from JS
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
                        var key  = S("key",  "Key");
                        var chr  = S("char", "Char");
                        var cki = MapToConsoleKeyInfo(key, chr, B("shift","Shift"), B("ctrl","Ctrl"), B("alt","Alt"));
                        _tcsKey?.TrySetResult(cki);
                        break;
                    }

                    case "Resize":
                        var w = I("width","Width"); if (w > 0) _width = w;
                        var h = I("height","Height"); if (h > 0) _height = h;
                        break;

                    // ignore unknowns
                }
            }
            catch
            {
                // swallow malformed payloads
            }
          });

          _uiAlive = true;
          uiReady.Set();

          // This must run on the same thread that created the window.
          _win.WaitForClose();
        }
        catch
        {
          // If Photino throws, make sure waiters get nudged so the app can unwind.
          _tcsReadLine?.TrySetResult(null);
          _tcsMenu?.TrySetResult(null);
          _tcsKey?.TrySetResult(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
          throw;
        }
        finally
        {
          NudgeWaitersAndStop();   
          _uiAlive = false;
          uiExited.Set();
      }
      });

      _uiThread.SetApartmentState(ApartmentState.STA);
      _uiThread.IsBackground = false; // keep process alive while window is open
      _uiThread.Start();

      // Wait until UI is ready enough to accept messages.
      uiReady.Wait();

      // Run your app loop on a worker.
      var appTask = Task.Run(appMain);

      // Block here until the UI exits.
      uiExited.Wait();

      // Let the app loop clean up / finish.
      await appTask;
    }

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
                    Post(new { type = "ShowToast", text = "[press ESC to open menu]" });
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

    public void Write(string text) => Post(new { type = "ConsoleWrite", text });
    public void WriteLine(string? text = null) => Post(new { type = "ConsoleWriteLine", text });
    public void Clear() => Post(new { type = "Clear" });

    public void BeginUpdate() => Post(new { type = "BeginBatch" });
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
        var json = JsonSerializer.Serialize(payload);
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

    private sealed class Inbound
    {
        public string Type { get; set; } = "";
        public string? Text { get; set; }
        public string? Key { get; set; }
        public string? Char { get; set; }
        public bool Shift { get; set; }
        public bool Ctrl { get; set; }
        public bool Alt { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    // Same minimal HTML you already had
    private const string MinimalHtml = @"<!doctype html>
<html>
<head>
<meta charset='utf-8'/>
<title>CSChat</title>
<style>
  :root { --bg:#111; --fg:#eee; }
  body { font-family: system-ui, sans-serif; margin:0; height:100vh; display:grid; grid-template-rows:auto 1fr auto; }
  header { padding: 8px 12px; background:var(--bg); color:var(--fg); }
  #main  { display:flex; flex-direction:column; }
  #console { flex:1; overflow:auto; padding:12px; white-space:pre-wrap;
             font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, 'Liberation Mono', monospace; }
  #composer { display:flex; padding:8px; gap:8px; border-top:1px solid #ddd; }
  #t { flex:1; padding:8px; font: inherit; }
  pre { margin:0 0 8px 0; white-space:pre-wrap; }
  .user { color:#08f; } .assistant { color:#0a0; } .tool { color:#aa0 } .system { color:#666; }
</style>
<body>
  <header>CSChat</header>
  <div id='main'>
    <div id='console'></div> <!-- big scrollable console/log -->
  </div>
  <div id='composer'>
    <input id='t' placeholder='Type and press Enter'/>
    <button id='send'>Send</button>
  </div>
<script>
  const consoleEl = document.getElementById('console');
  const t = document.getElementById('t');
  const send = document.getElementById('send');

  function post(obj) { try { window.external.sendMessage(JSON.stringify(obj)); } catch {} }

  function cwrite(s)     { consoleEl.textContent += (s ?? ''); consoleEl.scrollTop = consoleEl.scrollHeight; }
  function cwriteline(s) { cwrite((s ?? '') + '\n'); }

  function add(kind, text) {
    const el = document.createElement('pre');
    el.className = kind;
    el.textContent = text;
    consoleEl.appendChild(el);
    consoleEl.scrollTop = consoleEl.scrollHeight;
  }

  function submit() {
    const v = t.value.trim();
    if (!v) return;
    post({ type:'UserText', text:v });
    t.value = '';
  }
  send.onclick = submit;

  t.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); submit(); }
    else post({ type:'Key', key:e.key, char:e.key.length===1? e.key : '', shift:e.shiftKey, ctrl:e.ctrlKey, alt:e.altKey });
  });
  document.addEventListener('keydown', (e) => {
    if (e.target === t && e.key === 'Enter' && !e.shiftKey) return;
    post({ type:'Key', key:e.key, char:e.key.length===1? e.key : '', shift:e.shiftKey, ctrl:e.ctrlKey, alt:e.altKey });
  });

  // Robust receiver (string or object), renders to the big console
  window.external.receiveMessage = raw => {
    try {
      const msg = (typeof raw === 'string') ? JSON.parse(raw) : raw;
      if (!msg || !msg.type) return;

      switch (msg.type) {
        case 'AppendMessage':
          add((msg.role||'').toLowerCase(), `[${msg.role}] ${msg.content||''}`);
          break;
        case 'RenderHistory':
          consoleEl.innerHTML = '';
          (msg.items||[]).forEach(x => add((x.role||'').toLowerCase(), `[${x.role}] ${x.content||''}`));
          break;
        case 'ConsoleWrite':
          cwrite(msg.text||'');
          break;
        case 'ConsoleWriteLine':
          cwriteline(msg.text||'');
          break;
        case 'ShowMenu': {
          const choice = prompt((msg.header||'Menu') + '\n\n' + (msg.choices||[]).map((c,i)=>`${i+1}. ${c}`).join('\n'));
          const idx = Math.max(0, Math.min((msg.choices||[]).length-1, (parseInt(choice||'')-1)|0));
          post({ type:'MenuResult', text: (msg.choices||[])[idx] || null });
          break;
        }
        case 'PromptLine': {
          const ans = prompt('Input:', msg.placeholder||'') || '';
          post({ type:'UserText', text: ans });
          break;
        }
        case 'PromptPath': {
          const ans = prompt('Path:', '') || '';
          post({ type:'UserText', text: ans });
          break;
        }
        case 'FocusInput':
          t.focus(); break;
        case 'BeginBatch':
          break;
        case 'Clear':
          consoleEl.innerHTML = ''; break;
        case 'ShowToast':
          // (optional) could implement a transient banner here
          cwriteline((msg.text||''));
          break;
      }
    } catch (e) {
      cwriteline(`RX error: ${e.message}`);
    }
  };

  // Initial ping → enables .NET flush
  const ping = () => post({ type:'Resize', width: Math.floor(window.innerWidth/8), height: Math.floor(window.innerHeight/18) });
  new ResizeObserver(ping).observe(document.body);
  ping();

  cwriteline('UI ready...');
  t.focus();
</script>
</body>
</html>";
}