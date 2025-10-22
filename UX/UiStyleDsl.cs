using System;
using System.Collections.Generic;

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
