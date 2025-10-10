# UiNode/UiPatch Implementation Summary

This document summarizes the implementation of the declarative control layer (UiNode/UiPatch) as specified in `UxRefactor.md`.

## Files Created/Modified

### New Files

1. **UX/UiNode.cs** - Core data types
   - `UiKind` enum with all control types
   - `UiNode` record for declarative UI nodes
   - `UiEvent` record for UI events
   - `UiControlOptions` record for mount options
   - `UiHandler` delegate for event handlers
   - `UiOp` abstract base class and concrete operations:
     - `ReplaceOp` - Replace an entire node
     - `UpdatePropsOp` - Update node properties
     - `InsertChildOp` - Insert a child at index
     - `RemoveOp` - Remove a node
   - `UiPatch` record with convenience methods

2. **UX/UiNodeTree.cs** - Tree management and validation
   - Manages retained-mode UI tree
   - Validates unique keys across subtree
   - Validates props for node kinds
   - Applies patch operations atomically
   - Maintains focus state
   - Provides rollback on patch failure

3. **UX/UiNodeExamples.cs** - Usage examples
   - Example implementations for all scenarios in the spec
   - Chat surface mounting
   - Message streaming
   - Mail surface
   - Interactive forms
   - Accordions

4. **unittests/UiNodeTests.cs** - Unit tests
   - Tests for node creation
   - Patch operation tests
   - Validation tests (duplicate keys, missing keys)
   - Focus management tests
   - Atomic patch application tests

### Modified Files

1. **Errors.cs**
   - Added `PlatformNotReadyException` class

2. **UX/IUI.cs**
   - Added three new methods to IUi interface:
     - `Task SetRootAsync(UiNode root, UiControlOptions? options = null)`
     - `Task PatchAsync(UiPatch patch)`
     - `Task FocusAsync(string key)`

3. **UX/Terminal.cs**
   - Full implementation of SetRootAsync, PatchAsync, FocusAsync
   - Terminal-based rendering of all UiKind types
   - Focus management with visual indicators
   - Tree state management

4. **UX/Photino.cs**
   - Full implementation of SetRootAsync, PatchAsync, FocusAsync
   - JSON serialization for web view communication
   - Message posting for MountControl, PatchControl, FocusControl
   - Platform readiness validation

## Implementation Details

### SetRootAsync

**Purpose**: Mounts a new control surface as the root of the UI tree.

**Validation**:
- Throws `ArgumentNullException` if root is null
- Throws `InvalidOperationException` if duplicate keys exist in subtree
- Throws `PlatformNotReadyException` if Photino UI not initialized

**Side Effects**:
- Clears previous surface
- Renders root node
- Sets up key trapping (if enabled)
- Sets initial focus (if specified)

**Terminal Implementation**:
- Clears console
- Renders tree using `RenderNode()` helper
- Manages focus with visual indicators

**Photino Implementation**:
- Posts "MountControl" message to web view
- Serializes tree to JSON
- Includes control options in message

### PatchAsync

**Purpose**: Applies a patch containing one or more operations to the mounted UI tree.

**Validation**:
- Throws `ArgumentNullException` if patch is null
- Throws `KeyNotFoundException` if target key missing
- Throws `InvalidOperationException` on structural conflicts
- Throws `ValidationException` if props invalid for kind
- Throws `PlatformNotReadyException` if Photino UI not initialized

**Side Effects**:
- Mutates live tree atomically
- Reflows only affected nodes
- Rollback on any error (atomic guarantee)

**Supported Operations**:
1. `ReplaceOp(key, node)` - Replace entire node
2. `UpdatePropsOp(key, props)` - Update node properties
3. `InsertChildOp(parentKey, index, node)` - Insert child
4. `RemoveOp(key)` - Remove node

**Terminal Implementation**:
- Applies patch via UiNodeTree
- Re-renders entire tree (optimization opportunity)

**Photino Implementation**:
- Applies patch via UiNodeTree
- Posts "PatchControl" message with serialized operations
- Web view applies incremental updates

### FocusAsync

**Purpose**: Moves input focus to the specified node.

**Validation**:
- Throws `ArgumentNullException` if key is null/empty
- Throws `KeyNotFoundException` if node doesn't exist
- Throws `InvalidOperationException` if node not focusable
- Throws `PlatformNotReadyException` if Photino UI not initialized

**Focusable Node Kinds**:
- Button
- CheckBox
- Toggle
- TextBox
- TextArea
- ListView

**Terminal Implementation**:
- Updates focus in tree
- Visual focus indicators on render

**Photino Implementation**:
- Updates focus in tree
- Posts "FocusControl" message to web view
- Web view updates DOM focus

## UiNodeTree Features

### Key Validation
- Validates all keys are unique in subtree
- Fast O(1) node lookup via dictionary
- Maintains parent-child relationships

### Atomic Patch Application
- All operations in a patch succeed or all fail
- Original state preserved on error
- No partial updates

### Focus Management
- Validates focusability based on node kind
- Clears focus when focused node removed
- Single focus per surface

### Tree Rebuild Strategy
- Uses immutable record types
- Rebuilds paths from root on changes
- Efficient map updates after changes

## Wire Protocol (Photino)

### Messages from .NET → Web

