using System;
using System.Collections.Generic;
using System.Linq;

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

    public static UiNode Text(string key, string text) =>
        Node(key, UiKind.Label, new { Text = text });

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
