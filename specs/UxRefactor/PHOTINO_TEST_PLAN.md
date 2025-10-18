# Photino UiNode Integration - Test Plan

## Build Status
✅ **Build succeeded** with 44 warnings (all obsolete API usage in unrelated files)
- No compilation errors
- No breaking changes to the Photino integration

## Components to Test

### 1. ChatSurface
**Test scenarios:**
- [ ] Launch app and verify chat interface renders
- [ ] Type a message and press Enter - verify it appears as user bubble
- [ ] AI response should appear as assistant bubble
- [ ] Test markdown rendering in messages (bold, links, code blocks, tables)
- [ ] Test realtime streaming of AI responses (character-by-character)
- [ ] Scroll up/down through message history
- [ ] Verify message bubbles have correct colors (user: blue, assistant: gray, system: dashed)

**Expected behavior:**
- Messages render as bubbles aligned correctly
- Input composer at bottom with Send button
- Composer input expands for multi-line text
- Scroll works smoothly

### 2. FormOverlay
**Test scenarios:**
- [ ] Open a form (any command that uses `ShowFormAsync`)
- [ ] Verify modal overlay appears centered
- [ ] Fill in various field types:
  - [ ] String fields
  - [ ] Number fields
  - [ ] Boolean checkboxes
  - [ ] Enum dropdowns
  - [ ] Password fields
  - [ ] Text areas
- [ ] Test validation (leave required field empty, submit)
- [ ] Test form cancel (ESC or Cancel button)
- [ ] Test form submit (Enter or Submit button)

**Expected behavior:**
- Form appears as modal overlay with backdrop
- All fields render correctly
- Validation errors show in red
- Tab navigation works
- ESC cancels, Enter submits

### 3. MenuOverlay
**Test scenarios:**
- [ ] Open a menu (use RenderMenu or MenuOverlay.ShowAsync)
- [ ] Verify modal overlay appears centered
- [ ] Test filter box - type to filter choices
- [ ] Test keyboard navigation:
  - [ ] Up/Down arrows to move selection
  - [ ] PageUp/PageDown for page navigation
  - [ ] Home/End to jump to first/last
- [ ] Test Enter to select
- [ ] Test ESC to cancel
- [ ] Test mouse click to select

**Expected behavior:**
- Menu appears as modal overlay
- Filter-as-you-type works instantly
- Selected item highlighted
- Keyboard navigation smooth
- Selection confirmed or cancelled correctly

### 4. Progress
**Test scenarios:**
- [ ] Start a long-running operation with progress
- [ ] Verify progress bubble appears in message area
- [ ] Verify stats line shows (Running, Queued, Completed, Failed, Canceled)
- [ ] Verify ETA hint appears if available
- [ ] Verify progress bars update smoothly
- [ ] Verify only active items shown (not completed/canceled)
- [ ] Test Cancel button (or ESC key)
- [ ] Verify progress bubble disappears when complete

**Expected behavior:**
- Progress renders as tool bubble in messages
- Updates every 100-200ms smoothly
- Cancel button works
- Removed automatically on completion
- Shows artifact markdown when done

### 5. RealtimeWriter
**Test scenarios:**
- [ ] Use `BeginRealtime()` to start streaming output
- [ ] Write multiple lines of text
- [ ] Verify content appears incrementally
- [ ] Dispose the writer
- [ ] Verify ephemeral bubble is removed

**Expected behavior:**
- Content appears as it's written
- Scrolls to show new content
- Bubble removed on disposal
- No memory leaks (node properly cleaned up)

### 6. Table Rendering
**Test scenarios:**
- [ ] Render a table with `RenderTable()`
- [ ] Verify table appears as markdown in tool bubble
- [ ] Verify headers show
- [ ] Verify rows show with proper cell wrapping
- [ ] Test very long URLs in cells (should wrap)
- [ ] Test wide tables (should scroll horizontally if needed)

**Expected behavior:**
- Table renders as markdown
- Sticky headers work
- Cells wrap properly
- Zebra striping for readability

### 7. Report Rendering
**Test scenarios:**
- [ ] Render a report with `RenderReport()`
- [ ] Verify report appears as markdown tool bubble
- [ ] Verify sections and formatting correct

**Expected behavior:**
- Report renders as markdown
- Formatting preserved
- Scrollable if long

