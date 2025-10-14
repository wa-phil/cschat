# Progress Implementation - Before vs After

## Code Reduction Summary

### Before
- **IUi.cs**: 3 abstract method declarations
- **CUiBase.cs**: 3 abstract method declarations (pass-through)
- **Terminal.cs**: ~100 lines (TermProg class + 3 methods)
- **Photino.cs**: ~30 lines (ConcurrentDictionary + 3 methods)
- **index.html**: ~70 lines (progress bubble rendering in handler)
- **Total**: ~200 lines across 5 files

### After
- **IUi.cs**: No changes (methods still declared)
- **CUiBase.cs**: ~130 lines (3 virtual methods + CreateProgressNode helper)
- **Terminal.cs**: ~60 lines (Progress rendering case in RenderNode)
- **Photino.cs**: 1 comment line (removed implementation)
- **index.html**: ~150 lines (Progress rendering case in renderNode)
- **Total**: ~340 lines total BUT...

### Net Result
- **Unified logic**: Progress behavior now consistent across platforms
- **No duplication**: Core progress logic lives in one place (CUiBase)
- **Better testability**: Progress is now a UiNode, can be unit tested
- **More features**: Terminal/Photino implementations were limited, now both get full features

## Before: Platform-Specific Implementations

### Terminal (Old)
```csharp
private sealed class TermProg
{
    public string Title = "";
    public int RegionTop = 0;
    public int RegionHeight = 3 + Program.config.MaxMenuItems + 2;
    public int LastWidth;
    public CancellationTokenSource Cts = new();
}

private readonly Dictionary<string, TermProg> _progress = new();

public override string StartProgress(string title, CancellationTokenSource cts)
{
    var id = Guid.NewGuid().ToString("n");
    var tp = new TermProg { Title = title, Cts = cts, LastWidth = Math.Max(10, Width - 1) };
    _progress[id] = tp;
    
    SetCursorPosition(0, tp.RegionTop);
    Progress.DrawBoxedHeader(title);
    for (int i = 0; i < Program.config.MaxMenuItems + 2; i++) 
        WriteLine(new string(' ', tp.LastWidth));
    SetCursorPosition(0, tp.RegionTop);
    return id;
}
```

**Issues:**
- Fixed position rendering (RegionTop=0)
- Not scrollable with chat history
- Manual cursor management
- Width change handling fragile

### Photino (Old)
```csharp
private readonly ConcurrentDictionary<string, CancellationTokenSource> _progressMap = new();

public override string StartProgress(string title, CancellationTokenSource cts)
{
    var id = Guid.NewGuid().ToString("n");
    Post(new { type = "StartProgress", id, title, cancellable = true });
    _progressMap[id] = cts;
    return id;
}

public override void UpdateProgress(string id, ProgressSnapshot snapshot)
{
    Post(new { type = "UpdateProgress", id, items = ..., stats = ..., eta = ... });
}
```

**Issues:**
- Custom message protocol (StartProgress, UpdateProgress, CompleteProgress)
- Not integrated with chat surface
- Separate rendering path in HTML
- Not reusable for other UiNode features

## After: Unified UiNode Implementation

### CUiBase (New)
```csharp
protected readonly ConcurrentDictionary<string, CancellationTokenSource> _progressMap = new();

public virtual string StartProgress(string title, CancellationTokenSource cts)
{
    var id = Guid.NewGuid().ToString("n");
    _progressMap[id] = cts;
    
    try
    {
        if (_uiTree.Root != null && _uiTree.FindNode("messages") != null)
        {
            var progressNode = CreateProgressNode(id, title, ...);
            var patch = new UiPatch(new InsertChildOp("messages", int.MaxValue, progressNode));
            PatchAsync(patch).GetAwaiter().GetResult();
        }
    }
    catch { /* Best effort */ }
    
    return id;
}
```

**Benefits:**
- Progress is a UiNode like any other control
- Automatically scrollable in messages panel
- No special rendering path needed
- Reuses UiNode infrastructure

