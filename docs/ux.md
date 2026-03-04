# UX

Located in `/UX/`. Provides the `IUi` abstraction over two concrete implementations: a classic console (`Terminal`) and a web-based GUI (`PhotinoUi`).

---

## Architecture Overview

CSChat's UX is a two-tier declarative system:

```
Commands / Engine / Subsystems
          │
          │  build immutable UiNode trees
          │  call ui.PatchAsync / ReconcileAsync
          ▼
   ┌──────────────────────────────────────┐
   │   IUi (contract boundary)            │
   │   CUiBase (shared retained-mode layer│
   │     UiNodeTree   key→node map        │
   │     UiReconciler tree diff           │
   │     UiPatch      atomic edits        │
   └──────────────────────────────────────┘
          │  PostPatchAsync (template method)
          ▼
   Terminal : CUiBase
     └── TermDom (virtual DOM)
           Layout() → TermSnapshot (lines + keyMap)
           Diff()   → TermEdit[]   (minimal changes)
           Apply()  → ANSI codes to console
```

**Upper tier** — commands build immutable `UiNode` trees without knowing anything about rendering. `CUiBase` reconciles new trees against previous ones and applies minimal patches.

**Lower tier** — the platform renderer (`TermDom` in Terminal, browser DOM in Photino) receives those patches and applies them efficiently. Terminal debounces patches at ~60 fps.

---

## File Map

| File | Role |
|------|------|
| `IUI.cs` | Interface contracts: `IUi`, `IInputRouter`, `IRealtimeWriter` |
| `UiNode.cs` | Core declarative model: `UiNode`, `UiKind`, `UiProperty`, `UiPatch`, `UiNodeTree`, `UiReconciler` |
| `UiNodeExtensions.cs` | Fluent builder DSL (`Ui.*`, `Style.*` static helpers) |
| `CUiBase.cs` | Abstract base: reconciliation, focus, progress, overlays — platform-agnostic |
| `Terminal.cs` | Concrete terminal implementation; owns `TermDom` virtual DOM and `TerminalInputRouter` |
| `Photino.cs` | Web-based GUI implementation via Photino.NET |
| `UiFrame.cs` | Top-level orchestration: `UiFrameController` (input loop), `UiFrameBuilder` (frame tree construction) |
| `ChatSurface.cs` | Chat display + composer; input state machine, message rendering, submit flow |
| `MenuOverlay.cs` | Filterable selection list modal |
| `InputOverlay.cs` | Single-question text input modal |
| `ConfirmOverlay.cs` | Yes/No confirmation modal |
| `FormOverlay.cs` | Structured form with field types, validation, submit/cancel |
| `FilePicker.cs` | Platform-native file dialogs (`IFilePicker`, Windows/macOS implementations) |
| `Progress.cs` | Progress tracking: `ProgressItem` state machine, `ProgressSnapshot`, `ProgressUi` node builder |
| `Table.cs` | `Table` model (headers + string rows) |
| `Report.cs` | Document tree: sections, paragraphs, lists, tables; renders to Markdown or PlainText |
| `Utilities.cs` | `TruncatePlain`, `TruncatePlainHard`, HTML stripping |

---

## Core Types

### UiNode (immutable declarative tree node)

```csharp
public sealed record UiNode(
    string Key,
    UiKind Kind,
    IReadOnlyDictionary<UiProperty, object?> Props,
    UiStyles Styles,
    IReadOnlyList<UiNode> Children
);
```

- **Key** — stable string identity across patches; must be unique in the tree.
- **Kind** — `Column | Row | Accordion | Label | Button | CheckBox | Toggle | TextBox | TextArea | ListView | Html | Spacer`
- **Props** — extensible property bag (Text, Value, Items, OnClick, OnChange, OnEnter, …). Event handlers are `UiHandler` delegates: `async Task (UiEvent e)`.
- **Styles** — separate visual layer: `ForegroundColor`, `BackgroundColor`, `Bold`, `Align`, `Wrap`.
- **Children** — immutable list. Container nodes (`Column`, `Row`, `Accordion`) render children in order.

Fluent construction via `Ui.*` / `Style.*` helpers:
```csharp
var node = Ui.Column("root",
    Ui.Text("title", "Hello").WithStyles(Style.Bold),
    Ui.Button("btn", "OK").WithProps(new { OnClick = handler })
);
```

### UiPatch (atomic edit batch)

Operations: `ReplaceOp` (full subtree swap), `UpdatePropsOp` (merge props), `InsertChildOp` (add child at index), `RemoveOp` (delete node). Applied all-or-nothing via `UiNodeTree.ApplyPatch`.

