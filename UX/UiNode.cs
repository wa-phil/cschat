using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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

/// <summary>
/// Manages a retained-mode UI tree with validation and patch operations
/// </summary>
public sealed class UiNodeTree
{
    private UiNode? _root;
    private readonly Dictionary<string, UiNode> _nodeMap = new();
    private readonly Dictionary<string, string> _parentMap = new(); // child key -> parent key
    private string? _focusedKey;

    public UiNode? Root => _root;
    public string? FocusedKey => _focusedKey;
    public bool IsEmpty => _root == null;

    /// <summary>
    /// Validates that all keys in the subtree are unique
    /// </summary>
    private void ValidateUniqueKeys(UiNode node, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(node.Key))
            throw new InvalidOperationException("Node keys cannot be null or empty");

        if (!seen.Add(node.Key))
            throw new InvalidOperationException($"Duplicate keys in surface: '{node.Key}' appears more than once");

        foreach (var child in node.Children)
        {
            ValidateUniqueKeys(child, seen);
        }
    }

    /// <summary>
    /// Builds the internal node map and parent map for fast lookups
    /// </summary>
    private void BuildMaps(UiNode node, string? parentKey = null)
    {
        _nodeMap[node.Key] = node;
        if (parentKey != null)
        {
            _parentMap[node.Key] = parentKey;
        }

        foreach (var child in node.Children)
        {
            BuildMaps(child, node.Key);
        }
    }

    /// <summary>
    /// Validates that props are appropriate for the node kind
    /// </summary>
    private void ValidateProps(UiKind kind, IReadOnlyDictionary<string, object?> props)
    {
        // Basic validation - can be extended based on requirements
        if (props == null)
            throw new ValidationException($"Props cannot be null for {kind}");

        // Add kind-specific validation as needed
        switch (kind)
        {
            case UiKind.TextBox:
            case UiKind.TextArea:
                // Could validate text, placeholder, maxLength, etc.
                break;
            case UiKind.ListView:
                // Could validate items, selectedIndex, etc.
                break;
            // Add more validations as needed
        }
    }

    /// <summary>
    /// Sets a new root node, replacing any existing tree
    /// </summary>
    public void SetRoot(UiNode root)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        // Validate unique keys
        var seen = new HashSet<string>();
        ValidateUniqueKeys(root, seen);

        // Clear existing state
        _root = root;
        _nodeMap.Clear();
        _parentMap.Clear();
        _focusedKey = null;

        // Build lookup maps
        BuildMaps(root);
    }

    /// <summary>
    /// Finds a node by key
    /// </summary>
    public UiNode? FindNode(string key)
    {
        return _nodeMap.TryGetValue(key, out var node) ? node : null;
    }

    /// <summary>
    /// Determines if a node is focusable based on its kind
    /// </summary>
    public bool IsFocusable(UiKind kind)
    {
        return kind switch
        {
            UiKind.Button => true,
            UiKind.CheckBox => true,
            UiKind.Toggle => true,
            UiKind.TextBox => true,
            UiKind.TextArea => true,
            UiKind.ListView => true,
            _ => false
        };
    }

    /// <summary>
    /// Sets the focused key
    /// </summary>
    public void SetFocus(string key)
    {
        if (!_nodeMap.TryGetValue(key, out var node))
            throw new KeyNotFoundException($"Node with key '{key}' not found");

        if (!IsFocusable(node.Kind))
            throw new InvalidOperationException($"Node '{key}' of kind {node.Kind} is not focusable");

        _focusedKey = key;
    }

    /// <summary>
    /// Creates a new node with updated children (used for patching)
    /// </summary>
    private UiNode ReplaceChild(UiNode parent, int index, UiNode newChild)
    {
        var newChildren = parent.Children.ToList();
        newChildren[index] = newChild;
        return parent with { Children = newChildren };
    }

    /// <summary>
    /// Rebuilds a node's path from root with a replacement at a specific key
    /// </summary>
    private UiNode RebuildPath(string targetKey, Func<UiNode, UiNode> transform)
    {
        if (_root == null)
            throw new InvalidOperationException("No root node set");

        if (!_nodeMap.ContainsKey(targetKey))
            throw new KeyNotFoundException($"Node with key '{targetKey}' not found");

        // If target is the root, transform it directly
        if (targetKey == _root.Key)
        {
            return transform(_root);
        }

        // Build path from target to root
        var path = new List<string> { targetKey };
        var current = targetKey;
        while (_parentMap.TryGetValue(current, out var parent))
        {
            path.Add(parent);
            current = parent;
        }
        path.Reverse(); // Now path goes from root to target

        // Recursively rebuild from root down
        return RebuildNode(_root.Key, path, 0, targetKey, transform);
    }

    /// <summary>
    /// Recursively rebuilds a node and its descendants along the path to the target
    /// </summary>
    private UiNode RebuildNode(string currentKey, List<string> path, int pathIndex, string targetKey, Func<UiNode, UiNode> transform)
    {
        var node = _nodeMap[currentKey];
        
        // If this is the target node, apply the transformation
        if (currentKey == targetKey)
        {
            return transform(node);
        }

        // If we've reached the end of the path, return the node unchanged
        if (pathIndex >= path.Count - 1)
        {
            return node;
        }

        // Find which child is on the path to the target
        var nextKeyInPath = path[pathIndex + 1];
        var childIndex = node.Children.ToList().FindIndex(c => c.Key == nextKeyInPath);
        
        if (childIndex < 0)
        {
            // Child not found - this shouldn't happen if maps are consistent
            return node;
        }

        // Rebuild the child that's on the path
        var rebuiltChild = RebuildNode(nextKeyInPath, path, pathIndex + 1, targetKey, transform);
        
        // Create a new version of this node with the rebuilt child
        return ReplaceChild(node, childIndex, rebuiltChild);
    }

    /// <summary>
    /// Helper to rebuild a subtree starting from a key
    /// </summary>
    private UiNode RebuildFromKey(string key, Func<UiNode, UiNode> transform, string targetKey)
    {
        var node = _nodeMap[key];
        if (key == targetKey)
        {
            return transform(node);
        }

        // Check if any child needs transformation
        var newChildren = new List<UiNode>();
        bool childChanged = false;
        foreach (var child in node.Children)
        {
            if (IsAncestor(child.Key, targetKey))
            {
                newChildren.Add(RebuildFromKey(child.Key, transform, targetKey));
                childChanged = true;
            }
            else
            {
                newChildren.Add(child);
            }
        }

        return childChanged ? node with { Children = newChildren } : node;
    }

    /// <summary>
    /// Checks if a node is an ancestor of another
    /// </summary>
    private bool IsAncestor(string ancestorKey, string descendantKey)
    {
        var current = descendantKey;
        while (_parentMap.TryGetValue(current, out var parent))
        {
            if (parent == ancestorKey) return true;
            current = parent;
        }
        return false;
    }

    /// <summary>
    /// Applies a replace operation
    /// </summary>
    public void ApplyReplace(string key, UiNode newNode)
    {
        // Allow setting the root when tree is empty and the caller intends to replace the root key.
        // This makes initial ReplaceOp("frame.root", node) work even when no root was previously set.

        // Validate the new node's subtree for duplicate keys
        var seen = new HashSet<string>();
        ValidateUniqueKeys(newNode, seen);

        if (_root == null)
        {
            // If there's no root, only allow replacing when the requested key matches the new node's key.
            if (key != newNode.Key)
                throw new InvalidOperationException("No root node set");

            // Set the provided node as the new root
            SetRoot(newNode);
            return;
        }

        if (key == _root.Key)
        {
            // Replacing root
            SetRoot(newNode);
        }
        else
        {
            _root = RebuildPath(key, _ => newNode);
            _nodeMap.Clear();
            _parentMap.Clear();
            BuildMaps(_root);
        }
    }

    /// <summary>
    /// Applies an update props operation
    /// </summary>
    public void ApplyUpdateProps(string key, IReadOnlyDictionary<string, object?> props)
    {
        if (!_nodeMap.TryGetValue(key, out var node))
            throw new KeyNotFoundException($"Node with key '{key}' not found");

        ValidateProps(node.Kind, props);

        var updatedNode = node with { Props = props };
        
        if (_root == null)
            throw new InvalidOperationException("No root node set");

        if (key == _root.Key)
        {
            _root = updatedNode;
            _nodeMap[key] = updatedNode;
        }
        else
        {
            _root = RebuildPath(key, _ => updatedNode);
            _nodeMap.Clear();
            _parentMap.Clear();
            BuildMaps(_root);
        }
    }

    /// <summary>
    /// Applies an insert child operation
    /// </summary>
    public void ApplyInsertChild(string parentKey, int index, UiNode newChild)
    {
        if (!_nodeMap.TryGetValue(parentKey, out var parent))
        {
            // Spec invariant: Patch is atomic; missing parent should surface as KeyNotFound so
            // the entire patch rolls back. Silent skip previously led to later UpdateProps
            // exceptions for children that never got inserted. Restoring strict behavior.
            var availableKeys = string.Join(", ", _nodeMap.Keys.Take(20));
            var treeState = _root == null ? "null" : $"root='{_root.Key}'";
            throw new KeyNotFoundException($"Parent node with key '{parentKey}' not found for insert of '{newChild?.Key}'. Tree state: {treeState}, Available keys ({_nodeMap.Count}): {availableKeys}");
        }
        // Allow callers to specify an index beyond the current child count to mean "append".
        // This makes patch producers more resilient to race conditions or off-by-one calculations.
        if (index < 0)
            throw new InvalidOperationException($"Index {index} is negative for parent '{parentKey}'");
        if (index > parent.Children.Count)
        {
            // Clamp to append position instead of throwing.
            index = parent.Children.Count;
        }

        // Validate new child doesn't have duplicate keys with existing tree
        var seen = new HashSet<string>(_nodeMap.Keys);
        ValidateUniqueKeys(newChild, seen);

        var newChildren = parent.Children.ToList();
        newChildren.Insert(index, newChild);
        var updatedParent = parent with { Children = newChildren };

        if (_root == null)
            throw new InvalidOperationException("No root node set");

        if (parentKey == _root.Key)
        {
            _root = updatedParent;
        }
        else
        {
            _root = RebuildPath(parentKey, _ => updatedParent);
        }

        _nodeMap.Clear();
        _parentMap.Clear();
        BuildMaps(_root);
        
        // Validate that the operation succeeded correctly
        if (!_nodeMap.ContainsKey(parentKey))
        {
            throw new InvalidOperationException($"Internal error: Parent key '{parentKey}' missing from rebuilt tree after insert operation");
        }
    }

    /// <summary>
    /// Applies a remove operation
    /// </summary>
    public void ApplyRemove(string key)
    {
        if (_root == null)
            throw new InvalidOperationException("No root node set");

        if (key == _root.Key)
            throw new InvalidOperationException("Cannot remove root node");

        if (!_nodeMap.TryGetValue(key, out var node))
            throw new KeyNotFoundException($"Node with key '{key}' not found");

        if (!_parentMap.TryGetValue(key, out var parentKey))
            throw new InvalidOperationException($"Node '{key}' has no parent");

        var parent = _nodeMap[parentKey];
        var newChildren = parent.Children.Where(c => c.Key != key).ToList();
        var updatedParent = parent with { Children = newChildren };

        _root = RebuildPath(parentKey, _ => updatedParent);
        _nodeMap.Clear();
        _parentMap.Clear();
        BuildMaps(_root);

        // Clear focus if the focused node was removed
        if (_focusedKey == key || IsAncestor(key, _focusedKey ?? ""))
        {
            _focusedKey = null;
        }
    }

    /// <summary>
    /// Applies all operations in a patch atomically
    /// </summary>
    public void ApplyPatch(UiPatch patch)
    {
        if (patch == null)
            throw new ArgumentNullException(nameof(patch));

        // Store original state for rollback
        var originalRoot = _root;
        var originalNodeMap = new Dictionary<string, UiNode>(_nodeMap);
        var originalParentMap = new Dictionary<string, string>(_parentMap);
        var originalFocusedKey = _focusedKey;

        try
        {
            foreach (var op in patch.Ops)
            {
                switch (op)
                {
                    case ReplaceOp replaceOp:
                        ApplyReplace(replaceOp.Key, replaceOp.Node);
                        break;
                    case UpdatePropsOp updatePropsOp:
                        ApplyUpdateProps(updatePropsOp.Key, updatePropsOp.Props);
                        break;
                    case InsertChildOp insertChildOp:
                        ApplyInsertChild(insertChildOp.ParentKey, insertChildOp.Index, insertChildOp.Node);
                        break;
                    case RemoveOp removeOp:
                        ApplyRemove(removeOp.Key);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown operation type: {op.GetType().Name}");
                }
            }
        }
        catch
        {
            // Rollback on any error
            _root = originalRoot;
            _nodeMap.Clear();
            _parentMap.Clear();
            foreach (var kvp in originalNodeMap) _nodeMap[kvp.Key] = kvp.Value;
            foreach (var kvp in originalParentMap) _parentMap[kvp.Key] = kvp.Value;
            _focusedKey = originalFocusedKey;
            throw;
        }
    }
}


