# UxRefactor — Feature Spec

**Branch:** `feature/UxRefactor`
**Status as of 2026-02-26:** Core layer complete; Terminal and Photino renderers functional; integration wired in `Program.cs`; remaining work is surface polish, missing tests, and legacy cleanup.

---

## Problem & Intent

The previous UI system had each interaction mode (chat, progress, menus, forms) hand-rolled in `Photino.cs` and `Terminal.cs` as imperative, non-composable code. Swapping surfaces, adding new full-window experiences (e.g., a Mail client), or iterating layout was fragile and required duplicating logic in both backends.

This refactor introduces a **declarative, retained-mode control layer** (UiNode/UiPatch) that:

- Mounts a single "surface" (e.g., Chat, Mail, modal Form) across both the Terminal and Photino UI backends.
- Communicates changes via a small diff/patch protocol — only what changed is transmitted or re-rendered.
- Provides predictable focus ownership and keyboard routing via an `IInputRouter` abstraction.
- Enables a proper overlay stack (modal menus, forms) layered on top of any content surface.

---

## Architecture Overview

### Layer Diagram

```
Program.cs
  └─ UiFrameController          ← main application loop + input routing
       ├─ UiFrameBuilder         ← constructs 3-layer frame tree (Header/Content/Overlays)
       ├─ ChatSurface            ← declares and patches the chat interface
       ├─ MenuOverlay            ← modal menu (filter, navigate, select)
       └─ FormOverlay            ← modal form (field editing, validation)

IUi (interface)
  ├─ CUiBase (abstract)         ← owns UiNodeTree, reconciler, progress, realtime, overlays
  │    ├─ Terminal               ← console backend with TermDom virtual-DOM renderer
  │    └─ Photino                ← Photino.NET backend; sends JSON messages to index.html
  └─ UiNodeTree                  ← retained-mode tree (atomic patch, focus, rollback)

UiNode / UiPatch / UiOp         ← core data model (immutable records)
UiReconciler                    ← tree diffing (prev → next → minimal patch)
```

### New Files in This Branch

| File | Purpose |
|------|---------|
| `UX/UiNode.cs` | Core data model: `UiKind`, `UiNode`, `UiStyles`, `UiProperty` (enum-keyed props), `UiOp` hierarchy, `UiPatch`, `UiPatchBuilder`, `UiNodeTree`, `UiReconciler` |
| `UX/UiNodeExtensions.cs` | Fluent DSL: `Ui.*` factory, `Style.*` helpers, `.WithProps()`, `.WithStyles()`, `.ForEach()`, `.If()`, `ToPatch()` |
| `UX/CUiBase.cs` | Abstract base shared by `Terminal` and `Photino`; owns tree, patches, realtime, progress, menu, form |
| `UX/UiFrame.cs` | `UiFrame` record, `UiFrameKeys` constants, `UiFrameBuilder` (create/push/pop overlays, replace header/content), `UiFrameController` (main loop) |
| `UX/ChatSurface.cs` | Chat surface builder: `Create()`, `AppendMessage()`, `UpdateMessageContent()`, `UpdateInput()`, `HandleKeyAsync()`, `ProcessChatInputAsync()`, realtime/ephemeral message helpers |
| `UX/MenuOverlay.cs` | Modal menu overlay: `Create()` builds UiNode; `ShowAsync()` drives keyboard input loop |
| `UX/FormOverlay.cs` | Modal form overlay: `Create()` builds UiNode; `ShowAsync()` drives field interaction |
| `UX/Progress.cs` | `ProgressUi.CreateNode()` — UiNode-based progress rendering (replaces old `Terminal.Progress` direct draw) |
| `unittests/UiNodeTests.cs` | Tree operations, atomicity/rollback, sequential inserts, sibling integrity |
| `unittests/ReconcilerTests.cs` | Reconciler diff scenarios: null prev, kind change, props diff, insert/remove, reorder, style change |
| `unittests/UiFrameTests.cs` | Frame builder, overlay push/pop, MenuOverlay structure, FormOverlay structure |
| `unittests/AsyncProgressTests.cs` | Progress tracking tests |