Build fluently:
```csharp
var patch = ui.MakePatch()
    .Update("title", new { Text = "Updated" })
    .Insert("messages", int.MaxValue, newMessageNode)
    .Build();
await ui.PatchAsync(patch);
```

### UiNodeTree (retained state)

Owns the mutable root + `key→node` map. Validates unique keys, supports `SetFocus`, `FindNode`, and atomic `ApplyPatch`. Only focusable kinds (`Button`, `CheckBox`, `Toggle`, `TextBox`, `TextArea`, `ListView`) can receive focus.

### UiReconciler (tree diff)

Compares two `UiNode` trees by stable key and emits a minimal `UiPatch`:
- Same key+kind+styles → merge prop changes, recurse children
- Different key/kind/styles → emit `ReplaceOp`
- Children: removes stale, inserts new, reorders moved, reconciles existing (by key identity)

```csharp
await ui.ReconcileAsync(previousNode, nextNode); // diff + apply
```

### IUi (contract boundary)

```csharp
public interface IUi
{
    // High-level I/O
    Task<bool> ShowFormAsync(UiForm form);
    Task<bool> ConfirmAsync(string question, bool defaultAnswer = false);
    Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt);

    Task RenderTableAsync(Table table, string? title = null);
    Task RenderReportAsync(Report report);
    IRealtimeWriter BeginRealtime(string title);

    // Progress
    Task<string> StartProgressAsync(string title, CancellationTokenSource cts);
    Task UpdateProgressAsync(string id, ProgressSnapshot snapshot);
    Task CompleteProgressAsync(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown);

    // Input
    IInputRouter GetInputRouter();
    bool TryCancelActiveProgress();
    Task<string?> RenderMenuAsync(string header, List<string> choices, int selected = 0);
    Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept);

    // Chat output
    Task RenderChatMessageAsync(ChatMessage message);
    Task RenderChatHistoryAsync(IEnumerable<ChatMessage> messages);

    // Declarative control layer
    Task SetRootAsync(UiNode root, UiControlOptions? options = null);
    Task PatchAsync(UiPatch patch);
    Task ReconcileAsync(UiNode? previous, UiNode next);
    UiPatchBuilder MakePatch();
    Task FocusAsync(string key);
    Task<bool> DispatchEventAsync(string nodeKey, string eventName, string? value = null);

    Task RunAsync(Func<Task> appMain);
}
```

### IInputRouter

```csharp
public interface IInputRouter
{
    ConsoleKeyInfo? TryReadKey(); // non-blocking poll
}
```

`TerminalInputRouter` wraps `Console.KeyAvailable` + `Console.ReadKey(intercept: true)`. The main loop in `UiFrameController.RunLoopAsync` polls this at ≤10 ms intervals.

### IRealtimeWriter

```csharp
public interface IRealtimeWriter : IDisposable
{
    void Write(string text);
    void WriteLine(string? text = null);
}
```

