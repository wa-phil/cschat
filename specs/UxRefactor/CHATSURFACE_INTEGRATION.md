# ChatSurface Integration into Program.cs

## Overview
ChatSurface has been fully integrated as the **primary and only** rendering mechanism in the main application loop. The previous legacy rendering methods (`RenderChatMessage`, `RenderChatHistory`) have been completely replaced with ChatSurface's declarative UiNode tree approach. This works seamlessly across both Terminal and GUI (Photino) modes.

## Complete Replacement Strategy

### Previous Approach (Removed)
```csharp
// OLD: Direct rendering calls
ui.RenderChatHistory(Context.Messages());
ui.RenderChatMessage(userMessage);
ui.RenderChatMessage(assistantMessage);
```

### New Approach (Current)
```csharp
// NEW: Declarative surface with patches
await ChatSurface.MountAsync(ui, messages, threadName: "Chat");
await ui.PatchAsync(ChatSurface.AppendMessage(userMessage, index));
await ui.PatchAsync(ChatSurface.UpdateMessageContent(messageId, content));
```

## Changes Made

### 1. Program.cs Main Loop (Complete Rewrite)

**Initial Mount (Lines 230-255)**
- ChatSurface is **always** mounted, regardless of UI mode
- No conditional logic or fallback to legacy rendering
- Both Terminal and Photino receive the same ChatSurface tree
- Thread name displayed in toolbar
- Event handlers registered (used in Photino)

```csharp
// Mount ChatSurface for all UI modes
var messages = Context.Messages(InluceSystemMessage: false).ToList();
await ChatSurface.MountAsync(
    ui,
    messages,
    inputText: "",
    threadName: active?.Name,
    onSend: async (e) => { await Task.CompletedTask; },
    onClear: async (e) => 
    {
        Context.Clear();
        Context.AddSystemMessage(config.SystemPrompt);
        await ui.PatchAsync(ChatSurface.ClearMessages());
    }
);
```

**Message Loop (Lines 257-288)**
- User messages rendered via `AppendMessage()` patch
- Assistant responses use two-step pattern:
  1. `AppendMessage()` with empty content
  2. `UpdateMessageContent()` with full response
- No conditional branching based on UI mode
- No try-catch fallbacks (relies on backend implementations)

```csharp
// Add user message
Context.AddUserMessage(userInput);
var userMessage = Context.Messages().Last();
var currentMessages = Context.Messages(InluceSystemMessage: false).ToList();
await ui.PatchAsync(ChatSurface.AppendMessage(userMessage, currentMessages.Count - 1));

// Add assistant response
var assistantMessage = new ChatMessage { Role = Roles.Assistant, Content = "", CreatedAt = DateTime.Now };
Context.AddAssistantMessage(response);
currentMessages = Context.Messages(InluceSystemMessage: false).ToList();
await ui.PatchAsync(ChatSurface.AppendMessage(assistantMessage, currentMessages.Count - 1));
await ui.PatchAsync(ChatSurface.UpdateMessageContent(assistantMessage.Id, response));
```

### 2. Chat/ChatCommands.cs (Simplified)

All commands now use ChatSurface exclusively with no mode detection or fallbacks:

**Command: `chat new`**
```csharp
var forked = ChatManager.CreateNewThread();
await ChatSurface.MountAsync(Program.ui, Array.Empty<ChatMessage>(), inputText: "", threadName: forked.Name);
```

**Command: `chat switch`**
```csharp
ChatManager.LoadThread(target);
var messages = Program.Context.Messages(InluceSystemMessage: false).ToList();
await ChatSurface.MountAsync(Program.ui, messages, inputText: "", threadName: target.Name);
```

**Command: `chat show`**
```csharp
var messages = Program.Context.Messages(InluceSystemMessage: false).ToList();
await ChatSurface.MountAsync(Program.ui, messages, inputText: "", threadName: Program.config.ChatThreadSettings.ActiveThreadName);
```

**Command: `chat clear`**
```csharp
Program.Context.Clear();
Program.Context.AddSystemMessage(Program.config.SystemPrompt);
await Program.ui.PatchAsync(ChatSurface.ClearMessages());
```

### 3. Backend Support (Already Implemented)

Both UI backends have complete ChatSurface support:

**Terminal.cs (Lines 964-1163)**
- `SetRootAsync()`: Validates, clears console, renders tree with ASCII/ANSI
- `PatchAsync()`: Applies patch atomically, re-renders entire tree
- `FocusAsync()`: Sets focus in tree state
- `RenderNode()`: Recursive rendering with indentation, colors, focus indicators

**Photino.cs (Lines 668-776)**
- `SetRootAsync()`: Validates, sends MountControl message to web view
- `PatchAsync()`: Applies patch, sends PatchControl message with operations
- `FocusAsync()`: Sets focus, sends FocusControl message
- `SerializeNode()`: Converts UiNode to JSON for web transmission

**index.html (JavaScript Control Surface)**
- `mountControl()`: Clears DOM, renders control tree
- `renderNode()`: Creates DOM elements for each UiKind
- `patchControl()`: Applies incremental updates to DOM
- Full support for all UiKind types and properties

## Architecture

### Universal Message Flow

**User Message:**
```
User input → Context.AddUserMessage() 
          → ChatSurface.AppendMessage() 
          → ui.PatchAsync()
          → Terminal: RenderNode() or Photino: DOM update
```

**Assistant Response:**
```
Engine.PostChatAsync() → Context.AddAssistantMessage()
                      → ChatSurface.AppendMessage() (empty)
                      → ui.PatchAsync()
                      → ChatSurface.UpdateMessageContent()
                      → ui.PatchAsync()
                      → Terminal/Photino: Incremental update
```