### Key Modified Files

| File | Changes |
|------|---------|
| `UX/IUI.cs` | Added `SetRootAsync`, `PatchAsync`, `ReconcileAsync`, `MakePatch`, `FocusAsync`; added `IInputRouter` with `TryReadKey()`; removed `ReadInputWithFeaturesAsync` / `ReadPathWithAutocompleteAsync` (now internal to Terminal) |
| `UX/Terminal.cs` | Now extends `CUiBase`; implements `PostSetRootAsync`/`PostPatchAsync`/`PostFocusAsync` via `TermDom` virtual-DOM; houses `TerminalInputRouter` |
| `UX/Photino.cs` | Now extends `CUiBase`; sends `MountControl`/`PatchControl`/`FocusControl` JSON messages; handles `ControlEvent`/`ControlHotkey` from frontend; houses `PhototinoInputRouter` |
| `UX/wwwroot/index.html` | Rewrote JavaScript: `mountControl()`, `renderNode()`, `patchControl()`, `focusControl()`; renders all `UiKind` types to DOM; dispatches `ControlEvent` messages back to .NET |
| `Program.cs` | Instantiates `UiFrameController`, calls `InitializeAsync()` to mount the frame, then `RunLoopAsync()` as the main input loop |
| `Errors.cs` | Added `PlatformNotReadyException` |

---

## Core Data Model (`UX/UiNode.cs`)

### UiNode

Immutable record representing a single UI element:

```csharp
public sealed record UiNode(
    string Key,
    UiKind Kind,
    IReadOnlyDictionary<UiProperty, object?> Props,
    IReadOnlyList<UiNode> Children
) {
    public UiStyles Styles { get; init; } = UiStyles.Empty;
}
```

Keys must be unique within any mounted tree. Violation throws `InvalidOperationException` at `SetRoot` / `ApplyInsertChild` time.

### UiKind

```
Column | Row | Accordion | Label | Button | CheckBox | Toggle
TextBox | TextArea | ListView | Html | Spacer
```

### UiProperty (enum-keyed props)

All node properties are stored under a strongly-typed `UiProperty` enum key, eliminating stringly-typed dictionary bugs. The enum includes layout (`Width`, `Height`, `Padding`, `Layout`, `Columns`), content (`Text`, `Value`, `Placeholder`, `Title`, `Items`, `SelectedIndex`), state (`Enabled`, `Scrollable`, `AutoScroll`, `Checked`), event handlers (`OnClick`, `OnChange`, `OnEnter`, `OnToggle`, `OnItemActivated`), and structural props (`ZIndex`, `Role`, `Modal`, `Focusable`).

### UiStyles

A separate, typed style bag for visual attributes: `ForegroundColor`, `BackgroundColor`, `Bold`, `Style` (string tag), `Wrap`, `Align`.

### Patch Operations

| Operation | Effect |
|-----------|--------|
| `ReplaceOp(key, node)` | Replace entire subtree at `key` |
| `UpdatePropsOp(key, props)` | Merge props into existing node (preserves un-mentioned keys) |
| `InsertChildOp(parentKey, index, node)` | Insert child; clamps out-of-range index to append |
| `RemoveOp(key)` | Remove node and its subtree |

`UiPatch` groups ops and is applied **atomically** — if any op fails, the tree rolls back to its pre-patch state.

### UiNodeTree

Manages the live retained tree. Features:

- O(1) node lookup via key→node map.
- Parent map for path reconstruction.
- Unique-key validation on `SetRoot` and insert.
- Atomic patch with rollback on any error.
- Single-focus tracking with focusability validation by `UiKind`.
- Handles "upsert" semantics on `ApplyInsertChild` when a node with the same key already exists.

### UiReconciler

Computes a minimal `UiPatch` from two UiNode trees:

- `null` previous → `ReplaceOp` for the next node (mount semantics).
- Same key but `Kind` or `Styles` changed → `ReplaceOp`.
- Props differ → `UpdatePropsOp` for only the changed/added keys.
- Children: removes keys absent in next, inserts keys absent in prev, reorders via remove+insert, recurses into matching keys.

---

## DSL and Fluent Builder (`UX/UiNodeExtensions.cs`)

### `Ui.*` Factory

```csharp
Ui.Column("key", child1, child2)
Ui.Row("key", child1, child2)
Ui.Text("key", "Hello")
Ui.Button("key", "Send", onClick)
Ui.TextBox("key", value: "", placeholder: "Type...")
Ui.CheckBox("key", label: "Enable", isChecked: false)
Ui.ListView("key", items, selectedIndex, onItemActivated)
Ui.Spacer("key")
Ui.Node("key", UiKind.Html, new { Content = "<b>hello</b>" })
```

### `Style.*` Helpers

```csharp
Style.Bold
Style.AlignLeft / AlignCenter / AlignRight
Style.Wrap
Style.Color(fg: ConsoleColor.Cyan, bg: ConsoleColor.DarkGray)
Style.Tag("dim")
Style.Combine(Style.AlignCenter, Style.Bold)
```

### Extension Methods

| Method | Description |
|--------|-------------|
| `.WithProps(object)` | Merge anonymous object into props (name→UiProperty lookup) |
| `.WithProps(dict)` | Merge typed dictionary |
| `.WithStyles(UiStyles)` | Replace style bag |
| `.WithChildren(params UiNode[])` | Append children |
| `.ForEach<T>(source, map)` | Map a sequence to children |
| `.If(condition, factory)` | Conditionally add child |
| `.WithSize(w, h)` | Set Width/Height via props |
| `.ToPatch(targetKey)` | Produce a `ReplaceOp` patch targeting `targetKey` |

### `UiPatchBuilder`

```csharp
// Bound builder (applies when PatchAsync() is called):
await ui.MakePatch()
    .Update("input", new Dictionary<UiProperty, object?> { [UiProperty.Text] = text })
    .Insert("messages", int.MaxValue, messageNode)
    .PatchAsync();

// Unbound builder (just builds a UiPatch):
var patch = new UiPatchBuilder()
    .Replace("header", newHeader)
    .Remove("overlay-menu")
    .Build();
```

---

## Frame Model (`UX/UiFrame.cs`)

The application window is structured as a 3-layer frame:

```
frame.root (Column, role=frame)
├── <header node>     (role=header) — toolbar, thread title, buttons
├── <content node>    (role=content) — current surface (ChatSurface, MailSurface, etc.)
└── frame.overlays    (Column, role=overlay)
     ├── overlay-menu  (when menu open)
     └── overlay-form  (when form open)
```

**`UiFrameBuilder`** creates and manipulates this tree:

| Method | Returns |
|--------|---------|
| `Create(frame)` | Full root `UiNode` |
| `ReplaceContent(node)` | Patch targeting `frame.content` |
| `ReplaceHeader(node)` | Patch targeting `frame.header` |
| `PushOverlay(node)` | `InsertChildOp` into `frame.overlays` |
| `PopOverlay(key)` | `RemoveOp` for named overlay |

**`UiFrameController`** drives the main loop:

1. `InitializeAsync()` — builds header + chat surface, calls `SetRootAsync`.
2. `RunLoopAsync()` — polls `IInputRouter.TryReadKey()`:
   - **ESC** → open `MenuOverlay.ShowAsync()`, dispatch selected command, restore focus.
   - **Other keys** → delegate to `ChatSurface.HandleKeyAsync()`.
   - **Submit action** → `ChatSurface.ProcessChatInputAsync()` which calls `Engine.PostChatAsync`.