Returned by `BeginRealtime(title)`. Each call to `Write`/`WriteLine` inserts or updates an ephemeral node in the messages panel via patch. `Dispose` marks the node as `Finalized` (stays visible, won't be saved to chat history).

---

## CUiBase (shared platform-agnostic layer)

`CUiBase : IUi` provides the shared retained-mode implementation used by both Terminal and Photino:

- **Declarative control** — `SetRootAsync`, `PatchAsync`, `ReconcileAsync`, `FocusAsync`, `DispatchEventAsync` all operate on `_uiTree` then delegate to platform template methods.
- **Overlay helpers** — `ShowFormAsync` → `FormOverlay.ShowAsync`, `ConfirmAsync` → `ConfirmOverlay.ShowAsync`.
- **Progress management** — `StartProgressAsync`/`UpdateProgressAsync`/`CompleteProgressAsync` insert, reconcile, and remove ephemeral progress nodes in the messages panel.
- **Realtime output** — `BeginRealtime` returns a `RealtimeWriterImpl` that patches ephemeral message nodes on each write.

**Template methods** platforms must override:

| Method | Purpose |
|--------|---------|
| `PostSetRootAsync(root, options)` | Mount the root tree (clear console + full render, or send to browser) |
| `PostPatchAsync(patch)` | Apply a patch delta (terminal diff, or browser message) |
| `PostFocusAsync(key)` | Highlight newly focused element |

---

## Data Flows

### Input → Engine

```
User keystroke
  → Console.ReadKey (non-blocking via KeyAvailable)
  → TerminalInputRouter.TryReadKey()
  → UiFrameController.RunLoopAsync polls at ≤10 ms
  → [ESC] MenuOverlay.ShowAsync — runs its own poll loop
           → ExecuteCommand → Engine / Context
  → [other] ChatSurface.HandleKeyAsync → ChatInputState update
  → [Enter] ProcessChatInputAsync → Engine.PostChatAsync → LLM stream
  → Response tokens → Context → RenderChatMessageAsync
  → PatchAsync → TermDom.Layout → Diff → Apply (ANSI)
```

### Output → Display

```
Engine.PostChatAsync yields ChatMessage
  → CUiBase.RenderChatMessageAsync
  → ChatSurface.AppendMessage → UiPatch
  → CUiBase.PatchAsync (atomically updates UiNodeTree)
  → Terminal.PostPatchAsync (debounces ≤16 ms)
  → TermDom.Layout → TermSnapshot
  → [first render] GetFullRender → Apply all lines
  → [subsequent]   Diff → Apply only changed lines (ANSI)
```

### Streaming Output

```
ui.BeginRealtime(title) → RealtimeWriterImpl created
  → InsertRealtimeMessage patch (first Write call)
  → each Write/WriteLine → UpsertRealtimeMessage patch → Apply diff
  → Dispose → UpdateMessageState(Finalized) patch
```

### Forms / Modal Overlays

```
ui.ShowFormAsync(form) → FormOverlay.ShowAsync
  → UiFrameBuilder.PushOverlay → PatchAsync (insert into overlays container)
  → FormOverlay drives its own poll loop (GetInputRouter)
  → user submits / cancels
  → UiFrameBuilder.PopOverlay(key) → PatchAsync (remove node)
  → returns field values (or false if cancelled)
```

---

## UiFrameController

`UiFrameController` owns the top-level layout and the main input loop:

```
frame.root (Column)
  ├── frame.header (Row)    — thread name, buttons
  ├── frame.content (Column) — ChatSurface (messages + composer)
  └── frame.overlays (Column) — 0..N modal overlays (topmost = last child)
```

- `InitializeAsync` — mounts the initial frame tree; seeds `_prevHeaderNode`/`_prevContentNode` for future reconciler diffs.
- `RunLoopAsync` — polls `IInputRouter`; ESC triggers `MenuOverlay.ShowAsync`; all other keys go to `ChatSurface.HandleKeyAsync`.
- `UpdateHeaderAsync` / `UpdateContentAsync` — reconcile subtrees against cached previous versions, emitting minimal patches.

---

## Overlay Management

Overlays are children of the `frame.overlays` container. The last child is visually topmost.

- `UiFrameBuilder.PushOverlay(node)` — inserts at `int.MaxValue` (end of overlays children).
- `UiFrameBuilder.PopOverlay(key)` — emits a `RemoveOp` for the given key.

Each overlay component (e.g., `MenuOverlay`, `FormOverlay`) runs its own polling loop via `IInputRouter` while active, and removes itself on dismiss. Overlay keys are hard-coded per component (`"overlay-menu"`, `"overlay-form"`, etc.).

> **Note:** Visual stacking is determined solely by child order in the overlays container (last child = topmost). ZIndex has been removed from `UiProperty` and is no longer assigned.

---

## ChatSurface

`ChatSurface` builds and manages the chat panel:
- `Create(messages)` — builds the initial Column (messages panel + spacer + composer).
- `HandleKeyAsync(ui, key, state)` — pure function: given current state and a key, returns new state + action (None | Submit).
- `AppendMessage / UpsertRealtimeMessage / UpdateMessages` — static helpers returning `UiPatch` for the messages panel.
- `ProcessChatInputAsync` — submits user text to Engine, appends user and assistant messages to the chat surface.

`ChatInputState` is an immutable record: `Text`, `CaretPos`, `History`, `HistoryIndex`, `FocusKey`.

---

## Terminal

**File:** `UX/Terminal.cs`

Concrete `IUi` backed by the console. Owns:

- **`TermDom`** — private virtual DOM: walks `UiNode` trees → `TermSnapshot` (list of `TermLine`s + `key→TermRegion` map) → diffs against previous snapshot → applies minimal ANSI edits.
  - `_render` registry: unified `Dictionary<UiKind, Action<TermCtx, UiNode>>` for both leaf and container kinds.
  - Leaf renderers: `DrawTextInput`, `DrawCheckLike`, and lambdas for Label/Button/ListView/Html/Spacer.
  - Container renderers: `RenderColumn` (→ `RenderDockBottom` / `RenderGrid` / default), `RenderRow` (→ `RenderRowJustify` / `RenderGrid` / default), `RenderAccordion`.
  - Scroll primitives: `ComputeScrollMetrics`, `ScrollbarGlyph`, `ResolveScrollOffset` — shared across ListView, dock-bottom layout, and `CompositeOverlayBox`.
  - `BuildBoxRunLines` — static helper assembling bordered overlay run-lines (header/body/footer + scrollbar).
- **`TerminalInputRouter`** — wraps `Console.KeyAvailable` + `Console.ReadKey(intercept: true)`.
- **`WatchResizeAsync`** — background task monitoring terminal width/height; forces full repaint on change.
- **Debounced rendering** — `PostPatchAsync` coalesces rapid patches (~60 fps cap via 16 ms delay + interlocked counter). Prevents console flicker during fast LLM token streams.

### RenderTable

Renders a `Table` as ASCII, auto-sizing columns to fit terminal width. Output is appended as a `Roles.Tool` chat message.

### RenderReport

Converts `Report` to plain text at current terminal width. Output is appended as a `Roles.Tool` chat message.

---

## PhotinoUi

**File:** `UX/Photino.cs`

Web-based GUI using [Photino.NET](https://github.com/tryphotino/photino.NET). The frontend lives in `UX/wwwroot/index.html`.

**Threading:** `RunAsync` creates a dedicated STA thread for the Photino window. App logic runs on the original thread. Communication via a thread-safe outbox queue and `TaskCompletionSource` for blocking operations.

**Message protocol** (JSON over `PhotinoWindow.SendWebMessage`):

| Direction | Type | Description |
|-----------|------|-------------|
| App → Frontend | `ChatMessage` | Append a chat message |
| App → Frontend | `StartRealtime` | Begin a streaming output block |
| App → Frontend | `RealtimeAppend` | Append text to a realtime block |
| App → Frontend | `RealtimeComplete` | Close a realtime block |
| App → Frontend | `ShowMenu` | Show a menu; blocks until `MenuResult` |
| App → Frontend | `ShowForm` | Show a form; blocks until `FormResult` |
| App → Frontend | `RenderTable` / `RenderReport` | Structured output |
| App → Frontend | `StartProgress` / `UpdateProgress` / `CompleteProgress` | Progress tracking |
| Frontend → App | `Input` | User submitted a message |
| Frontend → App | `MenuResult` / `FormResult` | User interacted with a modal |
| Frontend → App | `Ready` | Frontend initialized |

---

## UiForm

**File:** `UX/IUI.cs` — `UiForm` and `UiField<TModel, TValue>`

Strongly-typed form builder:

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

`UiForm.Create(title, original)` clones the model via JSON round-trip so mutations don't affect the original until confirmed.

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

All fields support: `.WithHelp()`, `.MakeOptional()`, `.WithPlaceholder()`, `.WithRegex(pattern, msg)`, `.IntBounds(min, max)`, `.WithPathMode(mode)`.

---

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
            processAsync: async (item, progressItem, ct) =>
            {
                progressItem.SetTotal(chunks);
                progressItem.Advance(1, "embedded");
                return true;
            });
```

A background task pumps `UpdateProgressAsync` snapshots to the UI at ~100 ms intervals. On completion, a Markdown summary is persisted to the chat context via `Context.AddToolMessage()` and emitted via `CompleteProgressAsync`.

`ProgressSnapshot` captures: running/queued/completed/failed/canceled counts, per-item state and percentages, optional ETA hint.

---

## Table and Report

**File:** `UX/Table.cs`, `UX/Report.cs`

`Table` holds column headers and string rows. `Terminal.RenderTable` auto-sizes columns to fit terminal width. `Report` is a higher-level document model; `Terminal.RenderReport` converts it to plain text.

Both append output as `Roles.Tool` chat messages so they appear in chat history.

---

## FilePicker

**File:** `UX/FilePicker.cs`

Platform-native file picker:
- **Windows** — `IFileOpenDialog` / `IFileSaveDialog` COM interfaces via P/Invoke
- **macOS** — `NSOpenPanel` via Objective-C runtime P/Invoke
- **Other** — falls back to terminal path-with-autocomplete

`FilePicker.Create()` returns the appropriate `IFilePicker`.

---

## Known Issues

### Overlay stack fragility *(accepted risk)*
`UiFrameBuilder.PopOverlay(key)` requires callers to know the overlay's string key. Each overlay component hard-codes its key (e.g., `"overlay-menu"`), creating implicit coupling. In theory, two concurrent overlays of the same kind would collide on key — but this scenario is not currently possible: all overlay entry points are sequential command actions, so at most one overlay of each kind can be open at a time.
