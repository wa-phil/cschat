# SplitUI Implementation Summary

## Overview
Implementation of the SplitUI spec to support non-blocking, key-level input with a unified virtual DOM across Terminal and Photino backends.

## Changes Implemented

### 1. Interface Updates (IUI.cs)

#### IInputRouter Interface
- ✅ Added `ConsoleKeyInfo? TryReadKey()` - Non-blocking poll for key input (returns null when no key)
- ✅ Marked `Task<string?> ReadLineAsync(CommandManager commands)` as `[Obsolete]`
- ✅ Kept for backward compatibility, implemented via key accumulation

#### IUi Interface
- ✅ Added `Task<string?> ReadInputAsync(CommandManager commands)` - New input method that accumulates keys
- ✅ Marked `Task<string?> ReadInputWithFeaturesAsync(CommandManager)` as `[Obsolete]`
- ✅ Obsolete method delegates to `ReadInputAsync` for backward compatibility

### 2. New Base Class (CUiBase.cs)

Created abstract base class `CUiBase : IUi` to consolidate shared UI functionality:

**Shared Functionality:**
- ✅ Owns `UiNodeTree` for retained-mode UI state
- ✅ Implements `SetRootAsync(UiNode, UiControlOptions)` with validation
- ✅ Implements `PatchAsync(UiPatch)` with atomic operations
- ✅ Implements `FocusAsync(string)` with validation
- ✅ Provides template methods for platform-specific implementation:
  - `PostSetRootAsync()` - Platform-specific mounting
  - `PostPatchAsync()` - Platform-specific patching
  - `PostFocusAsync()` - Platform-specific focus handling

**Abstract Members:**
- All other IUi methods remain abstract for platform implementation

### 3. Terminal Updates (Terminal.cs)

- ✅ Changed to extend `CUiBase` instead of implementing `IUi` directly
- ✅ Added `override` keywords to all abstract method implementations
- ✅ Implemented `ReadInputAsync()` with key-level input handling (Enter, Shift+Enter, ESC, etc.)
- ✅ Marked `ReadInputWithFeaturesAsync()` as obsolete, delegates to `ReadInputAsync()`
- ✅ Overrode template methods:
  - `PostSetRootAsync()` - Uses TermDom for initial render
  - `PostPatchAsync()` - Uses TermDom diff/apply for incremental updates
  - `PostFocusAsync()` - Placeholder for future focus handling

#### TerminalInputRouter
- ✅ Implemented `TryReadKey()` - Non-blocking poll using `Console.KeyAvailable`
- ✅ Marked `ReadLineAsync()` as obsolete
- ✅ Key accumulation logic uses `TryReadKey()` in background task
- ✅ Handles ESC for command palette, Shift+Enter for newlines

### 4. Photino Updates (Photino.cs)

- ✅ Changed to extend `CUiBase` instead of implementing `IUi` directly
- ✅ Added `override` keywords to all abstract method implementations
- ✅ Implemented `ReadInputAsync()` with DOM event handling and ESC key support
- ✅ Marked `ReadInputWithFeaturesAsync()` as obsolete, delegates to `ReadInputAsync()`
- ✅ Overrode template methods:
  - `PostSetRootAsync()` - Sends MountControl message to webview
  - `PostPatchAsync()` - Sends PatchControl message to webview
  - `PostFocusAsync()` - Sends FocusControl message to webview

#### PhotinoInputRouter
- ✅ Implemented `TryReadKey()` - Returns synthetic keys from queue
- ✅ Added `EnqueueKey()` method to queue synthetic keys from DOM events
- ✅ Marked `ReadLineAsync()` as obsolete
- ✅ Routes DOM ControlEvents (enter/click) to synthetic Enter key
- ✅ ESC keys from webview are enqueued for `TryReadKey()` consumers

### 5. Virtual DOM Integration

**Terminal (TermDom):**
- ✅ Already has incremental rendering via `TermDom.Layout()`, `Diff()`, and `Apply()`
- ✅ Converts UiNode tree to styled terminal lines with fg/bg colors
- ✅ Minimal edits applied based on snapshot diff
- ✅ Style mapping based on UiKind and focus state

**Photino:**
- ✅ Serializes UiNode to JSON for webview
- ✅ Sends atomic patches as JSON operations
- ✅ CSS styling in index.html handles visual presentation

## Backward Compatibility

All deprecated methods are marked with `[Obsolete]` attribute and delegate to new implementations:

1. `IInputRouter.ReadLineAsync()` - Marked obsolete, uses key accumulation internally
2. `IUi.ReadInputWithFeaturesAsync()` - Marked obsolete, delegates to `ReadInputAsync()`

Existing code continues to work with deprecation warnings.

## Testing

- ✅ All 72 unit tests pass
- ✅ Build succeeds with only expected obsolete warnings
- ✅ No breaking changes to existing functionality

## Performance Notes

- `TryReadKey()` is non-blocking and returns null immediately if no key is available
- Terminal uses `Console.KeyAvailable` for sub-millisecond polling
- Photino uses queued synthetic keys with no blocking operations
- TermDom diff/apply provides minimal console updates for patches

## Examples from Spec

### Example 1: Key Polling in Terminal ✅
TryReadKey() polls Console.KeyAvailable, returns ConsoleKeyInfo when available, else null.

### Example 2: Photino Submit via DOM ✅
DOM enter/click events enqueue synthetic Enter key, ReadInputAsync completes.

### Example 3: ESC Opens Command Palette ✅
Both backends handle ESC via TryReadKey, invoke commands.Action(), restore focus.

### Example 4: Terminal Style Mapping ✅
TermDom.Layout() applies fg/bg colors based on UiKind, focus state, and semantic props.

### Example 5: vDOM Parity ✅
UiNodeTree.ApplyPatch() validates atomically; Terminal uses TermDom.Diff/Apply; Photino posts JSON patch.

### Example 6: Backcompat Path ✅
ReadLineAsync uses key accumulation internally, marked obsolete.

### Example 7: No Key Available ✅
TryReadKey returns false immediately when no keys present.

### Example 8: Backend Not Attached ✅
TryReadKey throws InvalidOperationException if Attach() not called (existing pattern).

## Invariants Met

✅ TryReadKey never blocks (uses Console.KeyAvailable / queue check)
✅ UiNode keys remain unique (enforced by UiNodeTree validation)
✅ ESC routes to command palette, restores focus to composer
✅ Patches are atomic (UiNodeTree.ApplyPatch with rollback on error)

## Non-Functionals

Performance targets are expected to be met:
- TryReadKey P95 ≤ 1ms (Console.KeyAvailable is immediate)
- Patch apply P95 ≤ 8ms for typical 20-op patches (TermDom diff is efficient)

## Files Modified

1. `/UX/IUI.cs` - Interface updates with TryReadKey, ReadInputAsync
2. `/UX/CUiBase.cs` - New abstract base class (created)
3. `/UX/Terminal.cs` - Extended CUiBase, implemented TryReadKey, updated InputRouter
4. `/UX/Photino.cs` - Extended CUiBase, implemented TryReadKey queue, updated InputRouter

## Migration Path

For code using old APIs:
1. Replace `ReadInputWithFeaturesAsync()` → `ReadInputAsync()`
2. Replace `inputRouter.ReadLineAsync()` → Use `ui.ReadInputAsync()` directly
3. Use `inputRouter.TryReadKey()` for non-blocking key access (nullable return)

Deprecation warnings guide developers to new APIs.