3. `RefreshMessagesAsync()` — appends loaded thread messages one by one after initialization.
4. `UpdateHeaderAsync/UpdateContentAsync` — reconcile-based updates for those frame regions.

---

## ChatSurface (`UX/ChatSurface.cs`)

Declares the chat interface as a UiNode tree and provides patch factories:

```
chat-root (Column, layout=dock-bottom)
├── messages (Column, scrollable, autoScroll)
│   ├── msg-{id} (Column)
│   │   ├── msg-{id}-header (Label)
│   │   └── msg-{id}-content (Label, wrap)
│   └── ...
├── spacer
└── composer (Row)
    ├── input (TextBox)
    └── send-btn (Button)
```

Key patch factories:

| Method | Description |
|--------|-------------|
| `Create(messages)` | Build full chat content node |
| `CreateHeader(threadName)` | Build toolbar node |
| `AppendMessage(msg, index)` | `InsertChildOp` appending a message |
| `UpdateMessageContent(id, text)` | `UpdatePropsOp` on content label (streaming) |
| `UpdateInput(text)` | Update input TextBox and send-btn enabled state |
| `ClearMessages()` | `ReplaceOp` on messages panel with empty node |
| `InsertRealtimeMessage(key, content)` | Insert ephemeral message for realtime output |
| `UpsertRealtimeMessage(key, content)` | Update existing ephemeral message content |
| `UpdateMessageState(id, state)` | Update message State prop (Finalized, etc.) |

**`HandleKeyAsync`** processes: Enter (submit), Shift+Enter (soft newline), Backspace/Delete, Home/End, Ctrl+Left/Right (word nav), Tab (cycle focus input↔send), Up/Down/PgUp/PgDown (scroll messages panel), printable chars (insert at caret).

**`ProcessChatInputAsync`** handles the full chat turn: add user message → `UpdateAsync` (reconcile) → call AI provider → append response → `UpdateAsync` again.

---

## Overlay System

### MenuOverlay (`UX/MenuOverlay.cs`)

```
overlay-menu (Column, Modal=true)
├── overlay-menu-title (Label)
├── overlay-menu-filter (TextBox)
└── overlay-menu-list (ListView)
```

`ShowAsync()` drives the interaction loop: type to filter, Up/Down/PgUp/PgDown to navigate, Enter to select, ESC to cancel. Uses `UiReconciler` to apply minimal patches as filter text changes.

### FormOverlay (`UX/FormOverlay.cs`)

Renders `UiForm` as a `UiNode` tree with per-field label, input, help text, and error label groups. `ShowAsync()` processes field edits, validates on submit, and returns confirmed model or null on cancel.

---

## IUi Extensions

New methods added to `IUi`:

```csharp
Task SetRootAsync(UiNode root, UiControlOptions? options = null);
Task PatchAsync(UiPatch patch);
Task ReconcileAsync(UiNode? previous, UiNode next);
UiPatchBuilder MakePatch();
Task FocusAsync(string key);
IInputRouter GetInputRouter();
```

`IInputRouter` provides non-blocking key polling:

```csharp
public interface IInputRouter {
    ConsoleKeyInfo? TryReadKey(); // returns null if no key available
}
```

This replaces the blocking `ReadInputWithFeaturesAsync` call in the previous chat loop, enabling a proper event-driven model where overlays and chat input share the same thread without nesting blocking calls.

---

## Terminal Backend

`Terminal` extends `CUiBase`. It implements the three template methods via an internal `TermDom` virtual-DOM:

**`TermDom`** is a two-pass renderer:
1. **Layout** — traverses the UiNode tree, assigns each node to one or more `TermLine` records (text + color + alignment), and records key→region maps. Handles: Label (with word-wrap), Button (bracketed, focused highlight), TextBox/TextArea (underline, cursor), CheckBox/Toggle, ListView (viewport-clamped scrollable list), Row/Column (recursive, respects `GridColumns`), Accordion (expand/collapse), Spacer.
2. **Diff** — compares old and new `TermSnapshot` line by line; emits a minimal list of `TermEdit` operations (write line at position).
3. **Apply** — executes `TermEdit` list using `Console.SetCursorPosition` + color writes.

