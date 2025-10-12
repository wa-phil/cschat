# SplitUI – Spec

## Problem & Intent
Program.cs and several features still rely on ReadLineAsync (string), which forces backend-specific logic and blocks on line input. We want non-blocking, key-level input so Terminal can behave like a proper TTY while Photino keeps its DOM-first UX. We’ll also generalize the “virtual DOM” so both backends share a retained model (UiNodeTree) with backend-specific styling, and move shared implementation into a new CUiBase to reduce duplication. Current interfaces show ReadLineAsync on IInputRouter and ReadInputWithFeaturesAsync on IUi; Terminal already has an incremental TermDom and per-key handling we can build on.

## Interfaces & Data Shapes
- Function(s)
 * interface IInputRouter
   * void Attach(IUi ui); (unchanged)
    * ConsoleKeyInfo? TryReadKey();
   Non-blocking poll; returns true and fills key if a key is available, else false. Backend must not block the caller.
   * event Action<string>? OnInputChanged; (unchanged; Photino uses DOM change events)
   * Deprecated: Task<string?> ReadLineAsync(CommandManager commands); (kept for backcompat; implemented via key accumulation).
 * interface IUi
   * Replace Task<string?> ReadInputWithFeaturesAsync(CommandManager) with:
     * Task<string?> ReadInputAsync(CommandManager commands);
     Accumulates ConsoleKeyInfo from IInputRouter.TryReadKey into a buffer and resolves on submit (Enter), honoring Shift+Enter for newline. Photino routes DOM “enter/click” to synthetic keys; Terminal uses real keys (see Terminal currently reading keys and accumulating lines).
   * ConsoleKeyInfo ReadKey(bool intercept); remains for low-level needs today.
 * abstract partial class CUiBase : IUi (new)
   * Owns UiNodeTree mounting/patching, focus routing, overlay helpers. Photino and Terminal already duplicate these ideas; Photino serializes nodes & patches for the web view, and both backends perform focus & patch logic that can be centralized.
   * Provides:
     * Task SetRootAsync(UiNode root, UiControlOptions? options = null);
     * Task PatchAsync(UiPatch patch);
     * Task FocusAsync(string key);
     * Minimal template methods for backend-specific transport (e.g., PostPatch, PostFocus).

- Data
 * UiNode, UiPatch, UiNodeTree (retained control model; unchanged). These already validate uniqueness, build maps, and apply atomic patches.
 * UiFrame & UiFrameBuilder stay as the base layout (Header/Content/Overlays).

## Examples (Given / When / Then)
1. Key polling in Terminal
    Given TerminalInputRouter is attached, When the app loop calls TryReadKey() repeatedly, Then printable keys are appended, Backspace edits, Enter submits, and Shift+Enter inserts a newline (Terminal already handles these semantics today via key reading & accumulation).
2. Photino submit via DOM → key stream
    Given Photino raises ControlEvent “enter” on the text box or a click on “send”, When PhotinoInputRouter receives it, Then it emits a synthetic ConsoleKeyInfo sequence (ending with Enter) so ReadInputAsync completes just like Terminal. Photino today maps change/enter/click into ReadLineAsync TCS; this will be re-routed through the key path.
3. Escape opens command palette
    Given either backend, When ESC is read via TryReadKey, Then commands.Action() runs and focus returns to composer (Photino already listens for ESC and refocuses input; Terminal does similar).
4. Terminal style mapping
    Given a UiNode ListView with selectedIndex, When Terminal renders, Then the selected row is styled (fg/bg) and focus is highlighted; this already exists in TermDom and will be formalized as style rules per UiKind.
5. vDOM parity
    Given a patch updates only part of the content, When UiNodeTree.ApplyPatch runs, Then Terminal applies minimal line edits (TermDom.Diff/Apply) and Photino posts a JSON patch to the webview.
6. Backcompat path
    Given a caller still uses ReadLineAsync, When invoked, Then the router internally accumulates keys until Enter and returns the submitted string (bridging period).
7. Edge case: no key available
    Given TryReadKey is called in a tight loop with no input, When no keys are present, Then it returns false immediately; the app loop remains responsive.
8. Failure: backend not attached
    Given IInputRouter.Attach has not been called, When TryReadKey or ReadInputAsync is used, Then an InvalidOperationException is thrown (matches existing pattern in routers).

## Invariants & Non-Functionals
 * Invariant:
    * TryReadKey must never block.
    * UiNode keys remain unique; patches are atomic (already enforced by UiNodeTree).
    * ESC always routes to the command palette in both backends and restores focus to composer.
* Perf:
    * P95 ≤ 1 ms per TryReadKey call with no input (hot path).
    * P95 ≤ 8 ms to apply a typical UiPatch of ≤ 20 ops in Terminal (diff+apply).
* Backcompat:
    * IInputRouter.ReadLineAsync(...) remains but is implemented via key accumulation and marked [Obsolete].
    * IUi.ReadInputWithFeaturesAsync(...) remains but delegates to ReadInputAsync(...) and is marked [Obsolete]. Photino’s existing focus and ESC logic keep working.

## Additional Design Notes ##
### Generic vDOM with backend styling ###
 * Keep UiNodeTree as the retained model for both backends (already present).
 * Terminal style system: introduce a small style mapper that converts common props (e.g., role, focusable, semantic states) to (ForegroundColor, BackgroundColor) per UiKind at render time; TermDom already builds TermLine(fg, bg) lists.
 * Photino style: remains CSS in index.html; events and patches are already transported (ControlEvent, PatchControl).

### Refactor: move shared code into CUiBase ###
 * Consolidate: node serialization helpers, frame mount/patch/focus bookkeeping, overlay push/pop convenience built on UiFrameBuilder (already standardized roles/zIndex).
 * Each platform implements only: transport (Post/PatchControl, focus message), and platform-specific I/O like file pickers and rendering sinks. Photino currently serializes nodes and posts to the webview; Terminal performs diff/line write—both sit atop the same base.

## Done
- [ ] Tests cover examples & invariants
- [ ] Perf budget verified
- [ ] Docs/changelog updated