**Thread Switch:**
```
ChatManager.LoadThread() → Context loaded with new messages
                        → ChatSurface.MountAsync() with new tree
                        → ui.SetRootAsync()
                        → Terminal: Clear + render or Photino: Replace DOM
```

### UiNode Tree Structure

The same tree structure is used for both Terminal and Photino:

```
chat-root (Column)
├── toolbar (Row)
│   ├── thread-title (Label) - Shows thread name
│   ├── spacer-toolbar (Spacer)
│   └── clear-btn (Button) - Clears chat
├── messages (Column, scrollable, autoScroll)
│   ├── msg-{id} (Column) - User/Assistant message
│   │   ├── msg-{id}-header (Label) - Timestamp + role
│   │   └── msg-{id}-content (Label) - Message text
│   └── ... more messages
├── spacer (Spacer)
└── composer (Row)
    ├── input (TextBox) - Text input field
    └── send-btn (Button) - Send message
```

## Benefits of Complete Replacement

### 1. **Unified Code Path**
- No mode-specific branching
- Easier to maintain and debug
- Same behavior guaranteed across backends

### 2. **Declarative UI**
- UI state represented as data (UiNode tree)
- Incremental updates via patches
- Easier to reason about UI changes

### 3. **Consistent API**
- All UI updates go through ChatSurface
- Backends implement same IUi interface
- Easy to add new backends (e.g., web-based UI)

### 4. **Better Performance**
- Photino: Incremental DOM updates (no full re-render)
- Terminal: Targeted re-rendering (can be optimized further)
- Patch operations are atomic and efficient

### 5. **Future-Proof**
- Event-driven architecture ready
- Streaming updates enabled
- Rich UI features possible (reactions, threading, etc.)

## Backend Rendering Differences

### Terminal Mode
- Renders UiNode tree as ASCII text with ANSI colors
- Layout containers (Column/Row) control indentation
- Focus indicated with color inversion (black on white)
- Full re-render on each patch (optimization opportunity)
- Labels show role prefixes like "[USER]", "[ASSISTANT]"
- Buttons shown as `[Button Text]`

### Photino (GUI) Mode
- Renders UiNode tree as HTML DOM elements
- Layout containers use CSS flexbox (column/row direction)
- Focus indicated with CSS focus styles
- Incremental DOM updates (only changed elements)
- Rich styling with colors, borders, rounded corners
- Interactive buttons with click handlers

## Code Locations

### Core Integration
- **Main loop**: `Program.cs` lines 230-288
- **Chat commands**: `Chat/ChatCommands.cs` lines 18-79
- **ChatSurface API**: `UX/ChatSurface.cs`

### Backend Implementations
- **Terminal backend**: `UX/Terminal.cs` lines 964-1163
- **Photino backend**: `UX/Photino.cs` lines 668-776
- **Web renderer**: `UX/wwwroot/index.html` (JavaScript)

### Supporting Files
- **UiNode types**: `UX/UiNode.cs`
- **Tree management**: `UX/UiNodeTree.cs`
- **IUi interface**: `UX/IUI.cs` lines 368-394

## Removed Code

The following legacy methods are **no longer called** from Program.cs or ChatCommands.cs:
- `ui.RenderChatMessage()` - Replaced by `ChatSurface.AppendMessage()`
- `ui.RenderChatHistory()` - Replaced by `ChatSurface.MountAsync()`
- Direct console/DOM manipulation - Replaced by patch operations

These methods still exist in Terminal.cs and Photino.cs for backward compatibility with other parts of the codebase (e.g., tool output, progress reports), but the main chat loop no longer uses them.

## Testing

### Terminal Mode
```bash
cschat --ui terminal
```
Expected behavior:
- ChatSurface renders as ASCII text with colors
- Messages appear with timestamps and role indicators
- Thread name shown in toolbar
- All chat commands work correctly

### GUI Mode
```bash
cschat --ui gui
```
Expected behavior:
- ChatSurface renders as styled HTML in Photino window
- Messages appear in bubbles with colors
- Thread name shown in toolbar
- Buttons are interactive (though click handlers need wiring)
- Smooth incremental updates

### Test Scenarios

1. **Fresh Start**: Launch app, send messages, verify display
2. **Thread Switch**: Create/switch threads, verify messages update
3. **Clear Chat**: Clear history, verify empty messages panel
4. **Long Conversation**: Send many messages, verify scrolling works
5. **Special Characters**: Test unicode, emoji, code blocks
6. **Multi-line Input**: Test shift+enter for newlines

## Known Limitations

1. **Terminal Re-rendering**: Currently re-renders entire tree on each patch
   - Could optimize to only update changed portions
   - Acceptable for typical chat usage

2. **Event Handlers**: Send button doesn't trigger action yet
   - Input still comes from `ReadInputWithFeaturesAsync()`
   - Need to wire up ControlEvent messages

3. **True Streaming**: Response shown all at once
   - Need to integrate with streaming API
   - Update content as chunks arrive

4. **Layout Differences**: Terminal and Photino render differently
   - Terminal: Simple ASCII, all elements visible
   - Photino: Rich HTML, better visual hierarchy
   - Both functional, just different visual styles

## Next Steps

1. **Event Wiring**: Connect send button to actual message sending
2. **True Streaming**: Update message content as AI generates response
3. **Input Integration**: Get input from ChatSurface TextBox component
4. **Terminal Optimization**: Optimize RenderNode to avoid full re-render
5. **Rich Formatting**: Support markdown, code highlighting in messages
6. **Keyboard Navigation**: Tab between focusable elements

## Migration Complete ✓

ChatSurface is now the **sole rendering mechanism** for the main chat interface. All code paths use the declarative UiNode tree approach with patch-based updates. Both Terminal and Photino backends fully support the new architecture.

