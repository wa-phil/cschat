using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Kinds of UI nodes supported in the declarative control layer
/// </summary>
public enum UiKind
{
    Column,
    Row,
    Accordion,
    Label,
    Button,
    CheckBox,
    Toggle,
    TextBox,
    TextArea,
    ListView,
    Html,
    Spacer
}

/// <summary>
/// Declarative UI node for retained-mode control layer
/// </summary>
public sealed record UiNode(
    string Key,
    UiKind Kind,
    IReadOnlyDictionary<string, object?> Props,
    IReadOnlyList<UiNode> Children
);

/// <summary>
/// Event emitted from a UI node
/// </summary>
public sealed record UiEvent(
    string Key,
    string Name,
    string? Value,
    object? Tag
);

/// <summary>
/// Options for mounting a control surface
/// </summary>
public sealed record UiControlOptions(
    bool TrapKeys = true,
    string? InitialFocusKey = null
);

/// <summary>
/// Handler delegate for UI events
/// </summary>
public delegate Task UiHandler(UiEvent e);

/// <summary>
/// Base class for patch operations
/// </summary>
public abstract record UiOp;

/// <summary>
/// Replace an entire node in the tree
/// </summary>
public sealed record ReplaceOp(string Key, UiNode Node) : UiOp;

/// <summary>
/// Update properties of an existing node
/// </summary>
public sealed record UpdatePropsOp(string Key, IReadOnlyDictionary<string, object?> Props) : UiOp;

/// <summary>
/// Insert a child node at a specific index
/// </summary>
public sealed record InsertChildOp(string ParentKey, int Index, UiNode Node) : UiOp;

/// <summary>
/// Remove a node from the tree
/// </summary>
public sealed record RemoveOp(string Key) : UiOp;

/// <summary>
/// Collection of operations to apply to the UI tree
/// </summary>
public sealed record UiPatch(params UiOp[] Ops)
{
    /// <summary>
    /// Convenience method to create a patch that replaces a single node
    /// </summary>
    public static UiPatch Replace(string key, UiNode n) => new(new ReplaceOp(key, n));
}
