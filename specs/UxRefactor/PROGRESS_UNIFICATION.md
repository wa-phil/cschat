# Progress Unification - Implementation Summary

## Overview
Refactored progress reporting from platform-specific implementations to a unified UiNode-based approach in `CUiBase.cs`, mirroring the pattern used by `RealtimeWriter`.

## Changes Made

### 1. UiNode Extensions (`UX/UiNode.cs`)
- **Added `UiKind.Progress`** - New node type for progress displays
- **Added Progress-specific properties to `UiProperty`**:
  - `ProgressItems` - List of progress item data (name, percent, state, note, steps)
  - `ProgressStats` - Statistics tuple (running, queued, completed, failed, canceled)
  - `EtaHint` - Estimated time remaining string
  - `IsActive` - Whether progress is still ongoing
  - `Cancellable` - Whether progress can be cancelled via ESC

### 2. CUiBase.cs - Unified Implementation
**Moved progress methods from abstract to virtual with UiNode-based implementation:**

```csharp
protected readonly ConcurrentDictionary<string, CancellationTokenSource> _progressMap;

public virtual string StartProgress(string title, CancellationTokenSource cts)
public virtual void UpdateProgress(string id, ProgressSnapshot snapshot)
public virtual void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown)
```

**Key Features:**
- Progress nodes are ephemeral messages inserted into the `messages` panel
- Marked with `ChatMessageState.EphemeralActive` so they don't persist to chat history
- ESC key handling delegates to platform-specific `IInputRouter.TryReadKey()`
- On completion, progress node is removed and final artifact is rendered as a Tool message
- Graceful fallback if messages panel doesn't exist yet

### 3. Terminal.cs - Rendering Updates
**Removed old implementation:**
- Deleted `TermProg` class and `_progress` dictionary
- Removed specialized `StartProgress`, `UpdateProgress`, `CompleteProgress` methods

**Added Progress rendering in `RenderNode`:**
- Renders `UiKind.Progress` nodes using existing `Progress` helper class
- Calls `Progress.DrawBoxedHeader()`, `Progress.DrawProgressRow()`, `Progress.DrawFooterStats()`
- Filters and sorts items (Running > Queued > Failed)
- Shows top N items (configurable via `Program.config.MaxMenuItems`)
- Displays cancellation hint and ETA

### 4. Photino.cs - Rendering Updates
**Removed old implementation:**
- Deleted `_progressMap` and specialized progress methods
- Progress nodes now inherit `_progressMap` from `CUiBase`

**Note:** `SerializeNode()` already handles all UiNode properties generically, so Progress nodes automatically serialize to JavaScript

### 5. index.html - Web View Rendering
**Added `case 'Progress'` to `renderNode()` function:**

**Rendering Features:**
- Styled as `.bubble.tool` (same as tool messages)
- Title header with bold text
- Filtered/sorted progress items (active work only)
- Animated progress bars with color-coded states:
  - Running: Blue (#4a9eff)
  - Failed: Red (#e74c3c)
  - Completed: Green (#2ecc71)
  - Queued: Gray (#95a5a6)
- State glyphs: ▶ (Running), ✓ (Completed), ✖ (Failed), ■ (Canceled), • (Queued)
- Percentage and step counts (e.g., "45.0% (9/20)")
- Stats footer with counts and ETA
- ESC hint when active

**Data Handling:**
- Handles both tuple format (`Item1`, `Item2`, etc.) and named properties
- Gracefully handles missing or null data

## Benefits

1. **Single Source of Truth**: Progress logic lives in `CUiBase`, not duplicated across platforms
2. **Consistent Behavior**: ESC cancellation, ETA hints, state filtering work identically everywhere
3. **Scrollable History**: Progress appears in messages panel alongside chat, can be scrolled
4. **Ephemeral by Design**: Marked as `EphemeralActive`, automatically excluded from persistence
5. **Incremental Rendering**: UiNode patches allow efficient updates without full redraws
6. **Extensibility**: Easy to add new progress visualizations or properties

## Compatibility Notes

- **Progress.cs unchanged**: `AsyncProgress.Builder.Run()` still calls `IUi` methods, which now delegate to UiNode implementation
- **Existing progress code unaffected**: All callers (RAG ingest, etc.) continue to work without changes
- **Terminal static helpers preserved**: `Progress.DrawBoxedHeader()`, `Progress.DrawProgressRow()`, etc. still used by Terminal renderer

## Testing Recommendations

1. **RAG Ingest**: `/ingest <directory>` - verify progress shows items, updates, cancels with ESC
2. **Window Resize (Terminal)**: Verify no scrolling or corruption during progress updates
3. **Photino UI**: Verify progress bubbles appear in chat, animate smoothly, handle cancellation
4. **Multiple Progress**: Start multiple operations, verify they stack correctly in messages panel
5. **Chat Surface Integration**: Verify progress doesn't interfere with ongoing chat or realtime messages

## Future Enhancements

- **Expand/Collapse**: Add accordion-style hiding of completed items
- **Pause/Resume**: Add pause button to halt progress temporarily
- **Detailed View**: Click item to see logs or errors
- **Notifications**: Desktop notifications for long-running operations
- **History**: Option to persist progress summaries to chat history