/// <summary>
/// Frame has 3 layers: Header (toolbar), Content (a single mounted surface), OverlayStack (0..N modals)
/// </summary>
public sealed record UiFrame(
    UiNode Header,           // e.g., thread title, buttons
    UiNode Content,          // e.g., ChatSurface root
    IReadOnlyList<UiNode> Overlays // e.g., MenuOverlay, FormOverlay (topmost is last)
);

/// <summary>
/// Marker props we standardize in node.Props for routing & layout
/// </summary>
public static class UiProps
{
    public const string ZIndex = "zIndex";          // int
    public const string Role   = "role";            // "frame|header|content|overlay"
    public const string Modal  = "modal";           // bool
    public const string Focusable = "focusable";   // bool
}

/// <summary>
/// Creates and manipulates frame trees with semantic keys for stable patching
/// </summary>
public static class UiFrameBuilder
{
    /// <summary>
    /// Creates a frame tree with semantic keys for stable patching.
    /// Returns root UiNode:
    ///   root(Column role=frame)
    ///     ├── header(Row role=header)
    ///     ├── content(Column role=content)
    ///     └── overlays(Column role=overlay) // children layered by zIndex
    /// </summary>
    public static UiNode Create(UiFrame frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));

        // Build header with role prop
        var headerWithRole = AddRoleIfMissing(frame.Header, "header");

        // Build content with role prop
        var contentWithRole = AddRoleIfMissing(frame.Content, "content");

        // Build overlays container
        var overlayChildren = frame.Overlays
            .Select((overlay, idx) => AddRoleIfMissing(
                AddZIndexIfMissing(overlay, 1000 + idx), 
                "overlay"))
            .ToList();

        var overlaysContainer = new UiNode(
            "frame.overlays",
            UiKind.Column,
            new Dictionary<string, object?>
            {
                [UiProps.Role] = "overlay"
            },
            overlayChildren
        );

        // Build root frame
        return new UiNode(
            "frame.root",
            UiKind.Column,
            new Dictionary<string, object?>
            {
                [UiProps.Role] = "frame"
            },
            new[] { headerWithRole, contentWithRole, overlaysContainer }
        );
    }

    /// <summary>
    /// Creates a patch to replace the content node
    /// </summary>
    public static UiPatch ReplaceContent(UiNode newContent)
    {
        if (newContent == null)
            throw new ArgumentNullException(nameof(newContent));

        var contentWithRole = AddRoleIfMissing(newContent, "content");
        return new UiPatch(new ReplaceOp("frame.content", contentWithRole));
    }

    /// <summary>
    /// Creates a patch to push an overlay onto the stack (top-most modal)
    /// </summary>
    public static UiPatch PushOverlay(UiNode overlay)
    {
        if (overlay == null)
            throw new ArgumentNullException(nameof(overlay));

        // Note: We need to know the current overlay count to set proper zIndex
        // For now, we'll use a high zIndex and let the caller manage order
        var overlayWithRole = AddRoleIfMissing(
            AddZIndexIfMissing(overlay, 2000), 
            "overlay");

        // Insert at the end (highest index = topmost)
        return new UiPatch(new InsertChildOp("frame.overlays", int.MaxValue, overlayWithRole));
    }

    /// <summary>
    /// Creates a patch to pop the top-most overlay from the stack
    /// </summary>
    public static UiPatch PopOverlay(string overlayKey)
    {
        if (string.IsNullOrEmpty(overlayKey))
            throw new ArgumentNullException(nameof(overlayKey));

        return new UiPatch(new RemoveOp(overlayKey));
    }

    /// <summary>
    /// Creates a patch to replace the header node
    /// </summary>
    public static UiPatch ReplaceHeader(UiNode newHeader)
    {
        if (newHeader == null)
            throw new ArgumentNullException(nameof(newHeader));

        var headerWithRole = AddRoleIfMissing(newHeader, "header");
        return new UiPatch(new ReplaceOp("frame.header", headerWithRole));
    }

    // Helper: adds role prop if not present, preserving other props
    private static UiNode AddRoleIfMissing(UiNode node, string role)
    {
        if (node.Props.ContainsKey(UiProps.Role))
            return node;

        var newProps = new Dictionary<string, object?>(node.Props)
        {
            [UiProps.Role] = role
        };

        return node with { Props = newProps };
    }

    // Helper: adds zIndex prop if not present
    private static UiNode AddZIndexIfMissing(UiNode node, int zIndex)
    {
        if (node.Props.ContainsKey(UiProps.ZIndex))
            return node;

        var newProps = new Dictionary<string, object?>(node.Props)
        {
            [UiProps.ZIndex] = zIndex
        };

        return node with { Props = newProps };
    }
}
