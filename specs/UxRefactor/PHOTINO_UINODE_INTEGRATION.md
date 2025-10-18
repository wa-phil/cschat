# Photino UiNode Integration - Refactoring Summary

## Overview
This refactoring completely rewrites `Photino.cs` and `index.html` to work seamlessly with the UiNode-based declarative UI architecture. The goal was to remove legacy rendering code and ensure all UI components (RealtimeWriter, FormOverlay, MenuOverlay, ChatSurface, Progress) work through the unified UiNode system.

## Key Changes

### 1. Photino.cs Updates

#### SerializeNode Enhancement
**Before:** Simple serialization that passed props dictionary directly
**After:** Properly serializes UiNode with enum-to-string conversion for both props and styles
- Converts `UiProperty` enum keys to strings for JSON transmission
- Extracts and serializes `UiStyles` separately as a dictionary
- Maintains proper child tree serialization

```csharp
private object SerializeNode(UiNode node)
{
    // Serialize props as dictionary with string keys (property names)
    var propsDict = new Dictionary<string, object?>();
    foreach (var kvp in node.Props)
    {
        var propName = kvp.Key.ToString(); // Convert enum to string
        propsDict[propName] = kvp.Value;
    }

    // Serialize styles if present
    var stylesDict = new Dictionary<string, object?>();
    if (node.Styles != null && node.Styles != UiStyles.Empty)
    {
        foreach (var kvp in node.Styles.Values)
        {
            var styleName = kvp.Key.ToString();
            stylesDict[styleName] = kvp.Value;
        }
    }

    return new
    {
        key = node.Key,
        kind = node.Kind.ToString(),
        props = propsDict,
        styles = stylesDict.Count > 0 ? stylesDict : null,
        children = childList
    };
}
```

#### Legacy Rendering Removal
- **RenderChatMessage**: Now uses `ChatSurface.AppendMessage()` via patches
- **RenderChatHistory**: Now uses `ChatSurface.UpdateMessages()` via patches
- **RenderMenu**: Now uses `MenuOverlay.ShowAsync()` for UiNode-based menus
- **RenderTable/RenderReport**: Already properly create markdown tool messages

#### HandleInbound Improvements
- Already properly routes `ControlEvent` messages to `PhotinoInputRouter`
- Correctly invokes UiNode event handlers (`OnClick`, `OnChange`, `OnEnter`, etc.)
- Handles `OpenExternal`, `CancelProgress`, `FormResult`, etc.

### 2. index.html Complete Rewrite

#### UiNode Rendering System
**New `renderNode()` function** handles all UiKind types:

**Layout Containers:**
- `Column`: Vertical flexbox with gap
- `Row`: Horizontal flexbox with gap, center-aligned

**Interactive Controls:**
- `TextBox`: Input field with change/enter events
- `TextArea`: Multi-line input with change events
- `Button`: Clickable button with OnClick event
- `CheckBox/Toggle`: Checkbox with toggle event
- `ListView`: Scrollable list with item selection and activation

**Content Display:**
- `Label`: Text display with styling support
- `Html`: Markdown or HTML rendering via marked.js
- `Accordion`: Collapsible details/summary element
- `Spacer`: Empty vertical space
- `Progress`: Full progress visualization with stats, items, ETA, and cancel button

#### Event Routing
All user interactions post `ControlEvent` messages:
```javascript
post({ type: 'ControlEvent', key: node.key, name: eventName, value: value });
```

Events include:
- `click`: Button clicks
- `change`: Input/textarea value changes
- `enter`: Enter key in text inputs
- `toggle`: Checkbox state changes
- `itemActivated`: ListView item selection

#### Patch Operations
Complete implementation of all patch operation types:

**1. Replace:** Full node replacement
```javascript
function replaceNode(key, newNode) {
    const oldElement = controlNodes.get(key);
    const newElement = renderNode(newNode);
    oldElement.parentNode.replaceChild(newElement, oldElement);
}
```

**2. UpdateProps:** Incremental property updates
- Handles `Text`, `Enabled`, `Checked`, `Placeholder`
- Special handling for `Items` (ListView rebuild)
- Special handling for Progress props (re-render)

**3. InsertChild:** Add child at specific index
- Supports both direct children and container children (`_contentContainer`)
- Auto-scrolls parent if `autoScroll` is enabled

**4. Remove:** Delete node from tree
```javascript
function removeNode(key) {
    const element = controlNodes.get(key);
    element.parentNode.removeChild(element);
    controlNodes.delete(key);
}
```

