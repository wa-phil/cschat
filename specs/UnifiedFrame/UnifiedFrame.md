# Problem & Intent #

Today, Program.cs still has to know about backends for interactive input and ad-hoc overlays (menus, forms), and Photino vs Terminal diverge because Photino has a DOM and Terminal doesn’t. We want:

1. Unified I/O: ReadInputWithFeaturesAsync(...) works the same in both backends via UiNode events (enter/click/change), so Program.cs no longer cares which UI is active.
2. Base Frame: a reusable “UiFrame” that owns the window, routes events, and provides slots/layers: Header, Content, OverlayStack. ChatSurface plugs into Content. “Menu” and “UiForm” render as modal overlays in OverlayStack.
3. Converged Rendering: RenderMenu and ShowFormAsync are built on UiNode and mounted as overlays above Content.
4. Terminal Virtual-DOM: Add a retained, incremental renderer that diffs UiNode trees to avoid full re-render flashes and to simulate a DOM: predictable z-order, focus, and patching parity with Photino.
5. ChatSurface cleanup: RenderChatMessage/RenderChatHistory remain for tools and legacy output, but core chat uses ChatSurface only.

This extends the UiNode/UiPatch model and the already-integrated ChatSurface loop.

## Interfaces & Data Shapes ##
### New frame model (C#) ###
```
// Frame has 3 layers: Header (toolbar), Content (a single mounted surface), OverlayStack (0..N modals)
public sealed record UiFrame(
    UiNode Header,           // e.g., thread title, buttons
    UiNode Content,          // e.g., ChatSurface root
    IReadOnlyList<UiNode> Overlays // e.g., MenuOverlay, FormOverlay (topmost is last)
);

// Marker props we standardize in node.Props for routing & layout:
public static class UiProps
{
    public const string ZIndex = "zIndex";          // int
    public const string Role   = "role";            // "frame|header|content|overlay"
    public const string Modal  = "modal";           // bool
    public const string Focusable = "focusable";   // bool
}
```
### Base Frame builder ###
```
public static class UiFrameBuilder
{
    // Creates a frame tree with semantic keys for stable patching.
    public static UiNode Create(UiFrame frame) { /* returns root UiNode:
      root(Column role=frame)
        ├── header(Row role=header)
        ├── content(Column role=content)
        └── overlays(Column role=overlay) // children layered by zIndex
    */ }

    // Patch helpers:
    public static UiPatch ReplaceContent(UiNode newContent);
    public static UiPatch PushOverlay(UiNode overlay);  // top-most modal
    public static UiPatch PopOverlay();                 // remove top-most
    public static UiPatch ReplaceHeader(UiNode newHeader);
}
```
### Unified I/O routing ###
```
// Backend-agnostic dispatcher: converts ControlEvent (Photino) or key sequences (Terminal)
// into UiEvents; bridges enter/click on "composer.input" or "send-btn" into input text.
public interface IInputRouter
{
    // binds to IUi to receive ControlEvent / key events
    void Attach(IUi ui);

    // returns a user-submitted text (null on close/exit)
    Task<string?> ReadLineAsync(CommandManager commands);

    // optional: propagate TextBox onChange to callers
    event Action<string> OnInputChanged;
}
```
* Photino backend already emits ControlEvent and maps enter/click to _tcsReadLine (you’ve started this); refactor that plumbing behind IInputRouter.
* Terminal backend maps keys to UiEvents (enter submits, Shift+Enter inserts newline) and feeds the same router.

## Overlay primitives (built on UiNode) ##
### MenuOverlay (replaces legacy RenderMenu) ###
```
public static class MenuOverlay
{
    // Builds a modal overlay node with a list view, filter box, and OK/Cancel
    // keys: "overlay-menu", "overlay-menu-title", "overlay-menu-filter", "overlay-menu-list", "overlay-menu-ok", "overlay-menu-cancel"
    public static UiNode Create(string title, IReadOnlyList<string> choices, int selectedIndex = 0);

    // Drives the overlay interaction and returns the chosen string (or null if cancelled)
    public static Task<string?> ShowAsync(IUi ui, string title, IReadOnlyList<string> choices, int selectedIndex = 0);
}
```
### FormOverlay (replaces legacy Photino ShowForm message path) ###
```
public static class FormOverlay
{
    // Renders an existing UiForm model as a modal overlay on top of the frame.
    // The implementation adapts UiForm.Fields into UiNode inputs, collects values, runs TrySetFromString + Validate.
    public static UiNode Create(UiForm form);

    public static Task<bool> ShowAsync(IUi ui, UiForm form);
}
```
    Note: IUi.ShowFormAsync(UiForm) remains for back-compat but internally calls FormOverlay.ShowAsync(ui, form) so Program.cs doesn’t change.

## Terminal Virtual-DOM (incremental renderer) ##
Add an incremental diff renderer to Terminal.cs that consumes the already-retained UiNodeTree and computes a line diff rather than full clear + redraw:
```
// New in Terminal.cs
private sealed class TermDom
{
    // Flattens UiNode -> lines with attributes (fg/bg), maintains key→region map
    public TermSnapshot Layout(UiNode root, int width);

    // Computes minimal edits to transform old snapshot into new snapshot
    public IEnumerable<TermEdit> Diff(TermSnapshot oldSnap, TermSnapshot newSnap);

    // Applies edits to console without scrolling, respects z-index (overlays last)
    public void Apply(IEnumerable<TermEdit> edits);
}
```
* UiNodeTree.ApplyPatch stays the source of truth & atomicity (already done). Terminal just stops clearing the screen and instead applies the TermEdits for changed regions, yielding DOM-like behavior.

