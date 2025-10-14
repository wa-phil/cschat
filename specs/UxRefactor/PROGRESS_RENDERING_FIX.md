# Progress Rendering Fix - Missing LayoutNode Implementation

## Problem Identified

The user correctly identified that progress updates were not showing in real-time. The UiNode-based progress implementation was **patching the tree** but **not rendering the updates**.

### Root Cause

When I initially implemented Progress as a UiNode:

1. ✅ Added `UiKind.Progress` enum value
2. ✅ Added progress properties to `UiProperty`
3. ✅ Implemented `StartProgress`, `UpdateProgress`, `CompleteProgress` in `CUiBase`
4. ❌ Added `case UiKind.Progress:` to **`RenderNode`** method (old fallback path)
5. ❌ **MISSED** adding `case UiKind.Progress:` to **`TermDom.LayoutNode`** method (actual rendering path)

### Why RenderNode Wasn't Enough

Terminal has **two rendering paths**:

1. **Old/Simple**: `RenderNode(UiNode node, int indent)` - Direct console rendering (obsolete, not used)
2. **New/Virtual DOM**: `TermDom.LayoutNode(UiNode node, ...)` - Converts UiNodes to `TermLine` objects for incremental rendering

The `PostPatchAsync` method uses the **Virtual DOM path**, which calls `_termDom.Layout()` → `LayoutNode()`. My original implementation only added Progress to the old path, so updates were being patched but never rendered!

## Solution

Added `case UiKind.Progress:` to `TermDom.LayoutNode` method in Terminal.cs (line ~1450).

### Implementation Details

The Progress rendering in `LayoutNode`:

```csharp
case UiKind.Progress:
    // Extract properties from the UiNode
    var progressTitle = node.Props.TryGetValue(UiProperty.Title, ...) ?? "Progress";
    var progressItems = node.Props.TryGetValue(UiProperty.ProgressItems, ...) as IReadOnlyList<...>;
    var progressStats = node.Props.TryGetValue(UiProperty.ProgressStats, ...) as ValueTuple<...>?;
    var etaHint = node.Props.TryGetValue(UiProperty.EtaHint, ...) ? ... : null;

    if (progressItems != null && progressStats.HasValue)
    {
        // Render box-drawing header
        lines.Add(new TermLine("┌─────...─┐", ConsoleColor.DarkGray, ...));
        lines.Add(new TermLine("│ Title... │", ConsoleColor.Green, ...));
        lines.Add(new TermLine("├─────...─┤", ConsoleColor.DarkGray, ...));

        // Filter and sort items (Running > Queued > Failed)
        var rows = progressItems
            .Where(x => x.state != Completed && x.state != Canceled)
            .OrderByDescending(x => Rank(x.state))
            .Take(topN)
            .ToList();

        // Render each row with glyph, name, percent, steps
        foreach (var r in rows)
        {
            var glyph = r.state switch { Running => "▶", Failed => "✖", ... };
            var left = $"{glyph} {r.name}";
            var right = $"{r.percent:0.0}% ({r.steps.done}/{r.steps.total})";
            // Compose left/right justified line
            var composed = left + spacing + right;
            lines.Add(new TermLine(composed, fg, bg, TextAlign.Left));
        }

        // Stats footer
        lines.Add(new TermLine($"in-flight: {running} queued: {queued} ...", ...));
        lines.Add(new TermLine($"ETA: {etaHint} • Press ESC to cancel", ...));
    }
    break;
```

### Key Features

1. **Box-drawing characters**: ┌, ─, ┐, │, ├, ┤ for nice borders
2. **Glyphs**: ▶ Running, ✓ Completed, ✖ Failed, ■ Canceled, • Queued
3. **Filtering**: Only shows Running, Queued, Failed (hides Completed/Canceled)
4. **Sorting**: Running (priority 3) > Queued (2) > Failed (1)
5. **Left/Right justification**: Name on left, percent + steps on right
6. **Color coding**: Failed items in Yellow, others in Gray
7. **Stats footer**: Real-time counts of all states
8. **ETA + ESC hint**: User feedback

## What Was Happening Before

```
AsyncProgress.Run() calls UpdateProgress() every 100ms
    ↓
CUiBase.UpdateProgress() patches the UiNode tree
    ↓
PatchAsync() calls PostPatchAsync()
    ↓
Terminal.PostPatchAsync() calls _termDom.Layout(root)
    ↓
TermDom.Layout() calls LayoutNode() for each node
    ↓
LayoutNode() switch statement... MISSING case UiKind.Progress!
    ↓
Progress node ignored, no TermLine objects created
    ↓
No visual update in terminal 😞
```

## What Happens Now

```
AsyncProgress.Run() calls UpdateProgress() every 100ms
    ↓
CUiBase.UpdateProgress() patches the UiNode tree
    ↓
PatchAsync() calls PostPatchAsync()
    ↓
Terminal.PostPatchAsync() calls _termDom.Layout(root)
    ↓
TermDom.Layout() calls LayoutNode() for each node
    ↓
LayoutNode() switch: case UiKind.Progress renders box, items, footer
    ↓
TermLine objects created with glyphs, colors, text
    ↓
_termDom.Diff() computes minimal edits
    ↓
_termDom.Apply() writes to console at specific positions
    ↓
Progress updates appear in real-time! ✅
```

## Photino Status

Photino rendering already works because:

1. `SerializeNode()` sends all UiNode properties to JavaScript
2. `index.html` has `case 'Progress':` in `renderNode()` function
3. JavaScript renders progress bars with animations

No changes needed for Photino.

## Testing

To test, run any command that uses `AsyncProgress`:

```bash
# Terminal test
/ingest path/to/directory

# Should see:
┌─────────────────────────────────────┐
│      Adding content to RAG          │
├─────────────────────────────────────┤
▶ file1.cs               45.0% (9/20)
▶ file2.cs               20.0% (4/20)
• file3.cs                0.0% (0/20)
in-flight: 2   queued: 1   completed: 0   failed: 0   canceled: 0
ETA: 5s • Press ESC to cancel
```

Progress should update every 100ms as items are processed.

## Files Changed

- `UX/Terminal.cs` - Added `case UiKind.Progress:` to `TermDom.LayoutNode` method (~80 lines)

## Lessons Learned

1. **Check all rendering paths**: Terminal has multiple (old simple, new virtual DOM)
2. **Follow the call chain**: StartProgress → PatchAsync → PostPatchAsync → Layout → LayoutNode
3. **Virtual DOM is the real path**: The simple RenderNode is legacy/unused
4. **Test with real scenarios**: Static analysis can't catch "node type not handled in switch"

## Related Files

- `UX/CUiBase.cs` - Base implementation (already correct)
- `UX/Progress.cs` - AsyncProgress.Builder.Run (unchanged, already correct)
- `Engine.cs` - Calls AsyncProgress.For(...).Run(...) (unchanged)
- `UX/wwwroot/index.html` - Photino rendering (already correct)