1. **MountControl**
```json
{
  "type": "MountControl",
  "tree": {
    "key": "root",
    "kind": "Column",
    "props": {},
    "children": []
  },
  "options": {
    "trapKeys": true,
    "initialFocusKey": "input"
  }
}
```

2. **PatchControl**
```json
{
  "type": "PatchControl",
  "patch": {
    "ops": [
      {
        "type": "updateProps",
        "key": "msg-1",
        "props": { "text": "Updated text" }
      }
    ]
  }
}
```

3. **FocusControl**
```json
{
  "type": "FocusControl",
  "key": "input-field"
}
```

### Messages from Web → .NET (Planned)

1. **ControlEvent** - User interaction events
```json
{
  "type": "ControlEvent",
  "key": "button-1",
  "name": "click",
  "value": null
}
```

2. **ControlHotkey** - Global keyboard events
```json
{
  "type": "ControlHotkey",
  "key": null,
  "hotkey": "Ctrl+S"
}
```

## Example Usage

### Basic Chat Surface
```csharp
var chatSurface = new UiNode(
    "chat-root",
    UiKind.Column,
    new Dictionary<string, object?>(),
    new[]
    {
        new UiNode("messages", UiKind.Column, new Dictionary<string, object?>(), Array.Empty<UiNode>()),
        new UiNode("input", UiKind.TextBox, new Dictionary<string, object?> { ["placeholder"] = "Type..." }, Array.Empty<UiNode>())
    }
);

await ui.SetRootAsync(chatSurface, new UiControlOptions(TrapKeys: true, InitialFocusKey: "input"));
```

### Streaming Updates
```csharp
// Append new message
var patch = new UiPatch(
    new InsertChildOp("messages", 0, new UiNode("msg-1", UiKind.Label, new Dictionary<string, object?> { ["text"] = "..." }, Array.Empty<UiNode>()))
);
await ui.PatchAsync(patch);

// Stream text updates
foreach (var chunk in streamingChunks)
{
    var updatePatch = new UiPatch(
        new UpdatePropsOp("msg-1", new Dictionary<string, object?> { ["text"] = chunk })
    );
    await ui.PatchAsync(updatePatch);
}
```

### Multiple Operations
```csharp
var multiPatch = new UiPatch(
    new UpdatePropsOp("status", new Dictionary<string, object?> { ["text"] = "Processing..." }),
    new UpdatePropsOp("progress", new Dictionary<string, object?> { ["value"] = 50 }),
    new InsertChildOp("log", 0, logEntry)
);
await ui.PatchAsync(multiPatch);
```

## Testing

Unit tests cover:
- ✓ Node creation and property access
- ✓ Patch creation and convenience methods
- ✓ Tree validation (unique keys)
- ✓ Duplicate key detection
- ✓ UpdateProps operation
- ✓ InsertChild operation
- ✓ Remove operation
- ✓ Focus management and validation
- ✓ Atomic patch application with rollback

Run tests:
```bash
cd unittests
dotnet test --filter "FullyQualifiedName~UiNodeTests"
```

## Error Handling

All errors follow the spec:

| Error Type | Thrown By | Reason |
|------------|-----------|--------|
| `ArgumentNullException` | SetRootAsync, PatchAsync, FocusAsync | Null parameter |
| `InvalidOperationException` | SetRootAsync | Duplicate keys in subtree |
| `InvalidOperationException` | PatchAsync | Structural conflict |
| `InvalidOperationException` | FocusAsync | Node not focusable |
| `KeyNotFoundException` | PatchAsync, FocusAsync | Target key not found |
| `ValidationException` | PatchAsync | Props invalid for kind |
| `PlatformNotReadyException` | Photino methods | UI not initialized |

## Performance Considerations

### Current Implementation
- Tree rebuild on every patch (immutable records)
- Full re-render in Terminal (can optimize later)
- Incremental updates in Photino (web view handles DOM diff)

### Future Optimizations
1. Partial tree rebuilds for isolated subtrees
2. Terminal: Track dirty regions, render only changes
3. Batch multiple patches into single render cycle
4. Cache serialized nodes for Photino

## Next Steps (Not in Scope)

Per the spec, these are future enhancements:

1. **Event Handling**
   - Implement event routing from web view
   - Handler registration via Props
   - Exception catching and telemetry

2. **Web View Implementation**
   - JavaScript renderer for Photino
   - DOM node mapping
   - Event capture and dispatch

3. **Surface Implementations**
   - ChatSurface builder
   - MailSurface builder
   - Refactor existing UiForm to use UiNode

4. **Documentation**
   - Developer guide for building surfaces
   - Focus rules and keyboard navigation
   - Migration notes for existing code

## Compliance with Spec

✓ All required data types implemented  
✓ All three IUi methods implemented  
✓ Error handling matches spec  
✓ Side effects match spec  
✓ Atomic patch application (invariant)  
✓ Unique key validation (invariant)  
✓ Focus singularity (invariant)  
✓ Unit tests for examples  
✓ Terminal implementation complete  
✓ Photino wire protocol implemented  

## Known Limitations

1. **Terminal Rendering**: Currently re-renders entire tree on patch (functional but not optimal)
2. **Event Handlers**: Props can contain handlers but dispatch not implemented
3. **Web View**: Requires JavaScript implementation (HTML/CSS/JS not included)
4. **Validation**: Basic prop validation; kind-specific rules can be extended
5. **Focus Visual**: Terminal shows focus with color; cursor positioning not implemented
