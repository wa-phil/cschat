# Whole-Window Controls & Surfaces (UiNode/UiPatch) – Spec #
## Problem & Intent ##

Today, Chat and Mail UIs are hand-rolled inside Photino.cs/Terminal.cs and MailCommands.cs, making it hard to create, swap, and iterate on rich, full-window experiences. We intend to introduce a declarative, retained-mode control layer (UiNode/UiPatch) that mounts a single “surface” (e.g., Chat, Mail, or a modal form) across UI backends, with a small diff/patch protocol, event routing, and predictable focus/keyboard ownership.

## Interfaces & Data Shapes ##

* Functions (new/extended in IUi):
  * Task SetRootAsync(UiNode root, UiControlOptions? options = null)
    * Errors: ArgumentNullException (root), InvalidOperationException (duplicate keys in subtree), PlatformNotReadyException (UI not initialized).
    * Side effects: Clears previous surface, renders root, optionally traps global keys, sets initial focus.
  * Task PatchAsync(UiPatch patch)
    * Errors: KeyNotFoundException (target key missing), InvalidOperationException (structural conflict: parent/child mismatch), ValidationException (props invalid for kind).
    * Side effects: Mutates live tree and reflows only affected nodes.
  * Task FocusAsync(string key)
    * Errors: KeyNotFoundException (no such node), InvalidOperationException (node not focusable).
    * Side effects: Moves input focus to node, updates keyboard routing.
* Functions (event model):
  * delegate Task UiHandler(UiEvent e);
  * Event dispatch originates in the backend (Photino/Terminal) and invokes handlers registered on a node’s Props (e.g., onClick, onChange, onEnter, onItemActivated).
  * Errors: Handler exceptions are caught, logged, and surfaced as UiEventError telemetry; UI remains responsive.
* Core data types (C#):
```
public enum UiKind { Column, Row, Accordion, Label, Button, CheckBox, Toggle, TextBox, TextArea, ListView, Html, Spacer }

public sealed record UiNode(
    string Key,
    UiKind Kind,
    IReadOnlyDictionary<string, object?> Props,
    IReadOnlyList<UiNode> Children
);

public sealed record UiEvent(string Key, string Name, string? Value, object? Tag);

public sealed record UiControlOptions(bool TrapKeys = true, string? InitialFocusKey = null);

public abstract record UiOp;
public sealed record ReplaceOp(string Key, UiNode Node) : UiOp;
public sealed record UpdatePropsOp(string Key, IReadOnlyDictionary<string, object?> Props) : UiOp;
public sealed record InsertChildOp(string ParentKey, int Index, UiNode Node) : UiOp;
public sealed record RemoveOp(string Key) : UiOp;

public sealed record UiPatch(params UiOp[] Ops)
{
    public static UiPatch Replace(string key, UiNode n) => new(new ReplaceOp(key, n));
}
```
* JSON Schemas (wire protocol for Photino postMessage)
  * UiNode:
```
{
  "title": "UiNode",
  "type": "object",
  "required": ["key", "kind", "props", "children"],
  "properties": {
    "key": { "type": "string", "minLength": 1 },
    "kind": { "type": "string", "enum": ["Column","Row","Accordion","Label","Button","CheckBox","Toggle","TextBox","TextArea","ListView","Html","Spacer"] },
    "props": { "type": "object", "additionalProperties": true },
    "children": { "type": "array", "items": { "$ref": "#" } }
  }
}
```

  * UiPatch:
```
{
  "title": "UiPatch",
  "type": "object",
  "required": ["ops"],
  "properties": {
    "ops": {
      "type": "array",
      "items": {
        "oneOf": [
          { "type": "object", "required": ["type","key","node"], "properties": { "type": { "const": "replace" }, "key": { "type": "string" }, "node": { "$ref": "UiNode" } } },
          { "type": "object", "required": ["type","key","props"], "properties": { "type": { "const": "updateProps" }, "key": { "type": "string" }, "props": { "type": "object", "additionalProperties": true } } },
          { "type": "object", "required": ["type","parentKey","index","node"], "properties": { "type": { "const": "insertChild" }, "parentKey": { "type": "string" }, "index": { "type": "integer", "minimum": 0 }, "node": { "$ref": "UiNode" } } },
          { "type": "object", "required": ["type","key"], "properties": { "type": { "const": "remove" }, "key": { "type": "string" } } }
        ]
      }
    }
  }
}
```

  * Photino bridge messages (from .NET → Web):
    * MountControl { tree: UiNode, options?: UiControlOptions }
    * PatchControl { patch: UiPatch }
    * FocusControl { key: string }
  * Photino bridge messages (from Web → .NET):
    * ControlEvent { key: string, name: string, value?: string }
    * ControlHotkey { key?: string, hotkey: string }

## Examples (Given / When / Then) ##
1. Mount Chat Surface
    * Given no surface is mounted,
    * When SetRootAsync(ChatSurface(vm), new UiControlOptions(TrapKeys:true, InitialFocusKey:"input")) is called,
    * Then the window shows toolbar, messages list, and composer; the input textbox has focus and global keys are trapped to the control.
2. Append Assistant Message by Patch
    * Given the Chat surface is mounted and a user message exists,
    * When PatchAsync(new UiPatch(new InsertChildOp("messages", 3, AssistantBubble("msg-42","…")))),
    * Then the messages list now shows the assistant bubble at index 3 and scrolls to it (Photino), or re-renders showing it (Terminal).
3. Update Streaming Text
    * Given message msg-42-content exists with text "Hel" ,
    * When PatchAsync(new UiPatch(new UpdatePropsOp("msg-42-content", new { text = "Hello, wor" }))) is applied repeatedly,
    * Then the bubble’s visible text updates in place without re-creating the whole list.
4. Edge case: Duplicate Keys in Subtree
    * Given a UiNode subtree contains two nodes with key "composer",
    * When SetRootAsync is called,
    * Then it fails fast with InvalidOperationException("Duplicate keys in surface"), no partial UI is mounted, and an error toast/log is emitted.
5. Failure: Patching Missing Node
    * Given surface root is mounted,
    * When PatchAsync(new UiPatch(new UpdatePropsOp("unknown-key", new { text="x" }))),
    * Then KeyNotFoundException is thrown, a UiPatchFailed telemetry event is logged, and the UI remains unchanged.
6. Swap to Mail Surface
    * Given Chat is mounted,
    * When user runs command use mail and code calls SetRootAsync(MailSurface(mailVm)),
    * Then the entire window shows folders (left), threads (right/top), and read pane (right/bottom); keyboard arrows navigate ListViews and Enter activates a thread.

## Invariants & Non-Functionals ##
 * Invariants
    * Keys are unique within the mounted tree.
    * Patch operations are atomic: either all ops apply or none (error leaves tree unchanged).
    * Event handlers are sandboxed: exception in one handler does not crash the UI thread; UI remains interactive.
    * Focus is singular: at most one focusable node is active at a time per surface.
* Backcompat
    * Existing IUi methods (menus, progress, bubbles, ShowFormAsync) remain functional.
    * UiForm may be refactored later to render via UiNode; initial release keeps legacy overlay implementation side-by-side.

Done
* [] Tests cover examples & invariants
    * [] Mount/patch/focus unit tests (tree model).
    * [] Photino integration tests (DOM created, event round-trip).
    * [] Terminal snapshot tests (ASCII layout, focus & key routing).
* []  Docs/changelog updated
    * [] Developer guide: building a surface, wiring handlers, focus rules.
    * [] Migration notes: creating ChatSurface/MailSurface; optional UiForm bridging.