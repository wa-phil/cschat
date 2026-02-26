# UX

Located in `/UX/`. Provides the `IUi` abstraction over two concrete implementations: a classic console (`Terminal`) and a web-based GUI (`PhotinoUi`).

## IUi Interface

**File:** `UX/IUI.cs`

```csharp
public interface IUi
{
    Task<bool> ShowFormAsync(UiForm form);
    Task<bool> ConfirmAsync(string question, bool defaultAnswer = false);
    Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt);

    void RenderTable(Table table, string? title = null);
    void RenderReport(Report report);
    IRealtimeWriter BeginRealtime(string title);

    string StartProgress(string title, CancellationTokenSource cts);
    void UpdateProgress(string id, ProgressSnapshot snapshot);
    void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown);

    Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory);
    Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager);
    string? RenderMenu(string header, List<string> choices, int selected = 0);
    ConsoleKeyInfo ReadKey(bool intercept);

    void RenderChatMessage(ChatMessage message);
    void RenderChatHistory(IEnumerable<ChatMessage> messages);

    // Console-like low-level methods: Write, WriteLine, Clear, Width, Height, ForegroundColor, etc.
    Task RunAsync(Func<Task> appMain);
}
```

`IRealtimeWriter` is a disposable write-through object returned by `BeginRealtime(title)`. Both implementations use it to emit incremental output during long-running operations.

## UiForm

**File:** `UX/IUI.cs` — `UiForm` and `UiField<TModel, TValue>`

A strongly-typed form builder for collecting structured user input. Supports fluent field construction:

```csharp
var form = UiForm.Create("Edit Settings", myModel);
form.AddString<MyModel>("Host", m => m.Host, (m, v) => m.Host = v)
    .WithHelp("LLM server URL")
    .WithPlaceholder("http://localhost:11434");
form.AddInt<MyModel>("Max Tokens", m => m.MaxTokens, (m, v) => m.MaxTokens = v)
    .IntBounds(min: 1, max: 128000);

if (await Program.ui.ShowFormAsync(form))
{
    var updated = (MyModel)form.Model!;
}
```

### Field Types

| Method | `UiFieldKind` | Notes |
|--------|--------------|-------|
| `AddString` | `String` | Single-line text |
| `AddText` | `Text` | Multi-line text |
| `AddPassword` | `Password` | Hidden input |
| `AddInt` | `Number` | Integer with optional bounds |
| `AddFloat` / `AddDouble` / `AddDecimal` | `Decimal` | Floating point |
| `AddLong` | `Long` | 64-bit integer |
| `AddBool` | `Bool` | True/false |
| `AddGuid` | `Guid` | UUID |
| `AddEnum<TEnum>` | `Enum` | Enum values shown as dropdown |
| `AddChoice` | `Enum` | String dropdown from an explicit list |
| `AddDate` | `Date` | Date picker |
| `AddTime` | `Time` | Time picker |
| `AddPath` | `Path` | File system path |
| `AddList<TItem>` | `Array` | List of simple values (JSON-encoded) |

All fields support fluent modifiers: `.WithHelp()`, `.MakeOptional()`, `.WithPlaceholder()`, `.WithRegex(pattern, msg)`, `.IntBounds(min, max)`, `.WithPathMode(mode)`.

`UiForm.Create(title, original)` clones the model via JSON round-trip so mutations do not affect the original until confirmed.

## Terminal

**File:** `UX/Terminal.cs`

Console-based UI. All output goes to `Console`. Runs entirely on the calling thread (`RunAsync` just calls `appMain()`).

### RenderMenu

Clears the screen, displays a header, and renders a scrollable filtered menu:
- **Arrow keys** — navigate items
- **Page Up/Down, Home, End** — scroll
- **Number keys 1–9** — quick-select (when ≤ 10 items)
- **Printable characters** — incremental filter (hides non-matching items)
- **Enter** — confirm selection
- **Escape** — cancel (returns `null`)

### ReadInputWithFeaturesAsync

The main chat input loop:
- **Enter** — submit message
- **Shift+Enter** — soft newline (multi-line input)
- **Up Arrow** — recall last input
- **Escape** — open the command menu