`TerminalInputRouter` implements `IInputRouter` using a background thread that calls `Console.ReadKey(intercept: true)` and enqueues results to a `ConcurrentQueue<ConsoleKeyInfo>`. `TryReadKey()` dequeues without blocking.

---

## Photino Backend

`Photino` extends `CUiBase`. It implements the template methods by posting JSON messages to the web view:

| .NET → Web | Description |
|-----------|-------------|
| `MountControl { tree, options }` | Mount a new surface; web view clears and renders from root |
| `PatchControl { patch }` | Apply patch ops to live DOM |
| `FocusControl { key }` | Focus DOM element by key |

| Web → .NET | Description |
|-----------|-------------|
| `ControlEvent { key, name, value }` | User interaction (click, change, enter, itemActivated) |
| `ControlHotkey { key, hotkey }` | Global keyboard events |

`PhototinoInputRouter` implements `IInputRouter` by buffering `ControlEvent`/`ControlHotkey` messages as synthesized `ConsoleKeyInfo` values, enabling the same `HandleKeyAsync` path as Terminal.

### JavaScript Renderer (`index.html`)

Renders all `UiKind` types to DOM using CSS Flexbox. Supports:

- `Column` / `Row` — `flex-direction: column / row`
- `Label` — `<span>` with color, bold, wrap styles
- `Button` — `<button>` with enabled/disabled, click dispatch
- `TextBox` / `TextArea` — `<input>` / `<textarea>` with change/enter dispatch
- `CheckBox` / `Toggle` — `<input type=checkbox>` with change dispatch
- `ListView` — `<ul>` with item selection, keyboard navigation
- `Accordion` — collapsible `<details>`
- `Html` — raw innerHTML insertion
- `Spacer` — flex spacer

Patch operations `replace`, `updateProps`, `insertChild`, `remove` apply incrementally to the live DOM element map (keyed by `data-key` attribute).

---

## Progress System (`UX/Progress.cs` + `CUiBase`)

Progress is now rendered as UiNode subtrees inserted into the messages panel:

- `StartProgress` — inserts an ephemeral `progress-{id}` node into `messages`.
- `UpdateProgress` — calls `ReconcileAsync(prevNode, newNode)` to apply minimal patches.
- `CompleteProgress` — removes the progress node, renders the artifact as a Tool message.

`ProgressUi.CreateNode(id, snapshot)` builds the progress UiNode tree: title label, per-item rows (glyph, name, percentage, step count), footer stats. The old `Terminal.Progress` drawing methods are retained as a fallback for pre-frame terminal draws during startup.

---

## Realtime Output (`CUiBase.RealtimeWriterImpl`)

`BeginRealtime(title)` inserts an ephemeral `realtime_{guid}` node into the messages panel. Subsequent `Write`/`WriteLine` calls patch the node's text in-place. On `Dispose`, the node is marked `Finalized` (stays visible but won't be persisted to chat history).

---

## Tests

Tests live in `unittests/` and pass cleanly:

| Test Class | Coverage |
|-----------|---------|
| `UiNodeTests` | Node creation, `UpdateProps`, `InsertChild` (sequential, deep, sibling isolation), `Remove`, `SetFocus`, atomic patch rollback, duplicate key detection, helpful error messages |
| `ReconcilerTests` | Null prev → Replace, kind change → Replace, props diff → UpdateProps, insert/remove children, reorder via remove+insert, styles change → Replace |
| `UiFrameTests` | Frame structure validation, overlay zIndex ordering, `ReplaceContent`/`ReplaceHeader` patches, `PushOverlay`/`PopOverlay` patches, MenuOverlay structure, FormOverlay structure |
| `AsyncProgressTests` | Async progress tracking |