## Flow & Ownership ##
1. Program.cs mounts the Frame once and swaps Content via patches. ChatSurface is the initial Content.
2. ReadInputWithFeaturesAsync becomes a thin call into IInputRouter.ReadLineAsync, which is event-driven in both backends.
3. When you need a menu: push MenuOverlay onto the frame, await choice, then pop.
4. When you need a form: push FormOverlay, await submit/cancel, then pop.
5. ChatSurface continues to issue fine-grained patches (AppendMessage, UpdateMessageContent, UpdateInput) as today.

## Examples (Given / When / Then) ##
1. Frame Mount + ChatSurface
 * Given no surface is mounted,
 * When ui.SetRootAsync(UiFrameBuilder.Create(new UiFrame(Header=ChatHeader(...), Content=ChatSurface.Create(...), Overlays=[])), new UiControlOptions(TrapKeys:true, InitialFocusKey:"composer.input")),
 * Then the window shows a header with thread name, the messages panel, and the composer focused.

2. Unified Input Submit
 * Given IInputRouter is attached and the composer has text "hello",
 * When the user presses Enter or clicks “Send”,
 * Then ReadLineAsync(...) completes with "hello" in both Terminal and Photino.

3. Open Menu Overlay
 * Given ChatSurface is in Content,
 * When MenuOverlay.ShowAsync(ui, "system commands", choices, selected:0) is called,
 * Then a modal overlay appears on top (zIndex > content), keyboard routes to the overlay list; Escape cancels; selection returns the chosen string.

4. Show Form Overlay
 * Given a UiForm with various field kinds,
 * When FormOverlay.ShowAsync(ui, form) is called,
 * Then the overlay renders all fields; on submit it performs per-field TrySetFromString and optional form.Validate and only closes when valid; focus returns to the composer. (Terminal uses inputs + inline errors; Photino uses DOM).

5. Terminal vDOM Patch
 * Given a frame with Content=ChatSurface and no overlays,
 * When ChatSurface.AppendMessage(...) applies a patch,
 * Then Terminal updates only the message region lines (no full clear), preserving scroll and cursor stability.

6. Overlay Stack
 * Given two overlays are pushed (Menu, then Form),
 * When the top overlay pops,
 * Then keyboard/focus returns to the previous overlay until the stack is empty, then to the composer.

## Invariants & Non-Functionals ##
* Atomic patches: Still enforced by UiNodeTree.ApplyPatch (all-or-nothing).
* Unique keys: Enforced for every subtree; duplicates fail fast.
* Focus singularity: Exactly one focusable node at a time; overlay focus supersedes content focus.
* Z-order: Header < Content < OverlayStack (in insertion order).
* Parity: UI semantics match across Photino and Terminal. Terminal vDOM must keep flicker low and avoid buffer scrolling.
* Perf: P95 10ms to render a single small patch (≤5 ops) @ 120×40 terminal; P95 30ms for menu/form open/close transitions.
* Backcompat: Keep IUi.RenderMenu, IUi.ShowFormAsync, IUi.RenderChatMessage/RenderChatHistory for existing tools; internally route them through frame overlays or ChatSurface where practical.

## Interfaces to Add/Adjust ##
* IInputRouter (new) with concrete implementations:
    * PhotinoInputRouter reads ControlEvent from Photino and maps enter/click to submit; change → OnInputChanged.
    * TerminalInputRouter maps key events to the same model (Enter submit; Shift+Enter newline; ESC → command palette hook).
* UiFrameBuilder (new helper).
* MenuOverlay & FormOverlay (new) implemented atop UiNode.
* Terminal.TermDom (new) for incremental rendering.

No changes needed to IUi.SetRootAsync/PatchAsync/FocusAsync (already present).

## Migration Plan (high level) ##
1. Introduce UiFrameBuilder and mount it in Program.cs (wrap existing ChatSurface as Content).
2. Extract I/O: Replace direct _tcsReadLine and Terminal input loop with IInputRouter. Both backends call the same ReadLineAsync.
3. Overlay re-routes:
    * Make IUi.RenderMenu call MenuOverlay.ShowAsync internally.
    * Make IUi.ShowFormAsync call FormOverlay.ShowAsync.
4. Terminal vDOM: swap Terminal’s full re-render in PatchAsync for TermDom.Diff+Apply while keeping UiNodeTree as source of truth.
5. Peel RenderChatMessage/RenderChatHistory from IUi later; tools can post Tool messages that ChatSurface displays (or keep legacy rendering for tools).


Done
 * [ ] Code: UiFrameBuilder, MenuOverlay, FormOverlay, IInputRouter (+ Photino/Terminal implementations), Terminal.TermDom
 * [ ] Program.cs uses UiFrameBuilder + IInputRouter.ReadLineAsync
 * [ ] Parity tests: Photino vs Terminal overlay behavior & input submit
 * [ ] Perf: P95 budgets verified (patch apply, overlay open/close)
 * [ ] Docs: “Building on the Base Frame”, “Overlay authoring”, “Terminal vDOM” guide