/// <summary>
/// Central palette and style definitions for <see cref="MailClientOverlay"/>.
/// Intentionally separate from layout and interaction logic so colors can be
/// swapped independently — and eventually at runtime via a theme command.
///
/// Future direction: make this a non-static class loaded from config/theme file,
/// registered with a ThemeManager, and switchable from the system menu.
/// </summary>
public static class MailClientTheme
{
    // ── Raw palette ────────────────────────────────────────────────────────
    public static ConsoleColor ToolbarFg    { get; } = ConsoleColor.White;
    public static ConsoleColor ToolbarBg    { get; } = ConsoleColor.DarkBlue;
    public static ConsoleColor HeaderFg     { get; } = ConsoleColor.Cyan;
    public static ConsoleColor MetaFg       { get; } = ConsoleColor.DarkCyan;
    public static ConsoleColor MutedFg      { get; } = ConsoleColor.DarkGray;
    public static ConsoleColor StatusFg     { get; } = ConsoleColor.DarkGray;
    public static ConsoleColor StatusBg     { get; } = ConsoleColor.Black;

    // ── Composed styles ────────────────────────────────────────────────────

    /// <summary>Top toolbar bar — keyboard-shortcut hints.</summary>
    public static UiStyles Toolbar     => Style.Color(ToolbarFg, ToolbarBg);

    /// <summary>Column panel heading (Favorites / Messages / etc.).</summary>
    public static UiStyles PanelHeader => Style.Combine(Style.Bold, Style.Color(HeaderFg));

    /// <summary>Email header fields: From, To, Date, Subject.</summary>
    public static UiStyles MetaLabel   => Style.Color(MetaFg);

    /// <summary>Dim / secondary text (empty-state hints, separators).</summary>
    public static UiStyles Muted       => Style.Color(MutedFg);

    /// <summary>Bottom status bar.</summary>
    public static UiStyles Status      => Style.Color(StatusFg, StatusBg);
}