### RenderChatMessage

Renders timestamped role-colored output:

| Role | Color |
|------|-------|
| User | Cyan |
| Assistant | Green |
| Tool | Yellow (content in DarkGray) |
| System | DarkBlue (content in DarkGray) |

### Progress Bar

Nested `Progress` inner class renders a boxed header with per-item progress bars showing name, percentage, step count, and state glyph (▶ running, ✓ completed, ✖ failed, ■ canceled). ESC during progress cancels via the passed `CancellationTokenSource`.

## PhotinoUi

**File:** `UX/Photino.cs`

Web-based GUI using [Photino.NET](https://github.com/tryphotino/photino.NET). The frontend HTML/JS lives in `UX/wwwroot/index.html`.

### Threading

`RunAsync` creates a dedicated STA thread for the Photino window. The app logic runs on the original thread. Communication between the two is via a thread-safe outbox queue and `TaskCompletionSource` instances for blocking operations.

### Message Protocol

Messages are serialized to JSON and sent via `PhotinoWindow.SendWebMessage`. Inbound messages from the frontend are dispatched in `OnMessage`. Key message types:

| Direction | Type | Description |
|-----------|------|-------------|
| App → Frontend | `ChatMessage` | Append a chat message |
| App → Frontend | `StartRealtime` | Begin a streaming output block |
| App → Frontend | `RealtimeAppend` | Append text to a realtime block |
| App → Frontend | `RealtimeComplete` | Close a realtime block |
| App → Frontend | `ShowMenu` | Show a menu; blocks until `MenuResult` |
| App → Frontend | `ShowForm` | Show a form; blocks until `FormResult` |
| App → Frontend | `RenderTable` | Render a table |
| App → Frontend | `RenderReport` | Render a report |
| App → Frontend | `StartProgress` | Begin progress tracking |
| App → Frontend | `UpdateProgress` | Update progress snapshot |
| App → Frontend | `CompleteProgress` | Finalize progress |
| Frontend → App | `Input` | User typed a message |
| Frontend → App | `MenuResult` | User selected a menu item |
| Frontend → App | `FormResult` | User submitted a form |
| Frontend → App | `Ready` | Frontend initialized |

## Progress

**File:** `UX/Progress.cs`

`AsyncProgress.Builder` runs a collection of items concurrently with a live progress UI:

```csharp
var (results, failures, canceled) =
    await AsyncProgress.For("Adding content to RAG")
        .WithDescription(path)
        .WithCancellation(cts)
        .Run<(string Name, string Content), bool>(
            items: () => list,
            nameOf: x => x.Name,
            processAsync: async (item, progressItem, ct) => {
                progressItem.SetTotal(chunks);
                progressItem.Advance(1, "embedded");
                return true;
            });
```

A background task pumps `UpdateProgress` snapshots to the UI at 100 ms intervals. On completion, a Markdown summary is persisted to the chat context via `Context.AddToolMessage()` and emitted to the UI via `CompleteProgress`.

`ProgressSnapshot` captures counts (running/queued/completed/failed/canceled), per-item state and percentages, and an optional ETA hint.

## Table and Report

**File:** `UX/Table.cs`, `UX/Report.cs`

`Table` holds column headers and string rows. `Terminal.RenderTable` auto-sizes columns to fit the terminal width. `Report` is a higher-level document model; `Terminal.RenderReport` converts it to plain text at the current terminal width.

Both write their output as `Roles.Tool` chat messages so they appear in chat history.

## FilePicker

**File:** `UX/FilePicker.cs`

Platform-native file picker:
- **Windows** — uses `IFileOpenDialog` / `IFileSaveDialog` COM interfaces via P/Invoke
- **macOS** — uses `NSOpenPanel` via Objective-C runtime P/Invoke
- **Other** — falls back to terminal path-with-autocomplete

`FilePicker.Create()` returns the appropriate `IFilePicker` implementation. `Terminal.PickFilesAsync` wraps `ReadPathWithAutocompleteAsync` as a simple fallback.