Run with:
```bash
dotnet test
dotnet test --filter "FullyQualifiedName~UiNodeTests"
dotnet test --filter "FullyQualifiedName~ReconcilerTests"
dotnet test --filter "FullyQualifiedName~UiFrameTests"
```

---

## What Is Working

| Area | Status |
|------|--------|
| `UiNode` / `UiPatch` / `UiNodeTree` data model | ✅ Complete, all invariants tested |
| `UiReconciler` diffing | ✅ All diff scenarios covered by tests |
| `Ui.*` + `Style.*` DSL | ✅ Complete, used throughout |
| `UiPatchBuilder` fluent API | ✅ Complete |
| `CUiBase` shared control layer | ✅ Complete |
| `UiFrame` / `UiFrameBuilder` | ✅ Complete, tested |
| `UiFrameController` (main loop) | ✅ Wired in `Program.cs` |
| `ChatSurface` patch factories + input handling | ✅ Functional |
| `MenuOverlay` interactive modal | ✅ Functional (filter, navigate, select) |
| `FormOverlay` structure | ✅ Built; interaction driven by `CUiBase.ShowFormAsync` |
| Progress (UiNode-based) | ✅ Ephemeral nodes in messages panel |
| Realtime output (UiNode-based) | ✅ Ephemeral nodes with finalize on close |
| Terminal `TermDom` virtual-DOM renderer | ✅ Functional (layout, diff, incremental apply) |
| Photino JSON message protocol | ✅ MountControl / PatchControl / FocusControl |
| JavaScript renderer (`index.html`) | ✅ All UiKind types, incremental DOM patches |
| `IInputRouter` non-blocking input | ✅ Terminal and Photino implementations |
| `Program.cs` integration | ✅ Frame mounted, commands wired, loop running |
| Build (no errors) | ✅ Compiles cleanly |

---

## What Is Not Yet Working / Partial

| Area | Issue |
|------|-------|
| Terminal overlay visual stacking | Overlays are inserted correctly into the tree but the TermDom renders them inline in the layout flow. There is no terminal-side "modal on top of content" visual isolation. The overlay appears appended below content rather than visually floating over it. |
| Photino overlay CSS stacking | The `z-index` and `position: absolute/fixed` CSS for overlay nodes in `index.html` may not produce correct modal behavior across all browser compositing scenarios. Needs visual verification. |
| Terminal `dock-bottom` layout | The `Layout = "dock-bottom"` prop on `chat-root` is not yet implemented in `TermDom`. The composer row is rendered inline after messages rather than pinned to the bottom of the terminal viewport. |
| Scroll offset in Terminal | The `Min` prop is used as a scroll-offset proxy for the messages panel (set by `HandleKeyAsync`). The TermDom does limit ListView viewport height, but the `ScrollOffset` for a generic `Column` is not fully implemented. Messages may not scroll correctly in Terminal mode. |
| `MailCommands.cs` legacy output | ~22 `[Obsolete]` warnings from `Write`/`WriteLine` calls. These should be migrated to `IRealtimeWriter` to properly insert output into the UiNode messages panel. |
| ControlEvent → `IInputRouter` routing in Photino | `PhototinoInputRouter` translates `ControlEvent` messages into synthesized `ConsoleKeyInfo` values. The mapping covers common cases but is a lossy translation — rich events (e.g., `onChange` text value, `onItemActivated` index) cannot be accurately represented as a single key press. Proper event dispatch requires a separate event handler registration mechanism. |
| Unit handler invocation via `Props` | Event handlers stored in `Props` (`OnClick`, `OnChange`, etc.) are set on nodes in `ChatSurface` and `FormOverlay` but are not invoked anywhere in `CUiBase` or the backend implementations. The `UiEvent` / `UiHandler` delegate machinery is defined but not connected to actual keyboard or click events. |

---

## Work Remaining