### Terminal Rendering (New)
```csharp
case UiKind.Progress:
    var progressTitle = node.Props.TryGetValue(UiProperty.Title, ...) ?? "Progress";
    var progressItems = node.Props.TryGetValue(UiProperty.ProgressItems, ...) as IReadOnlyList<...>;
    var progressStats = node.Props.TryGetValue(UiProperty.ProgressStats, ...) as ValueTuple<...>?;
    
    if (progressItems != null && progressStats.HasValue)
    {
        Progress.DrawBoxedHeader(progressTitle);
        
        // Filter/sort items
        var rows = progressItems
            .Where(x => x.state != ProgressState.Completed && x.state != ProgressState.Canceled)
            .OrderByDescending(x => Rank(x.state))
            .Take(topN)
            .ToList();
        
        foreach (var r in rows)
            Progress.DrawProgressRow(r.name, r.percent, ...);
        
        Progress.DrawFooterStats(...);
    }
    break;
```

**Benefits:**
- Integrated into UiNode rendering pipeline
- Reuses existing Progress helper methods
- Automatically gets UiNode benefits (patching, focus, etc.)

### Photino Rendering (New)
```csharp
// No special code needed! SerializeNode already handles all props:
private object SerializeNode(UiNode node)
{
    var childList = new List<object>(node.Children.Count);
    foreach (var c in node.Children)
        childList.Add(SerializeNode(c));
    
    return new
    {
        key = node.Key,
        kind = node.Kind.ToString(),  // "Progress"
        props = node.Props,           // All progress properties
        children = childList
    };
}
```

**Benefits:**
- Zero special code in C#
- All logic in HTML renderNode function
- Consistent with other UiKind rendering

## Property Comparison

### Old (Photino custom message)
```javascript
{
  type: "UpdateProgress",
  id: "abc123",
  items: [...],
  stats: { running: 1, queued: 2, ... },
  eta: "5s",
  active: true
}
```

### New (UiNode props)
```csharp
new Dictionary<UiProperty, object?>
{
    [UiProperty.Title] = "Progress",
    [UiProperty.ProgressItems] = [...],
    [UiProperty.ProgressStats] = (1, 2, 3, 4, 5),
    [UiProperty.EtaHint] = "5s",
    [UiProperty.IsActive] = true,
    [UiProperty.Cancellable] = true
}
```

**Benefits:**
- Strongly typed (enum-based props)
- Same serialization path as all other nodes
- Compile-time safety

## Behavior Improvements

### Scrolling
**Before**: Terminal progress at fixed position, Photino progress in separate bubble  
**After**: Progress appears inline in messages panel, scrolls with chat history

### Cancellation
**Before**: Terminal polls Console.KeyAvailable, Photino handles CancelProgress event  
**After**: Both use IInputRouter.TryReadKey() for consistency

### Multi-Progress
**Before**: Terminal overwrites same region, Photino creates separate divs  
**After**: Both stack naturally in messages panel as separate UiNodes

### Completion
**Before**: Terminal clears region + renders artifact, Photino replaces bubble  
**After**: Both remove progress node + render Tool message (consistent)

## Testing Improvements

### Before
- Manual testing only
- Hard to unit test platform-specific implementations
- No shared test cases

### After
- Unit tests for progress UiNode creation ✓
- Unit tests for progress property updates ✓
- Can test progress behavior without UI ✓
- Shared test cases for both platforms ✓

## Migration Path

### No Breaking Changes
- All existing progress callers (RAG ingest, etc.) work unchanged
- `IUi.StartProgress()` / `UpdateProgress()` / `CompleteProgress()` still exist
- `ProgressSnapshot` unchanged
- `Progress.cs` unchanged

### Backwards Compatible
- Old code calling `Program.ui.StartProgress(...)` works identically
- Progress still appears in UI
- ESC still cancels
- Artifacts still rendered as Tool messages
