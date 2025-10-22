using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Immutable, fluent extension methods for composing UiNode trees.
/// Each method returns a new UiNode instance with updated state.
/// </summary>
public static class UiNodeExtensions
{
    /// <summary>
    /// Append children to the node, returning a new node.
    /// </summary>
    public static UiNode WithChildren(this UiNode node, params UiNode[] children)
    {
        var list = node.Children?.ToList() ?? new List<UiNode>();
        if (children != null && children.Length > 0)
            list.AddRange(children);
        return node with { Children = list };
    }

    /// <summary>
    /// Append a single child.
    /// </summary>
    public static UiNode AddChild(this UiNode node, UiNode child)
        => node.WithChildren(child);

    /// <summary>
    /// Replace or add styles, returning a new node with the new style bag.
    /// </summary>
    public static UiNode WithStyles(this UiNode node, UiStyles styles)
        => node with { Styles = styles ?? UiStyles.Empty };

    /// <summary>
    /// Merge props from an anonymous object or typed record.
    /// Property names match UiProperty keys case-insensitively.
    /// </summary>
    public static UiNode WithProps(this UiNode node, object props)
    {
        if (props is null) return node;

        var merged = node.Props?.ToDictionary(kv => kv.Key, kv => kv.Value)
                     ?? new Dictionary<UiProperty, object?>();

        var type = props.GetType();
        foreach (var p in type.GetProperties())
        {
            var name = p.Name;
            if (!Enum.TryParse<UiProperty>(name, ignoreCase: true, out var key))
                throw new ArgumentException($"Unknown UI property '{name}'. Ensure it exists in UiProperty enum.");
            merged[key] = p.GetValue(props);
        }

        return node with { Props = merged };
    }

    /// <summary>
    /// Merge props from an explicit UiProperty dictionary.
    /// </summary>
    public static UiNode WithProps(this UiNode node, IReadOnlyDictionary<UiProperty, object?> props)
    {
        if (props is null) return node;
        var merged = node.Props?.ToDictionary(kv => kv.Key, kv => kv.Value)
                     ?? new Dictionary<UiProperty, object?>();
        foreach (var kv in props)
            merged[kv.Key] = kv.Value;
        return node with { Props = merged };
    }

    /// <summary>
    /// Convenience: set explicit size via props (Width/Height properties).
    /// </summary>
    public static UiNode WithSize(this UiNode node, double? width = null, double? height = null)
    {
        var dict = new Dictionary<UiProperty, object?>();
        if (width is not null) dict[UiProperty.Width] = width;
        if (height is not null) dict[UiProperty.Height] = height;
        return node.WithProps((IReadOnlyDictionary<UiProperty, object?>)dict);
    }

    /// <summary>
    /// Conditional child composition. If condition is true, appends the child produced by the factory.
    /// </summary>
    public static UiNode If(this UiNode node, bool condition, Func<UiNode> childFactory)
    {
        if (!condition) return node;
        var child = childFactory();
        return node.WithChildren(child);
    }

    /// <summary>
    /// Map over a sequence and append each mapped child.
    /// </summary>
    public static UiNode ForEach<T>(this UiNode node, IEnumerable<T> source, Func<T, UiNode> map)
    {
        if (source is null) return node;
        var children = source.Select(map).ToArray();
        return node.WithChildren(children);
    }
}

/// <summary>
/// Patch helpers to turn a node definition into a UiPatch targeting a key.
/// </summary>
public static class UiPatchExtensions
{
    public static UiPatch ToPatch(this UiNode node, string targetKey)
        => new UiPatchBuilder().Replace(targetKey, node).Build();
}