| Item | Priority | Notes |
|------|----------|-------|
| Terminal `dock-bottom` layout | High | Composer should be pinned to screen bottom; messages panel should fill remaining height. Requires TermDom layout to honor `Layout = "dock-bottom"` by reserving fixed rows at the bottom. |
| Terminal modal overlay rendering | High | When an overlay is pushed, TermDom should render a dimmed background + centered overlay box rather than inline below content. |
| `MailCommands.cs` migration | Medium | Replace `[Obsolete]` `Write`/`WriteLine` calls with `IRealtimeWriter` blocks so MAPI output appears in the messages panel. |
| Terminal snapshot / integration tests | Medium | Spec requires ASCII layout snapshot tests and focus + key routing tests. None exist yet. |
| Photino integration tests | Medium | Spec requires DOM creation and event round-trip tests. None exist yet. |
| `UiHandler` event dispatch | Medium | Wire stored `OnClick`/`OnChange`/`OnEnter`/`OnItemActivated` handlers to actual event dispatch in both Terminal (keyboard events) and Photino (`ControlEvent` messages). |
| Photino `ControlEvent` routing | Medium | Demote `PhototinoInputRouter` synthetic key mapping; instead route `ControlEvent` directly to the matching UiNode's handler via the `UiHandler` delegate. |
| MailSurface | Low | Spec example #6: swap entire content area to a Mail surface (folders, thread list, read pane). The `UiFrameBuilder.ReplaceContent` mechanism is ready; only the MailSurface builder itself is missing. |
| `docs/ux.md` update | Low | The existing UX doc still describes the old `IUi` API (`ReadInputWithFeaturesAsync`, old progress signatures, etc.). Should be updated to reflect the new `SetRootAsync`/`PatchAsync`/`ReconcileAsync` API and the builder DSL. |
| `[Obsolete]` method removal | Low | `IUi.Write`/`WriteLine` are marked obsolete and should be removed once `MailCommands.cs` (and any other remaining callers) are migrated. |
| Performance: batched patch cycles | Low | Currently every `PatchAsync` triggers an immediate re-layout + re-render. A micro-batch (coalesce patches within one event loop tick) would reduce redundant terminal redraws during streaming. |

---

## Migration Notes

### From old `ReadInputWithFeaturesAsync` loop

Old pattern (removed from `IUi`):
```csharp
var input = await ui.ReadInputWithFeaturesAsync(commandManager);
```

New pattern (driven by `UiFrameController`):
```csharp
var controller = new UiFrameController(ui, ui.GetInputRouter(), context, config);
await controller.InitializeAsync();
controller.SetCommandManager(commandManager);
await controller.RunLoopAsync();
```

### Building a new surface

Implement a static surface builder class (similar to `ChatSurface`) that:
1. Returns a root `UiNode` from `Create(viewModel)`.
2. Provides patch factory methods for incremental updates.
3. Optionally implements a key handler method for keyboard interaction.

Mount via:
```csharp
await ui.PatchAsync(UiFrameBuilder.ReplaceContent(MySurface.Create(vm)));
```

### From old `IUi.RenderMenu`

Old:
```csharp
var choice = ui.RenderMenu("Select", choices, 0);
```

New (already handled by `CUiBase.RenderMenu` wrapper):
```csharp
var choice = await MenuOverlay.ShowAsync(ui, "Select", choices, 0);
```

`CUiBase.RenderMenu` calls `MenuOverlay.ShowAsync` synchronously for callers that need the non-async overload.

---

## File References

- Core spec: `specs/UxRefactor/UxRefactor.md`
- Implementation notes: `specs/UxRefactor/IMPLEMENTATION.md`, `IMPLEMENTATION_SUMMARY.md`, `CHATSURFACE_INTEGRATION.md`
- Progress unification notes: `specs/UxRefactor/PROGRESS_UNIFICATION.md`, `PROGRESS_UNIFICATION_COMPLETE.md`
- Photino test plan: `specs/UxRefactor/PHOTINO_TEST_PLAN.md`, `PHOTINO_UINODE_INTEGRATION.md`