#### Style Support
New `applyTextStyles()` and `applyLayoutProps()` helper functions:
- `ForegroundColor/BackgroundColor`: Console color mapping
- `Bold`: Font weight
- `Align`: Text alignment
- `Wrap`: Word wrapping control
- `Padding/Width/Height`: Layout dimensions

#### Progress Rendering
Comprehensive `renderProgressNode()` function:
- Displays title, stats, and ETA
- Shows only active items (Running, Queued, Failed)
- Renders progress bars with color coding
- Shows steps or notes per item
- Cancel button for cancellable progress

### 3. Architecture Benefits

#### Unified Rendering Path
**Before:** Multiple legacy rendering methods (ConsoleWrite, AddBubble, legacy forms/menus)
**After:** Single UiNode path with patches

```
Application → UiNode Tree → SerializeNode → JSON → renderNode → DOM
                    ↓
              PatchAsync → JSON → patchControl → DOM updates
```

#### Declarative UI
- **ChatSurface**: Builds chat UI as UiNode tree
- **FormOverlay**: Generates form UiNode overlay
- **MenuOverlay**: Creates menu UiNode overlay
- **Progress**: Rendered as ephemeral UiNode bubbles
- **RealtimeWriter**: Uses UiNode patches to insert/update content

#### Input Routing
Unified input flow via `PhotinoInputRouter`:
1. User interaction in browser → `ControlEvent` → C#
2. C# invokes UiNode event handlers
3. Handlers can patch UI or trigger business logic
4. Keys enqueued for `TryReadKey()` consumers

### 4. Component Integration

#### ChatSurface
- Creates chat UI with messages panel and composer
- Generates patches for message append/update
- Handles realtime streaming message updates
- Integrates with input router for submit handling

#### FormOverlay
- Builds modal form overlay from `UiForm` model
- Supports all field types (string, number, bool, enum, array, path)
- Validation and error display
- OK/Cancel buttons with proper event handling

#### MenuOverlay
- Builds modal menu overlay with filter box and list
- Filter-as-you-type support
- Keyboard navigation (Up/Down/PageUp/PageDown/Home/End)
- Enter to select, ESC to cancel

#### Progress
- Renders as ephemeral UiNode in messages panel
- Updates via patches as progress changes
- Shows active work items with progress bars
- Cancel button sends `CancelProgress` event
- Auto-removed when complete

#### RealtimeWriter
- Inserts ephemeral UiNode with unique key
- Updates content via `UpdateProps` patches
- Removes node on Dispose()
- Scrollable within messages panel

## Testing Checklist

✅ **UiNode Rendering**: All UiKind types render correctly
✅ **Event Routing**: Click, change, enter, toggle events work
✅ **Patches**: Replace, UpdateProps, InsertChild, Remove all functional
✅ **ChatSurface**: Messages display, composer works, realtime updates
✅ **FormOverlay**: Forms display, validation works, submit/cancel
✅ **MenuOverlay**: Menus display, filtering works, selection
✅ **Progress**: Progress displays, updates, cancels correctly
✅ **RealtimeWriter**: Content streams and updates correctly
✅ **Styles**: Colors, bold, alignment all apply correctly

## Migration Notes

### For Developers
- **Do not** use legacy `ConsoleWrite`/`ConsoleWriteLine` - use `IRealtimeWriter`
- **Do not** create manual DOM in JavaScript - use UiNode rendering
- **Always** use patches for UI updates, never direct DOM manipulation
- **Use** `ChatSurface` for chat UI instead of manual message rendering
- **Use** `FormOverlay`/`MenuOverlay` instead of legacy form/menu helpers

### Breaking Changes
- Old `index.html` backed up to `index_old.html`
- Legacy bubble rendering removed (now handled by UiNode)
- Legacy form/menu JavaScript removed (now UiNode-based)

## Future Enhancements
1. Add animation support to patches (fade in/out, slide, etc.)
2. Add more UiKind types (Tabs, SplitPanel, Tree, etc.)
3. Add theme support (dark/light mode)
4. Add accessibility improvements (ARIA labels, keyboard shortcuts)
5. Add virtualization for large ListView items
6. Add drag-and-drop support for reordering

## Summary
This refactoring successfully unifies the Photino UI around the UiNode architecture, removing all legacy rendering code and ensuring a consistent, declarative approach to UI construction. All major components (chat, forms, menus, progress, realtime) now work through the same UiNode/UiPatch pipeline, making the codebase more maintainable and extensible.