### 8. Input Routing
**Test scenarios:**
- [ ] Type in composer input field
- [ ] Verify ControlEvent sent with each character
- [ ] Press Enter - verify submit event
- [ ] Click Send button - verify submit event
- [ ] Press ESC - verify escape event
- [ ] Test Tab to focus Send button
- [ ] Test Shift+Enter for newline in input

**Expected behavior:**
- All key events routed to C#
- Input state tracked correctly
- Submit events trigger properly
- Focus management works

### 9. Event Handlers
**Test scenarios:**
- [ ] Create a button with OnClick handler
- [ ] Click button - verify handler invoked
- [ ] Create a checkbox with OnToggle handler
- [ ] Toggle checkbox - verify handler invoked
- [ ] Create a TextBox with OnChange handler
- [ ] Type in textbox - verify handler invoked
- [ ] Create a ListView with OnItemActivated handler
- [ ] Click list item - verify handler invoked

**Expected behavior:**
- All event handlers fire correctly
- Value passed correctly
- Async handlers work
- No race conditions

### 10. Patches
**Test scenarios:**
- [ ] Test Replace patch - entire node replaced
- [ ] Test UpdateProps patch - props updated incrementally
- [ ] Test InsertChild patch - child added at correct position
- [ ] Test Remove patch - node removed from tree
- [ ] Test batch patches - multiple ops in one patch

**Expected behavior:**
- All patch types work correctly
- No flickering during updates
- Node map stays consistent
- Auto-scroll works after insert

### 11. Styles
**Test scenarios:**
- [ ] Create a Label with ForegroundColor style
- [ ] Verify color applied correctly
- [ ] Create a Label with Bold style
- [ ] Verify bold applied
- [ ] Create a Label with Align=center style
- [ ] Verify centered
- [ ] Create a Label with Wrap=false style
- [ ] Verify text truncated with ellipsis

**Expected behavior:**
- All styles render correctly
- Colors match ConsoleColor enum
- Layout respected

### 12. Layout
**Test scenarios:**
- [ ] Create a Column with multiple children
- [ ] Verify vertical stacking
- [ ] Create a Row with multiple children
- [ ] Verify horizontal layout
- [ ] Test nested layout (Column in Row, etc.)
- [ ] Test Layout="dock-bottom" for chat surface
- [ ] Verify messages panel scrollable

**Expected behavior:**
- Flexbox layout works correctly
- Gaps between children consistent
- Nested layouts work
- Scrolling works where needed

## Regression Testing

### Previous Functionality
- [ ] Chat commands still work (`/help`, `/clear`, etc.)
- [ ] Provider switching works
- [ ] Context management works
- [ ] Tool calling works
- [ ] RAG queries work
- [ ] File operations work

### Performance
- [ ] No lag when typing in composer
- [ ] Smooth scrolling in messages
- [ ] Fast patch application
- [ ] No memory leaks (long-running sessions)

### Error Handling
- [ ] Malformed JSON from C# handled gracefully
- [ ] Missing node keys handled gracefully
- [ ] Invalid patch operations logged but don't crash
- [ ] Network errors handled (if applicable)

## Known Issues
1. ⚠️ `_tcsForm` field never assigned (warning CS0649) - needs investigation for form result handling
2. ⚠️ Obsolete API usage in Terminal.cs and MailCommands.cs - can be fixed later

## Test Results Summary
| Component       | Status | Notes |
|----------------|--------|-------|
| ChatSurface    | ⏳     |       |
| FormOverlay    | ⏳     |       |
| MenuOverlay    | ⏳     |       |
| Progress       | ⏳     |       |
| RealtimeWriter | ⏳     |       |
| Table          | ⏳     |       |
| Report         | ⏳     |       |
| Input Routing  | ⏳     |       |
| Event Handlers | ⏳     |       |
| Patches        | ⏳     |       |
| Styles         | ⏳     |       |
| Layout         | ⏳     |       |

Legend:
- ⏳ Not tested yet
- ✅ Passed
- ⚠️ Passed with minor issues
- ❌ Failed

## Manual Testing Steps
1. Run the app: `dotnet run`
2. Verify Photino window opens
3. Test chat interaction
4. Test various commands that use forms/menus
5. Test progress with long-running operations
6. Monitor console for errors
7. Check for memory leaks over extended usage

## Automated Testing
- [ ] Add unit tests for SerializeNode
- [ ] Add unit tests for renderNode (via jsdom)
- [ ] Add integration tests for patch operations
- [ ] Add end-to-end tests for common workflows
