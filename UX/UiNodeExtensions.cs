using System;
using System.Linq;
using System.Collections.Generic;

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

/// <summary>
/// Small style DSL. Note: sizing is modeled as UiProperty (Width/Height),
/// so Style helpers focus on visual styling (bold, align, wraps, etc.).
/// </summary>
public static class Style
{
    public static UiStyles Bold => UiStyles.Empty.With(UiStyleKey.Bold, true);

    public static UiStyles AlignLeft => UiStyles.Empty.With(UiStyleKey.Align, "left");
    public static UiStyles AlignCenter => UiStyles.Empty.With(UiStyleKey.Align, "center");
    public static UiStyles AlignRight => UiStyles.Empty.With(UiStyleKey.Align, "right");

    public static UiStyles Wrap => UiStyles.Empty.With(UiStyleKey.Wrap, true);

    public static UiStyles Color(object? fg = null, object? bg = null)
    {
        var s = UiStyles.Empty;
        if (fg != null) s = s.With(UiStyleKey.ForegroundColor, fg);
        if (bg != null) s = s.With(UiStyleKey.BackgroundColor, bg);
        return s;
    }

    public static UiStyles Tag(string styleTag)
        => UiStyles.Empty.With(UiStyleKey.Style, styleTag);

    public static UiStyles Combine(params UiStyles[] styles)
    {
        var dict = new Dictionary<UiStyleKey, object?>();
        if (styles != null)
        {
            foreach (var s in styles)
            {
                if (s is null) continue;
                foreach (var kv in s.Values) dict[kv.Key] = kv.Value;
            }
        }
        return new UiStyles(dict);
    }
}

/// <summary>
/// Lightweight, declarative DSL entrypoint for building UiNode trees.
/// Keeps construction terse while preserving the underlying immutable model.
/// </summary>
public static class Ui
{
    /// <summary>
    /// Convert an anonymous object or typed record into a UiProperty dictionary.
    /// Property names are matched case-insensitively to UiProperty enum values.
    /// </summary>
    private static IReadOnlyDictionary<UiProperty, object?> ToPropsDictionary(object? props)
    {
        if (props is null) return new Dictionary<UiProperty, object?>();

        var dict = new Dictionary<UiProperty, object?>();
        var type = props.GetType();
        foreach (var p in type.GetProperties())
        {
            var name = p.Name;
            if (!Enum.TryParse<UiProperty>(name, ignoreCase: true, out var key))
            {
                throw new ArgumentException($"Unknown UI property '{name}'. Ensure it exists in UiProperty enum.");
            }
            dict[key] = p.GetValue(props);
        }
        return dict;
    }

    /// <summary>
    /// Generic node factory. Use with anonymous object for props and optional styles.
    /// Children are provided as params for a natural, nested syntax.
    /// </summary>
    public static UiNode Node(
        string key,
        UiKind kind,
        object? props = null,
        UiStyles? styles = null,
        params UiNode[] children)
    {
        return new UiNode(
            key,
            kind,
            ToPropsDictionary(props),
            children?.ToList() ?? new List<UiNode>(),
            styles ?? UiStyles.Empty
        );
    }

    // Common shortcuts

    public static UiNode Spacer(string key, object? props = null) =>
        Node(key, UiKind.Spacer, props);

    public static UiNode Text(string key, string text) =>
        Node(key, UiKind.Label, new { Text = text });

    public static UiNode Markdown(string key, string content) =>
        Node(key, UiKind.Html, new { Content = content });

    public static UiNode Button(string key, string text, object? onClick = null) =>
        Node(key, UiKind.Button, new { Text = text, OnClick = onClick });

    public static UiNode TextBox(string key, string value = "", string? placeholder = null) =>
        Node(key, UiKind.TextBox, new { Text = value, Placeholder = placeholder });

    public static UiNode Row(string key, params UiNode[] children) =>
        Node(key, UiKind.Row, null, null, children);

    public static UiNode Column(string key, params UiNode[] children) =>
        Node(key, UiKind.Column, null, null, children);

    public static UiNode CheckBox(string key, string label, bool isChecked = false, object? onChange = null) =>
        Node(key, UiKind.CheckBox, new { Label = label, Checked = isChecked, OnChange = onChange });

    public static UiNode Toggle(string key, bool value = false, object? onToggle = null) =>
        Node(key, UiKind.Toggle, new { Value = value, OnToggle = onToggle });

    public static UiNode ListView(string key, IEnumerable<object>? items = null, int selectedIndex = -1, object? onItemActivated = null) =>
        Node(key, UiKind.ListView, new { Items = items, SelectedIndex = selectedIndex, OnItemActivated = onItemActivated });
}
