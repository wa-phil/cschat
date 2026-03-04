using System;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Terminal : CUiBase
{
    private TerminalInputRouter? _inputRouter;

    public override async Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt)
    {
        var path = await ReadPathWithAutocompleteAsync(false);
        // Restore the TermDom frame after raw console I/O
        await ForceRefreshAsync();

        if (string.IsNullOrWhiteSpace(path) || (opt.Mode != PathPickerMode.SaveFile && !System.IO.File.Exists(path)))
            return Array.Empty<string>();

        return new List<string> { path };
    }

    private Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory)
    {
        var buffer = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace && buffer.Count > 0)
            {
                buffer.RemoveAt(buffer.Count - 1);
                Write("\b \b");
            }
            else if (key.Key == ConsoleKey.Tab)
            {
                var current = new string(buffer.ToArray());
                var prefix = Path.GetDirectoryName(current) ?? ".";
                var partial = Path.GetFileName(current);
                var matches = Directory
                    .GetFileSystemEntries(prefix)
                    .Where(f => Path.GetFileName(f).StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Count == 1)
                {
                    var completion = matches[0];
                    for (int i = 0; i < partial.Length; i++) Write("\b \b");
                    buffer.RemoveRange(buffer.Count - partial.Length, partial.Length);
                    buffer.AddRange(Path.GetFileName(completion));
                    Write(Path.GetFileName(completion));
                }
                else if (matches.Count > 1)
                {
                    WriteLine();
                    WriteLine("Matches:");
                    matches.ForEach(m => WriteLine("  " + m));
                    Write("> " + new string(buffer.ToArray()));
                }
            }
            else if (key.KeyChar != '\0')
            {
                buffer.Add(key.KeyChar);
                Write(key.KeyChar.ToString());
            }
        }

        var result = new string(buffer.ToArray());
        return Task.FromResult<string?>(string.IsNullOrWhiteSpace(result) ? null : Path.GetFullPath(result));
    }

    public override IInputRouter GetInputRouter()
    {
        if (_inputRouter == null)
        {
            _inputRouter = new TerminalInputRouter();
        }
        return _inputRouter;
    }

    public override async Task RenderTableAsync(Table table, string? title = null)
    {
        int maxWidth = Width - 1;
        var hs = table.Headers.ToList();
        var rowList = table.Rows.ToList();

        int origCount = hs.Count;
        var indices = Enumerable.Range(0, origCount).ToList();

        if (rowList.Count > 0)
        {
            indices = indices.Where(i => rowList.Any(r => i < r.Length && !string.IsNullOrEmpty(r[i]))).ToList();
            if (indices.Count == 0) indices = Enumerable.Range(0, origCount).ToList();
        }

        var maxLens = new List<int>();
        foreach (var i in indices)
        {
            int w = hs[i]?.Length ?? 0;
            foreach (var r in rowList)
            {
                if (i < r.Length && r[i] != null)
                    w = Math.Max(w, r[i].Length);
            }
            maxLens.Add(w);
        }

        int colCount = indices.Count;
        if (colCount == 0) return; // nothing to render

        int sepWidth = 3 * Math.Max(0, colCount - 1);
        int contentMax = Math.Max(1, maxWidth - sepWidth);

        int minCol = 6;
        if (contentMax < colCount * minCol)
        {
            minCol = Math.Max(1, contentMax / colCount);
        }

        var widths = new int[colCount];
        int allocated = 0;
        for (int idx = 0; idx < colCount; idx++)
        {
            int remaining = colCount - idx - 1;
            int minForRemaining = remaining * minCol;
            int availableForThis = contentMax - allocated - minForRemaining;
            int desired = Math.Min(maxLens[idx], contentMax);
            int w = Math.Clamp(desired, minCol, Math.Max(minCol, availableForThis));
            widths[idx] = w;
            allocated += w;
        }

        int leftover = contentMax - allocated;
        for (int i = 0; leftover > 0 && i < colCount; i++)
        {
            int add = Math.Min(leftover, Math.Max(0, maxLens[i] - widths[i]));
            widths[i] += add;
            leftover -= add;
        }
        for (int i = 0; leftover > 0 && i < colCount; i++)
        {
            widths[i] += 1;
            leftover -= 1;
        }

        string Fit(string s, int w) => (s.Length <= w) ? s.PadRight(w) : s.Substring(0, Math.Max(0, w - 1)) + "…";

        var lines = new List<string>();
        lines.Add(string.Join(" │ ", indices.Select((origIdx, j) => Fit(hs[origIdx] ?? "", widths[j]))));
        lines.Add(string.Join("─┼─", widths.Select(c => new string('─', Math.Max(1, c)))));

        foreach (var row in rowList)
        {
            var parts = new List<string>();
            for (int j = 0; j < colCount; j++)
            {
                var origIdx = indices[j];
                var s = (origIdx < row.Length) ? (row[origIdx] ?? "") : "";
                parts.Add(Fit(s, widths[j]));
            }
            lines.Add(string.Join(" │ ", parts));
        }

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.AppendLine(title);
            sb.AppendLine(new string('-', Math.Min(maxWidth, title.Length)));
        }

        foreach (var line in lines) sb.AppendLine(line);
        var text = sb.ToString().TrimEnd();
        var message = new ChatMessage { Role = Roles.Tool, Content = text };
        Program.Context.AddToolMessage(text);
        await RenderChatMessageAsync(message);
    }

    public override async Task RenderReportAsync(Report report)
    {
        var width = Math.Max(20, Width - 1);
        var text = report?.ToPlainText(width) ?? "";
        var msg = new ChatMessage { Role = Roles.Tool, Content = text };
        Program.Context.AddToolMessage(text);
        await RenderChatMessageAsync(msg);
    }

    public override Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept)
        => Task.FromResult(Console.ReadKey(intercept));

    public override int CursorTop { get => Console.CursorTop; }

    public override int Width { get => Console.WindowWidth; }

    public override int Height { get => Console.WindowHeight; }

    public override bool KeyAvailable { get => Console.KeyAvailable; }

    public override bool IsOutputRedirected { get; } = Console.IsOutputRedirected;
    public override void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);

    public override ConsoleColor ForegroundColor
    {
        get => Console.ForegroundColor;
        set => Console.ForegroundColor = value;
    }

    public override ConsoleColor BackgroundColor
    {
        get => Console.BackgroundColor;
        set => Console.BackgroundColor = value;
    }

    public override void ResetColor() => Console.ResetColor();

    public override void Write(string text) => Console.Write(text);
    public override void WriteLine(string? text = null) => Console.WriteLine(text);

    public override void Clear() => Console.Clear();

    public override async Task RunAsync(Func<Task> appMain)
    {
        using var cts = new System.Threading.CancellationTokenSource();
        var resizeTask = WatchResizeAsync(cts.Token);
        try
        {
            await appMain();
        }
        finally
        {
            cts.Cancel();
            try { await resizeTask; } catch (OperationCanceledException) { }
        }
    }

    /// <summary>
    /// Background task that polls for terminal window size changes every 250ms.
    /// On resize: invalidates _lastSnapshot and forces a full re-render so that
    /// line positions stored in the snapshot remain consistent with the new dimensions.
    /// </summary>
    private async Task WatchResizeAsync(System.Threading.CancellationToken ct)
    {
        int lastW = Console.WindowWidth;
        int lastH = Console.WindowHeight;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(250, ct);
                int w = Console.WindowWidth;
                int h = Console.WindowHeight;
                if (w != lastW || h != lastH)
                {
                    lastW = w;
                    lastH = h;
                    // Null the snapshot first so PostSetRootAsync does a full repaint
                    _lastSnapshot = null;
                    if (_uiTree.Root != null)
                        await PostSetRootAsync(_uiTree.Root, _controlOptions ?? new UiControlOptions());
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore transient errors */ }
        }
    }

    // Declarative control layer - override base implementations for Terminal-specific rendering
    private readonly TermDom _termDom = new();
    private TermSnapshot? _lastSnapshot;

    // Pending-render counter for streaming patch debounce (~60fps coalescing).
    // Incremented by each PostPatchAsync entry; decremented after the 16ms wait.
    // Only the last decrementer (counter reaches 0) performs the actual render.
    private int _pendingRender = 0;

    protected override Task PostSetRootAsync(UiNode root, UiControlOptions options) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();

        // Clear the console and do initial render using TermDom
        Clear();
        _lastSnapshot = _termDom.Layout(root, Width, Height, _uiTree.FocusedKey);
        _termDom.Apply(_termDom.GetFullRender(_lastSnapshot));

        ctx.Succeeded();
        return Task.CompletedTask;
    });

    protected override async Task PostPatchAsync(UiPatch patch)
    {
        // Debounce: coalesce rapid patches (e.g. streaming tokens) at ~60fps.
        // Tree mutations were already applied in CUiBase.PatchAsync before this call.
        // We only gate the expensive TermDom render pass.
        System.Threading.Interlocked.Increment(ref _pendingRender);
        await Task.Delay(16);
        int remaining = System.Threading.Interlocked.Decrement(ref _pendingRender);

        // If more patches arrived during the wait, skip this render — last waiter wins.
        if (remaining > 0) return;

        if (_uiTree.Root != null)
        {
            var newSnapshot = _termDom.Layout(_uiTree.Root, Width, Height, _uiTree.FocusedKey);

            if (_lastSnapshot != null)
            {
                // Compute and apply minimal edits
                var edits = _termDom.Diff(_lastSnapshot, newSnapshot);
                _termDom.Apply(edits);
            }
            else
            {
                // First render or snapshot unavailable
                Clear();
                _termDom.Apply(_termDom.GetFullRender(newSnapshot));
            }

            _lastSnapshot = newSnapshot;
        }
    }

    protected override Task PostFocusAsync(string key)
    {
        // Recompute layout to reflect focus highlight changes and apply minimal edits
        if (_uiTree.Root != null)
        {
            var newSnapshot = _termDom.Layout(_uiTree.Root, Width, Height, _uiTree.FocusedKey);

            if (_lastSnapshot != null)
            {
                var edits = _termDom.Diff(_lastSnapshot, newSnapshot);
                _termDom.Apply(edits);
            }
            else
            {
                Clear();
                _termDom.Apply(_termDom.GetFullRender(newSnapshot));
            }

            _lastSnapshot = newSnapshot;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Terminal Virtual-DOM for incremental rendering
    /// Flattens UiNode -> lines with attributes, maintains key→region map, computes minimal edits
    /// </summary>
    private sealed class TermDom
    {
        // Lightweight drawing context provided to per-kind renderers
        private readonly struct TermCtx
        {
            // Full layout delegate: writes child into caller-supplied buffers (used by dock-bottom/grid).
            private readonly Action<UiNode, int, int, List<TermLine>, Dictionary<string, TermRegion>> _layoutInto;

            public TermCtx(int indent, int width, int screenHeight, string? focusedKey,
                List<TermLine> lines, Dictionary<string, TermRegion> keyMap,
                Action<UiNode, int, int, List<TermLine>, Dictionary<string, TermRegion>> layoutInto)
            {
                Indent = indent;
                Width = width;
                ScreenHeight = screenHeight;
                FocusedKey = focusedKey;
                Lines = lines;
                KeyMap = keyMap;
                _layoutInto = layoutInto;
            }
            public int Indent { get; }
            public int Width { get; }
            public int ScreenHeight { get; }
            public string? FocusedKey { get; }
            public List<TermLine> Lines { get; }
            public Dictionary<string, TermRegion> KeyMap { get; }
            /// <summary>Recurse into a child, writing to this context's shared buffers.</summary>
            public void LayoutChild(UiNode child, int childIndent, int childWidth)
                => _layoutInto(child, childIndent, childWidth, Lines, KeyMap);
            /// <summary>Recurse into a child, writing to caller-supplied buffers.</summary>
            public void LayoutInto(UiNode child, int childIndent, int childWidth,
                List<TermLine> ls, Dictionary<string, TermRegion> km)
                => _layoutInto(child, childIndent, childWidth, ls, km);
            public string IndentStr => new string(' ', Indent * 2);
            public bool IsFocused(UiNode n) => n.Key == FocusedKey;
            /// <summary>Returns a copy of this context with different indent and width (same buffers).</summary>
            public TermCtx WithBounds(int newIndent, int newWidth) =>
                new TermCtx(newIndent, newWidth, ScreenHeight, FocusedKey, Lines, KeyMap, _layoutInto);
        }

        // UiKind -> render function registry (all kinds, leaf and container)
        private readonly Dictionary<UiKind, Action<TermCtx, UiNode>> _render;

        public TermDom()
        {
            _render = new()
            {
                // Label
                [UiKind.Label] = (ctx, node) =>
                {
                    var align = ResolveAlign(node);
                    var wrap = ResolveWrap(node);
                    var fg = ResolveFg(node, ConsoleColor.Gray);
                    var bg = ResolveBg(node, ctx.IsFocused(node) ? ConsoleColor.DarkGray : ConsoleColor.Black);

                    var raw = node.Props.TryGetValue(UiProperty.Text, out var labelText)
                        ? (labelText?.ToString() ?? string.Empty)
                        : string.Empty;

                    if (wrap)
                    {
                        int labAvail = Math.Max(1, ctx.Width - ctx.Indent * 2);
                        foreach (var seg in WrapText(raw, labAvail))
                        {
                            var textOut = (align == TextAlign.Center) ? seg : ($"{ctx.IndentStr}{seg}");
                            ctx.Lines.Add(new TermLine(textOut, fg, bg, align));
                        }
                    }
                    else
                    {
                        var parts = (raw ?? string.Empty).Replace("\r\n", "\n").Split('\n');
                        foreach (var part in parts)
                        {
                            var textOut = (align == TextAlign.Center) ? part : ($"{ctx.IndentStr}{part}");
                            ctx.Lines.Add(new TermLine(textOut, fg, bg, align));
                        }
                    }
                },

                // Button
                [UiKind.Button] = (ctx, node) =>
                {
                    if (!node.Props.TryGetValue(UiProperty.Text, out var btnText)) btnText = string.Empty;
                    var isFocused = ctx.IsFocused(node);
                    var fg = isFocused ? ConsoleColor.Black : ConsoleColor.White;
                    var bg = isFocused ? ConsoleColor.White : ConsoleColor.DarkGray;
                    ctx.Lines.Add(new TermLine($"{ctx.IndentStr}[ {btnText} ]", fg, bg, TextAlign.Left));
                },

                // TextBox/TextArea
                [UiKind.TextBox]  = DrawTextInput,
                [UiKind.TextArea] = DrawTextInput,

                // CheckBox/Toggle
                [UiKind.CheckBox] = DrawCheckLike,
                [UiKind.Toggle]   = DrawCheckLike,

                // ListView (includes dropdown role)
                [UiKind.ListView] = (ctx, node) =>
                {
                    if (!node.Props.TryGetValue(UiProperty.Items, out var itemsObj) || itemsObj is not IEnumerable<object> items)
                        return;

                    var isFocused = ctx.IsFocused(node);
                    var isDropdown = node.Props.TryGetValue(UiProperty.Role, out var role) && string.Equals(role?.ToString(), "dropdown", StringComparison.OrdinalIgnoreCase);
                    var selectedIndex = node.Props.TryGetValue(UiProperty.SelectedIndex, out var si) && si is int idx ? idx : -1;
                    var itemList = items.Select(o => o?.ToString() ?? string.Empty).ToList();

                    if (isDropdown)
                    {
                        string current = (selectedIndex >= 0 && selectedIndex < itemList.Count) ? itemList[selectedIndex] : string.Empty;
                        if (string.IsNullOrWhiteSpace(current))
                            current = node.Props.TryGetValue(UiProperty.Placeholder, out var phv) ? (phv?.ToString() ?? "Select…") : "Select…";

                        var fg = isFocused ? ConsoleColor.Black : ResolveFg(node, ConsoleColor.Gray);
                        var bg = isFocused ? ConsoleColor.White : ResolveBg(node, ConsoleColor.Black);
                        var text = $"⭥[ {current} ]";
                        var outLine = $"{ctx.IndentStr}{text}";
                        outLine = EnsureWidth(outLine, ctx.Width);
                        ctx.Lines.Add(new TermLine(outLine, fg, bg, TextAlign.Left));
                        return;
                    }

                    int maxVisible = TryGetIntProp(node.Props, UiProperty.Height) ?? Program.config.MaxMenuItems;
                    maxVisible = Math.Max(1, maxVisible);
                    int count = itemList.Count;

                    if (count == 0)
                    {
                        var fgEmpty = isFocused ? ConsoleColor.Black : ResolveFg(node, ConsoleColor.DarkGray);
                        var bgEmpty = isFocused ? ConsoleColor.DarkGray : ResolveBg(node, ConsoleColor.Black);
                        var emptyText = $"{ctx.IndentStr}  (empty)";
                        emptyText = EnsureWidth(emptyText, ctx.Width);
                        ctx.Lines.Add(new TermLine(emptyText, fgEmpty, bgEmpty, TextAlign.Left));
                        return;
                    }

                    if (selectedIndex < 0) selectedIndex = 0;
                    if (selectedIndex >= count) selectedIndex = count - 1;

                    int visibleCount = Math.Min(maxVisible, count);
                    int offset = Math.Min(Math.Max(0, selectedIndex - visibleCount + 1), Math.Max(0, count - visibleCount));
                    bool showScrollbar = count > visibleCount;
                    int contentRight = showScrollbar ? (ctx.Width - 1) : ctx.Width;

                    var scroll = ComputeScrollMetrics(count, visibleCount, offset);

                    for (int j = 0; j < visibleCount; j++)
                    {
                        int i = offset + j;
                        var isSelected = i == selectedIndex;
                        var fg = isSelected ? ConsoleColor.Black : (isFocused ? ConsoleColor.Black : ResolveFg(node, ConsoleColor.Gray));
                        var bg = isSelected ? ConsoleColor.White : (isFocused ? ConsoleColor.DarkGray : ResolveBg(node, ConsoleColor.Black));

                        var prefix = $"{ctx.IndentStr}    ";
                        int availForItem = Math.Max(1, contentRight - prefix.Length);
                        var itemText = itemList[i] ?? string.Empty;
                        if (itemText.Length > availForItem)
                            itemText = itemText.Substring(0, Math.Max(0, availForItem - 1)) + "…";

                        var rowText = prefix + itemText;
                        if (rowText.Length < contentRight) rowText = rowText.PadRight(contentRight);
                        else if (rowText.Length > contentRight) rowText = rowText.Substring(0, contentRight);

                        if (showScrollbar)
                        {
                            rowText += ScrollbarGlyph(j, scroll);
                        }

                        rowText = EnsureWidth(rowText, ctx.Width);
                        ctx.Lines.Add(new TermLine(rowText, fg, bg, TextAlign.Left));
                    }
                },

                // Html — shown as plain text in the terminal; respects the same styles as Label
                [UiKind.Html] = (ctx, node) =>
                {
                    if (!node.Props.TryGetValue(UiProperty.Content, out var htmlContent)) return;
                    var raw = htmlContent?.ToString() ?? string.Empty;
                    var align = ResolveAlign(node);
                    var wrap  = ResolveWrap(node);
                    var fg    = ResolveFg(node, ConsoleColor.Gray);
                    var bg    = ResolveBg(node, ConsoleColor.Black);
                    if (wrap)
                    {
                        int avail = Math.Max(1, ctx.Width - ctx.Indent * 2);
                        foreach (var seg in WrapText(raw, avail))
                            ctx.Lines.Add(new TermLine(align == TextAlign.Center ? seg : $"{ctx.IndentStr}{seg}", fg, bg, align));
                    }
                    else
                    {
                        foreach (var part in raw.Replace("\r\n", "\n").Split('\n'))
                            ctx.Lines.Add(new TermLine(align == TextAlign.Center ? part : $"{ctx.IndentStr}{part}", fg, bg, align));
                    }
                },

                // Spacer
                [UiKind.Spacer] = (ctx, node) =>
                {
                    var height = node.Props.TryGetValue(UiProperty.Height, out var h) && h is int ht ? ht : 1;
                    for (int i = 0; i < height; i++)
                        ctx.Lines.Add(new TermLine(string.Empty, ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left));
                },

                // Accordion / containers
                [UiKind.Accordion] = RenderAccordion,
                [UiKind.Column]    = RenderColumn,
                [UiKind.Row]       = RenderRow,
            };

        }

        private static void DrawTextInput(TermCtx ctx, UiNode node)
        {
            var isFocused = ctx.IsFocused(node);
            var value = node.Props.TryGetValue(UiProperty.Text, out var v) ? v?.ToString() : (node.Props.TryGetValue(UiProperty.Value, out var v2) ? v2?.ToString() : "");
            var placeholder = node.Props.TryGetValue(UiProperty.Placeholder, out var p) ? p?.ToString() : "";
            var displayText = string.IsNullOrEmpty(value) ? placeholder : value;
            var textFg = isFocused ? ConsoleColor.Black : (string.IsNullOrEmpty(value) ? ConsoleColor.DarkGray : ResolveFg(node, ConsoleColor.White));
            var textBg = isFocused ? ConsoleColor.White : ResolveBg(node, ConsoleColor.Black);
            int avail = Math.Max(1, ctx.Width - ctx.Indent * 2);
            if (string.IsNullOrEmpty(displayText)) displayText = "";
            var wrapped = WrapText(displayText, avail);
            foreach (var wline in wrapped)
                ctx.Lines.Add(new TermLine($"{ctx.IndentStr}{wline}", textFg, textBg, TextAlign.Left));
        }

        private static void DrawCheckLike(TermCtx ctx, UiNode node)
        {
            var isFocused = ctx.IsFocused(node);
            var isChecked = node.Props.TryGetValue(UiProperty.Checked, out var chk) && chk is bool c && c;
            var checkbox = isChecked ? "[✓]" : "[ ]";
            var cbLabel = node.Props.TryGetValue(UiProperty.Text, out var cbt) ? cbt?.ToString() : "";
            var cbFg = isFocused ? ConsoleColor.Black : ResolveFg(node, ConsoleColor.Gray);
            var cbBg = isFocused ? ConsoleColor.White : ResolveBg(node, ConsoleColor.Black);
            ctx.Lines.Add(new TermLine($"{ctx.IndentStr}{checkbox} {cbLabel}", cbFg, cbBg, TextAlign.Left));
        }

        // Shared helpers
        private static bool TryParseColor(object? val, out ConsoleColor color)
        {
            if (val is ConsoleColor cc) { color = cc; return true; }
            if (val is string s && Enum.TryParse<ConsoleColor>(s, true, out var parsed)) { color = parsed; return true; }
            color = ConsoleColor.Gray; return false;
        }

        private static ConsoleColor ResolveFg(UiNode n, ConsoleColor @default)
        {
            if (n.Styles.Get<object?>(UiStyleKey.ForegroundColor) is object st && TryParseColor(st, out var c)) return c;
            var styleStr = n.Styles.Get<string>(UiStyleKey.Style) ?? (n.Props.TryGetValue(UiProperty.Style, out var ls) ? ls as string : null);
            if (string.Equals(styleStr, "dim", StringComparison.OrdinalIgnoreCase)) return ConsoleColor.DarkGray;
            return @default;
        }

        private static ConsoleColor ResolveBg(UiNode n, ConsoleColor @default)
        {
            if (n.Styles.Get<object?>(UiStyleKey.BackgroundColor) is object st && TryParseColor(st, out var c)) return c;
            return @default;
        }

        private static TextAlign ResolveAlign(UiNode n)
        {
            var a = n.Styles.Get<string>(UiStyleKey.Align);
            return string.Equals(a, "center", StringComparison.OrdinalIgnoreCase) ? TextAlign.Center : TextAlign.Left;
        }

        private static bool ResolveWrap(UiNode n)
        {
            var sv = n.Styles.Get<object?>(UiStyleKey.Wrap);
            if (sv is bool b) return b;
            return false;
        }
        /// <summary>
        /// Flattens UiNode -> lines with attributes (fg/bg), maintains key→region map
        /// </summary>
        public TermSnapshot Layout(UiNode root, int width, int screenHeight, string? focusedKey)
        {
            var lines = new List<TermLine>();
            var keyMap = new Dictionary<string, TermRegion>();

            // Base layout (header + content). Skip overlays container; we'll composite overlays later.
            UiNode? overlaysContainer = null;
            if (root.Key == UiFrameKeys.Root)
            {
                foreach (var child in root.Children)
                {
                    if (child.Key == UiFrameKeys.Overlays)
                    {
                        overlaysContainer = child;
                        continue;
                    }
                    LayoutNode(child, 0, width, screenHeight, focusedKey, lines, keyMap);
                }
            }
            else
            {
                LayoutNode(root, 0, width, screenHeight, focusedKey, lines, keyMap);
            }

            // Composite overlays on top (modal). Later overlays in the list have higher z-order.
            if (overlaysContainer != null && overlaysContainer.Children.Count > 0)
            {
                // Stacking order is determined by child order (last child = topmost).
                foreach (var overlay in overlaysContainer.Children)
                {
                    CompositeOverlayBox(overlay, width, screenHeight, focusedKey, lines, keyMap);
                }
            }

            return new TermSnapshot(lines, keyMap);
        }

        /// <summary>
        /// Computes minimal edits to transform old snapshot into new snapshot
        /// </summary>
        public IEnumerable<TermEdit> Diff(TermSnapshot oldSnap, TermSnapshot newSnap)
        {
            var edits = new List<TermEdit>();
            var maxLines = Math.Max(oldSnap.Lines.Count, newSnap.Lines.Count);

            for (int i = 0; i < maxLines; i++)
            {
                var oldLine = i < oldSnap.Lines.Count ? oldSnap.Lines[i] : null;
                var newLine = i < newSnap.Lines.Count ? newSnap.Lines[i] : null;

                if (oldLine == null && newLine != null)
                {
                    // New line added
                    edits.Add(new TermEdit(i, newLine));
                }
                else if (oldLine != null && newLine == null)
                {
                    // Line removed
                    edits.Add(new TermEdit(i, new TermLine("", ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left)));
                }
                else if (oldLine != null && newLine != null && !TermLineEqual(oldLine, newLine))
                {
                    // Line changed
                    edits.Add(new TermEdit(i, newLine));
                }
            }

            return edits;
        }

        private static bool TermLineEqual(TermLine a, TermLine b)
        {
            if (a.Align != b.Align) return false;
            // If either side uses runs, compare runs content+colors
            if (a.Runs is { } ar || b.Runs is { } br)
            {
                var aRuns = a.Runs ?? new List<TermRun> { new TermRun(a.Text ?? string.Empty, a.Foreground, a.Background) };
                var bRuns = b.Runs ?? new List<TermRun> { new TermRun(b.Text ?? string.Empty, b.Foreground, b.Background) };
                if (aRuns.Count != bRuns.Count) return false;
                for (int i = 0; i < aRuns.Count; i++)
                {
                    var arun = aRuns[i];
                    var brun = bRuns[i];
                    if (!string.Equals(arun.Text, brun.Text, StringComparison.Ordinal) || arun.Foreground != brun.Foreground || arun.Background != brun.Background)
                        return false;
                }
                return true;
            }
            // Fallback to simple value comparison
            return string.Equals(a.Text, b.Text, StringComparison.Ordinal) && a.Foreground == b.Foreground && a.Background == b.Background;
        }

        /// <summary>
        /// Applies edits to console without scrolling, respects z-index (overlays last)
        /// </summary>
        public void Apply(IEnumerable<TermEdit> edits)
        {
            Console.CursorVisible = false;
            foreach (var edit in edits)
            {
                Console.SetCursorPosition(0, edit.LineIndex);
                var width = Console.WindowWidth;

                if (edit.Line.Runs is { } runs)
                {
                    // Optional centering for runs: compute total length
                    int totalLen = 0;
                    foreach (var r in runs) totalLen += r.Text?.Length ?? 0;
                    int leftPad = 0;
                    if (edit.Line.Align == TextAlign.Center)
                    {
                        leftPad = Math.Max(0, (width - Math.Min(width, totalLen)) / 2);
                    }

                    // Left pad if needed
                    if (leftPad > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.Write(new string(' ', Math.Min(width, leftPad)));
                    }

                    int remaining = Math.Max(0, width - leftPad);
                    foreach (var r in runs)
                    {
                        if (remaining <= 0) break;
                        var t = r.Text ?? string.Empty;
                        if (t.Length > remaining) t = t.Substring(0, remaining);
                        Console.ForegroundColor = r.Foreground;
                        Console.BackgroundColor = r.Background;
                        Console.Write(t);
                        remaining -= t.Length;
                    }
                    // Pad out remainder of the line
                    if (remaining > 0)
                    {
                        // Use last run colors if available
                        var last = runs.Count > 0 ? runs[^1] : new TermRun("", ConsoleColor.Gray, ConsoleColor.Black);
                        Console.ForegroundColor = last.Foreground;
                        Console.BackgroundColor = last.Background;
                        Console.Write(new string(' ', remaining));
                    }
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = edit.Line.Foreground;
                    Console.BackgroundColor = edit.Line.Background;

                    // Pad or truncate to console width
                    var text = edit.Line.Text;
                    // If requested, center within full console width for base content lines
                    if (edit.Line.Align == TextAlign.Center)
                    {
                        // Avoid re-centering overlay border lines that include box-drawing chars
                        bool hasOverlayGlyph = text.IndexOf('┌') >= 0 || text.IndexOf('┐') >= 0 || text.IndexOf('└') >= 0 ||
                                               text.IndexOf('┘') >= 0 || text.IndexOf('│') >= 0 || text.IndexOf('─') >= 0 ||
                                               text.IndexOf('█') >= 0;
                        if (!hasOverlayGlyph)
                        {
                            var content = text ?? string.Empty;
                            if (content.Length > Math.Max(1, width))
                                content = (width <= 1) ? "…" : content.Substring(0, Math.Max(0, width - 1)) + "…";
                            int leftPad = Math.Max(0, (width - content.Length) / 2);
                            text = new string(' ', leftPad) + content;
                        }
                    }
                    if (text.Length < width)
                        text = text.PadRight(width);
                    else if (text.Length > width)
                        text = text.Substring(0, width);

                    Console.Write(text);
                    Console.ResetColor();
                }
            }
        }

        /// <summary>
        /// Gets all edits for a full render (used for initial render)
        /// </summary>
        public IEnumerable<TermEdit> GetFullRender(TermSnapshot snapshot)
        {
            return snapshot.Lines.Select((line, index) => new TermEdit(index, line));
        }

        private void LayoutNode(UiNode node, int indent, int width, int screenHeight, string? focusedKey, List<TermLine> lines, Dictionary<string, TermRegion> keyMap)
        {
            var startLine = lines.Count;

            var ctx = new TermCtx(indent, width, screenHeight, focusedKey, lines, keyMap,
                (child, ci, cw, ls, km) => LayoutNode(child, ci, cw, screenHeight, focusedKey, ls, km));
            if (_render.TryGetValue(node.Kind, out var renderer))
            {
                renderer(ctx, node);
            }

            // Record region for this node
            var endLine = lines.Count;
            if (endLine > startLine)
            {
                keyMap[node.Key] = new TermRegion(startLine, endLine - startLine);
            }
        }

        /// <summary>
        /// Compose a modal overlay rectangle centered in the viewport over existing lines.
        /// Preserves text outside the rectangle, so background content remains visible.
        /// </summary>
        private void CompositeOverlayBox(UiNode overlay, int screenWidth, int screenHeight, string? focusedKey, List<TermLine> baseLines, Dictionary<string, TermRegion> keyMap)
        {
            // Determine overlay box width from props (supports "80%" or absolute int), with sensible bounds
            int boxWidth = Math.Clamp(ParseWidth(overlay.Props, screenWidth), Math.Min(20, Math.Max(1, screenWidth - 2)), Math.Max(10, screenWidth - 2));
            int xStart = Math.Max(0, (screenWidth - boxWidth) / 2);
            int contentWidth = Math.Max(1, boxWidth - 2); // borders on left/right

            // Render overlay content into temporary lines (no outer indentation)
            var innerLines = new List<TermLine>();
            var tmpMap = new Dictionary<string, TermRegion>();
            foreach (var child in overlay.Children)
            {
                // Layout inner content to the overlay's content width so columns (e.g., edit/delete/add) fit inside the box
                LayoutNode(child, 0, contentWidth, screenHeight, focusedKey, innerLines, tmpMap);
            }

            var borderFg = ConsoleColor.Gray;
            var borderBg = ConsoleColor.Black;

            if (innerLines.Count == 0)
                innerLines.Add(new TermLine("", ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left));

            // Determine visible window budget for inner content inside the box (excludes top/bottom borders)
            int maxVisibleContent = Math.Max(1, screenHeight - 4); // leave at least 1 line margin overall
            int totalContent = innerLines.Count;

            // Identify scrollable body region among overlay children, preferring a child with AutoScroll flag or Min offset
            UiNode? bodyChild = null;
            foreach (var ch in overlay.Children)
            {
                if (ch.Props.ContainsKey(UiProperty.AutoScroll) || ch.Props.ContainsKey(UiProperty.Min) ||
                    (ch.Props.TryGetValue(UiProperty.Role, out var rv) && string.Equals(rv?.ToString(), "body", StringComparison.OrdinalIgnoreCase)))
                {
                    bodyChild = ch;
                    break;
                }
            }

            // Resolve body region bounds (within innerLines)
            int bodyStart = 0;
            int bodyLen = totalContent;
            if (bodyChild != null && tmpMap.TryGetValue(bodyChild.Key, out var bodyRegion))
            {
                bodyStart = Math.Max(0, Math.Min(totalContent, bodyRegion.StartLine));
                bodyLen = Math.Max(0, Math.Min(totalContent - bodyStart, bodyRegion.LineCount));
            }

            // Header/footer (non-scrollable) sizes
            int headerCount = Math.Max(0, Math.Min(bodyStart, totalContent));
            int footerCount = Math.Max(0, Math.Min(totalContent - (bodyStart + bodyLen), totalContent));

            // Compute body viewport height from remaining space after header+footer
            int bodyVisible = Math.Max(1, Math.Min(bodyLen, maxVisibleContent - headerCount - footerCount));
            // If header+footer consume almost all the space, clamp so we still show something
            if (headerCount + footerCount >= maxVisibleContent)
            {
                bodyVisible = 1;
            }

            // Compute focus target line and relative index within body when applicable
            int targetLine = -1;
            int targetRel = -1;
            if (!string.IsNullOrEmpty(focusedKey))
            {
                if (tmpMap.TryGetValue(focusedKey, out var region))
                    targetLine = Math.Max(0, Math.Min(totalContent - 1, region.StartLine));
                else if (tmpMap.TryGetValue(focusedKey + "-row", out var rowRegion))
                    targetLine = Math.Max(0, Math.Min(totalContent - 1, rowRegion.StartLine));

                if (targetLine >= bodyStart && targetLine < bodyStart + bodyLen)
                    targetRel = targetLine - bodyStart;
            }

            // Determine requested scroll offset from body child (AutoScroll=false + Min), if provided
            int? requestedOffset = null;
            if (bodyChild != null)
            {
                int? minProp = TryGetIntProp(bodyChild.Props, UiProperty.Min);
                // If AutoScroll is explicitly false or Min is present, treat it as body-driven scroll
                if ((bodyChild.Props.TryGetValue(UiProperty.AutoScroll, out var asv2) && asv2 is bool ab && !ab) || minProp.HasValue)
                {
                    requestedOffset = Math.Max(0, minProp ?? 0);
                }
            }

            // Compute effective offset inside body region
            int offset = ResolveScrollOffset(bodyLen, bodyVisible, false, requestedOffset, targetRel);

            // Scrollbar metrics based on body region
            var overlayScroll = ComputeScrollMetrics(bodyLen, bodyVisible, offset);

            // Compute how many header/footer lines can be shown within the content budget
            int headerVisible = Math.Min(headerCount, Math.Max(0, maxVisibleContent - bodyVisible));
            int footerVisible = Math.Min(footerCount, Math.Max(0, maxVisibleContent - bodyVisible - headerVisible));

            var boxRunLines = BuildBoxRunLines(innerLines, contentWidth,
                headerVisible, bodyStart, bodyLen, bodyVisible, footerVisible,
                offset, overlayScroll, borderFg, borderBg);

            // Compute vertical placement: center in current visible area approximation
            int viewportHeight = Math.Max(10, screenHeight);
            int boxHeight = boxRunLines.Count;
            int yStart = Math.Max(0, (Math.Min(baseLines.Count, viewportHeight) - boxHeight) / 2);

            // Ensure base lines list is long enough
            int requiredLines = yStart + boxHeight;
            while (baseLines.Count < requiredLines)
            {
                baseLines.Add(new TermLine("", ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left));
            }

            // Composite box over base lines, preserving text outside the box rectangle
            for (int i = 0; i < boxHeight; i++)
            {
                int lineIndex = yStart + i;
                // Expand base line into exact-width runs preserving original colors
                var baseRuns = ToExactWidthRuns(baseLines[lineIndex], screenWidth);
                var leftRuns = SliceRuns(baseRuns, 0, Math.Min(xStart, screenWidth));
                var overlayRuns = boxRunLines[i];
                // Ensure overlay runs exactly fill boxWidth
                overlayRuns = ClipOrPadRuns(overlayRuns, boxWidth, TextAlign.Left);
                var rightStart = Math.Min(screenWidth, xStart + boxWidth);
                var rightLen = Math.Max(0, screenWidth - rightStart);
                var rightRuns = SliceRuns(baseRuns, rightStart, rightLen);

                var composed = new List<TermRun>(leftRuns.Count + overlayRuns.Count + rightRuns.Count);
                composed.AddRange(leftRuns);
                composed.AddRange(overlayRuns);
                composed.AddRange(rightRuns);
                baseLines[lineIndex] = new TermLine(string.Empty, ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left) { Runs = composed };
            }

            // Record region for overlay (helps focus mapping, etc.)
            keyMap[overlay.Key] = new TermRegion(yStart, boxHeight);
        }

        private static List<string> WrapText(string text, int width)
        {
            var result = new List<string>();
            if (width <= 0) { result.Add(""); return result; }
            if (text == null) { result.Add(""); return result; }

            // Respect explicit newlines: wrap each paragraph independently and preserve blank lines
            var paragraphs = text.Replace("\r\n", "\n").Split('\n');

            foreach (var para in paragraphs)
            {
                var p = para ?? string.Empty;
                if (p.Length == 0)
                {
                    // Preserve empty line
                    result.Add("");
                    continue;
                }

                int idx = 0;
                while (idx < p.Length)
                {
                    int take = Math.Min(width, p.Length - idx);
                    int end = idx + take;
                    // Prefer breaking on whitespace when the chunk overflows
                    if (end < p.Length && !char.IsWhiteSpace(p[end - 1]))
                    {
                        int lastSpace = p.LastIndexOf(' ', end - 1, take);
                        if (lastSpace > idx)
                        {
                            end = lastSpace + 1; // include the space we break at
                        }
                    }

                    var segment = p.Substring(idx, end - idx).TrimEnd();
                    result.Add(segment);
                    idx = end;
                }
            }

            return result;
        }

        private static int? TryGetIntProp(IReadOnlyDictionary<UiProperty, object?> props, UiProperty key)
        {
            if (props.TryGetValue(key, out var val))
            {
                if (val is int i) return i;
                if (val is long l) return (int)l;
                if (val is string s && int.TryParse(s, out var p)) return p;
            }
            return null;
        }

        private static int ParseWidth(IReadOnlyDictionary<UiProperty, object?> props, int screenWidth)
        {
            // default 80%
            int defaultWidth = Math.Max(20, (int)(screenWidth * 0.8));
            if (!props.TryGetValue(UiProperty.Width, out var w) || w is null) return defaultWidth;
            if (w is int wi) return Math.Clamp(wi, 1, screenWidth);
            var s = w.ToString() ?? string.Empty;
            s = s.Trim();
            if (s.EndsWith("%") && int.TryParse(s.TrimEnd('%'), out var pct))
            {
                pct = Math.Clamp(pct, 1, 100);
                return Math.Max(1, (int)Math.Round(screenWidth * (pct / 100.0)));
            }
            if (int.TryParse(s, out var abs)) return Math.Clamp(abs, 1, screenWidth);
            return defaultWidth;
        }

        private static string TrimLeadingSpaces(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int i = 0;
            while (i < text.Length && text[i] == ' ') i++;
            return text.Substring(i);
        }

        private static string EnsureWidth(string text, int width)
        {
            if (text.Length < width) return text.PadRight(width);
            if (text.Length > width) return text.Substring(0, width);
            return text;
        }

        private static string FitToWidth(string text, int width)
        {
            if (text.Length <= width) return text;
            if (width <= 1) return new string('…', Math.Max(1, width));
            return text.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        // --- Runs helpers for colored segments ---
        private static List<TermRun> RunsFromLine(TermLine l)
        {
            if (l.Runs is { } r) return new List<TermRun>(r);
            return new List<TermRun> { new TermRun(l.Text ?? string.Empty, l.Foreground, l.Background) };
        }

        private static void TrimLeadingSpaces(List<TermRun> runs)
        {
            int i = 0;
            while (i < runs.Count)
            {
                var t = runs[i].Text ?? string.Empty;
                int j = 0;
                while (j < t.Length && t[j] == ' ') j++;
                if (j == 0) break;
                t = t.Substring(j);
                runs[i] = new TermRun(t, runs[i].Foreground, runs[i].Background);
                if (t.Length > 0) break;
                i++;
            }
            if (i > 0 && i < runs.Count)
            {
                runs.RemoveRange(0, i);
            }
            else if (i >= runs.Count)
            {
                runs.Clear();
            }
        }

        private static List<TermRun> ClipOrPadRuns(IReadOnlyList<TermRun> input, int width, TextAlign align)
        {
            // Compute total length
            int total = 0; foreach (var r in input) total += r.Text?.Length ?? 0;
            int leftPad = 0;
            if (align == TextAlign.Center && total < width)
            {
                leftPad = (width - total) / 2;
            }
            var result = new List<TermRun>();
            if (leftPad > 0)
                result.Add(new TermRun(new string(' ', leftPad), ConsoleColor.Gray, ConsoleColor.Black));

            int remaining = Math.Max(0, width - leftPad);
            foreach (var r in input)
            {
                if (remaining <= 0) break;
                var t = r.Text ?? string.Empty;
                if (t.Length > remaining) t = t.Substring(0, remaining);
                result.Add(new TermRun(t, r.Foreground, r.Background));
                remaining -= t.Length;
            }
            if (remaining > 0)
            {
                // pad using last background
                var last = result.Count > 0 ? result[^1] : new TermRun("", ConsoleColor.Gray, ConsoleColor.Black);
                result.Add(new TermRun(new string(' ', remaining), last.Foreground, last.Background));
            }
            return result;
        }

        private static List<TermRun> ToExactWidthRuns(TermLine line, int width)
        {
            var runs = RunsFromLine(line);
            return ClipOrPadRuns(runs, width, TextAlign.Left);
        }

        private static List<TermRun> SliceRuns(IReadOnlyList<TermRun> input, int start, int length)
        {
            var result = new List<TermRun>();
            if (length <= 0) return result;

            int pos = 0;
            int remaining = length;
            foreach (var r in input)
            {
                if (remaining <= 0) break;
                var t = r.Text ?? string.Empty;
                int runLen = t.Length;
                if (pos + runLen <= start)
                {
                    pos += runLen;
                    continue; // before window
                }
                // overlap starts here
                int localStart = Math.Max(0, start - pos);
                int take = Math.Min(remaining, Math.Max(0, runLen - localStart));
                if (take > 0)
                {
                    var slice = t.Substring(localStart, take);
                    result.Add(new TermRun(slice, r.Foreground, r.Background));
                    remaining -= take;
                }
                pos += runLen;
            }

            if (remaining > 0)
            {
                // pad with space using last known colors (or gray/black)
                var last = result.Count > 0 ? result[^1] : new TermRun("", ConsoleColor.Gray, ConsoleColor.Black);
                result.Add(new TermRun(new string(' ', remaining), last.Foreground, last.Background));
            }

            return result;
        }

        // ─── Static container renderers ──────────────────────────────────────────

        private static void RenderAccordion(TermCtx ctx, UiNode node)
        {
            var title = node.Props.TryGetValue(UiProperty.Title, out var t2) ? t2?.ToString() : "Accordion";
            var isExpanded = node.Props.TryGetValue(UiProperty.Expanded, out var exp) && exp is bool e && e;
            var accFg = ctx.IsFocused(node) ? ConsoleColor.Yellow : ConsoleColor.Cyan;
            ctx.Lines.Add(new TermLine($"{ctx.IndentStr}{(isExpanded ? "▼" : "▶")} {title}", accFg, ConsoleColor.Black, TextAlign.Left));
            if (isExpanded)
            {
                foreach (var child in node.Children)
                    ctx.LayoutChild(child, ctx.Indent + 1, ctx.Width);
            }
        }

        private static void RenderRowJustify(TermCtx ctx, UiNode node)
        {
            int startLine = ctx.Lines.Count;
            int contentWidth = Math.Max(10, ctx.Width - ctx.Indent * 2);

            static string RenderInline(UiNode c)
            {
                switch (c.Kind)
                {
                    case UiKind.Button:
                        var btnText = c.Props.TryGetValue(UiProperty.Text, out var bt) ? bt?.ToString() : "";
                        return $"[ {btnText} ]";
                    case UiKind.TextBox:
                    case UiKind.TextArea:
                        var value = c.Props.TryGetValue(UiProperty.Text, out var v) ? v?.ToString() : (c.Props.TryGetValue(UiProperty.Value, out var v2) ? v2?.ToString() : "");
                        var placeholder = c.Props.TryGetValue(UiProperty.Placeholder, out var p) ? p?.ToString() : "";
                        return string.IsNullOrEmpty(value) ? (placeholder ?? string.Empty) : value!;
                    case UiKind.ListView:
                        var itemsObj = c.Props.TryGetValue(UiProperty.Items, out var io) ? io : null;
                        var items = (itemsObj as IEnumerable<object>)?.Select(o => o?.ToString() ?? string.Empty).ToList() ?? new List<string>();
                        var selIdx = c.Props.TryGetValue(UiProperty.SelectedIndex, out var si) && si is int idx2 ? idx2 : -1;
                        string current = (selIdx >= 0 && selIdx < items.Count) ? items[selIdx] : (items.Count > 0 ? items[0] : "");
                        if (string.IsNullOrWhiteSpace(current)) current = c.Props.TryGetValue(UiProperty.Placeholder, out var pl) ? (pl?.ToString() ?? "Select…") : "Select…";
                        return $"⭥[ {current} ]";
                    case UiKind.Label:
                        return c.Props.TryGetValue(UiProperty.Text, out var lt) ? lt?.ToString() ?? string.Empty : string.Empty;
                    default:
                        return string.Empty;
                }
            }

            var leftStr  = RenderInline(node.Children[0]) ?? string.Empty;
            var rightStr = RenderInline(node.Children[1]) ?? string.Empty;

            bool leftFocused  = node.Children[0].Key == ctx.FocusedKey;
            bool rightFocused = node.Children[1].Key == ctx.FocusedKey;
            var lineFg = (leftFocused || rightFocused) ? ConsoleColor.Black : ConsoleColor.Gray;
            var lineBg = (leftFocused || rightFocused) ? ConsoleColor.White : ConsoleColor.Black;

            rightStr = FitToWidth(TrimLeadingSpaces(rightStr), Math.Max(0, contentWidth / 3));
            int availForLeft = Math.Max(0, contentWidth - rightStr.Length - 1);
            leftStr = FitToWidth(TrimLeadingSpaces(leftStr), availForLeft);
            int gap = Math.Max(1, contentWidth - leftStr.Length - rightStr.Length);

            var composed = ctx.IndentStr + leftStr + new string(' ', gap) + rightStr;
            ctx.Lines.Add(new TermLine(composed, lineFg, lineBg, TextAlign.Left));

            ctx.KeyMap[node.Key]              = new TermRegion(startLine, 1);
            ctx.KeyMap[node.Children[0].Key]  = new TermRegion(startLine, 1);
            ctx.KeyMap[node.Children[1].Key]  = new TermRegion(startLine, 1);
        }

        private static void RenderDockBottom(TermCtx ctx, UiNode node)
        {
            int startLine       = ctx.Lines.Count;
            int availableHeight = Math.Max(1, ctx.ScreenHeight - startLine);

            var topLines = new List<TermLine>();
            var topMap   = new Dictionary<string, TermRegion>();
            for (int i = 0; i < node.Children.Count - 1; i++)
                ctx.LayoutInto(node.Children[i], ctx.Indent, ctx.Width, topLines, topMap);

            var bottomLines = new List<TermLine>();
            var bottomMap   = new Dictionary<string, TermRegion>();
            ctx.LayoutInto(node.Children[^1], ctx.Indent, ctx.Width, bottomLines, bottomMap);

            int maxTopVisible = Math.Max(0, availableHeight - bottomLines.Count);

            UiNode? messagesChild = null;
            for (int i = 0; i < node.Children.Count - 1; i++)
            {
                if (node.Children[i].Key == UiFrameKeys.Messages) { messagesChild = node.Children[i]; break; }
            }
            bool autoScroll     = messagesChild != null && messagesChild.Props.TryGetValue(UiProperty.AutoScroll, out var asv) && asv is bool ab && ab;
            int requestedScroll = (messagesChild != null) ? (TryGetIntProp(messagesChild.Props, UiProperty.Min) ?? 0) : 0;
            requestedScroll     = Math.Max(0, requestedScroll);

            int totalTop  = topLines.Count;
            int maxOffset = Math.Max(0, totalTop - maxTopVisible);
            int? reqFromTop = (!autoScroll && maxTopVisible > 0)
                ? (int?)Math.Clamp(maxOffset - requestedScroll, 0, maxOffset)
                : null;
            int dropFromTop = ResolveScrollOffset(totalTop, maxTopVisible,
                autoScroll || maxTopVisible <= 0, reqFromTop, -1);

            var visibleTopLines = (dropFromTop > 0)
                ? topLines.Skip(dropFromTop).Take(maxTopVisible).ToList()
                : topLines.Take(maxTopVisible).ToList();

            int topStart = ctx.Lines.Count;
            ctx.Lines.AddRange(visibleTopLines);

            foreach (var kv in topMap)
            {
                int relStart = kv.Value.StartLine;
                int relEnd   = kv.Value.StartLine + kv.Value.LineCount;
                int newStart = Math.Min(Math.Max(0, relStart - dropFromTop), maxTopVisible);
                int newEnd   = Math.Min(Math.Max(0, relEnd   - dropFromTop), maxTopVisible);
                int len = Math.Max(0, newEnd - newStart);
                if (len > 0)
                    ctx.KeyMap[kv.Key] = new TermRegion(topStart + newStart, len);
            }

            int usedTotal = visibleTopLines.Count + bottomLines.Count;
            int filler    = Math.Max(0, availableHeight - usedTotal);
            for (int i = 0; i < filler; i++)
                ctx.Lines.Add(new TermLine("", ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left));

            var dockScroll = ComputeScrollMetrics(totalTop, maxTopVisible, maxOffset - dropFromTop);
            if (dockScroll.Overflow && maxTopVisible > 0)
            {
                for (int j = 0; j < maxTopVisible; j++)
                {
                    int idx      = topStart + j;
                    var l        = ctx.Lines[idx];
                    var baseText = l.Text ?? string.Empty;
                    var ensured  = baseText.Length < ctx.Width ? baseText.PadRight(ctx.Width)
                                 : baseText.Length > ctx.Width ? baseText.Substring(0, ctx.Width) : baseText;
                    char sb = ScrollbarGlyph(j, dockScroll);
                    if (ensured.Length > 0)
                        ensured = ensured.Substring(0, Math.Max(0, ctx.Width - 1)) + sb;
                    ctx.Lines[idx] = new TermLine(ensured, l.Foreground, l.Background, l.Align);
                }
            }

            int bottomStart = ctx.Lines.Count;
            ctx.Lines.AddRange(bottomLines);
            foreach (var kv in bottomMap)
                ctx.KeyMap[kv.Key] = new TermRegion(bottomStart + kv.Value.StartLine, kv.Value.LineCount);

            ctx.KeyMap[node.Key] = new TermRegion(startLine, Math.Max(1, ctx.Lines.Count - startLine));
        }

        private static void RenderGrid(TermCtx ctx, UiNode node)
        {
            int startLine    = ctx.Lines.Count;
            int contentWidth = Math.Max(10, ctx.Width - ctx.Indent * 2);

            var colsObj = node.Props.TryGetValue(UiProperty.Columns, out var co) ? co : null;
            var specs   = new List<(double val, bool isPercent, bool isFr)>();
            if (colsObj is GridColumns gc)
            {
                foreach (var col in gc.Columns)
                {
                    if (col.Kind == GridUnitKind.Percent) specs.Add((col.Value, true, false));
                    else specs.Add((col.Value, false, true));
                }
            }
            if (specs.Count == 0) { specs.Add((50, true, false)); specs.Add((50, true, false)); }

            int fixedPx = 0; double frTotal = 0;
            foreach (var (val, isPercent, isFr) in specs)
            {
                if (isPercent) fixedPx += (int)Math.Round(contentWidth * (val / 100.0));
                else if (isFr) frTotal += Math.Max(0.0001, val);
            }
            int rem = Math.Max(0, contentWidth - fixedPx);
            var widths = new List<int>(specs.Count);
            foreach (var (val, isPercent, isFr) in specs)
            {
                if (isPercent) widths.Add((int)Math.Round(contentWidth * (val / 100.0)));
                else if (isFr)  widths.Add(frTotal > 0 ? (int)Math.Round(rem * (val / frTotal)) : 0);
                else            widths.Add((int)Math.Round(val));
            }

            var childLines = new List<List<TermLine>>();
            var childMaps  = new List<Dictionary<string, TermRegion>>();
            for (int i = 0; i < Math.Min(node.Children.Count, widths.Count); i++)
            {
                var list = new List<TermLine>();
                var map  = new Dictionary<string, TermRegion>();
                ctx.LayoutInto(node.Children[i], 0, widths[i], list, map);
                childLines.Add(list);
                childMaps.Add(map);
            }

            int maxRows = childLines.Max(l => l.Count);
            for (int r = 0; r < maxRows; r++)
            {
                var runs = new List<TermRun>();
                if (ctx.Indent > 0)
                    runs.Add(new TermRun(new string(' ', ctx.Indent * 2), ConsoleColor.Gray, ConsoleColor.Black));

                for (int colIdx = 0; colIdx < childLines.Count; colIdx++)
                {
                    var childLine    = r < childLines[colIdx].Count ? childLines[colIdx][r] : new TermLine("", ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left);
                    bool isButtonCell = node.Children[colIdx].Kind == UiKind.Button;
                    var marker       = "  ";
                    int inner        = Math.Max(1, widths[colIdx] - marker.Length);

                    if (isButtonCell)
                    {
                        var childRuns = RunsFromLine(childLine);
                        TrimLeadingSpaces(childRuns);
                        int total   = 0; foreach (var rr in childRuns) total += rr.Text?.Length ?? 0;
                        total       = Math.Min(total, Math.Max(1, inner));
                        int leftPad  = Math.Max(0, (inner - total) / 2);
                        int rightPad = Math.Max(0, inner - leftPad - total);
                        if (marker.Length > 0) runs.Add(new TermRun(marker, ConsoleColor.Gray, ConsoleColor.Black));
                        if (leftPad  > 0) runs.Add(new TermRun(new string(' ', leftPad),  ConsoleColor.Gray, ConsoleColor.Black));
                        runs.AddRange(ClipOrPadRuns(childRuns, total, TextAlign.Left));
                        if (rightPad > 0) runs.Add(new TermRun(new string(' ', rightPad), ConsoleColor.Gray, ConsoleColor.Black));
                    }
                    else
                    {
                        var childRuns = RunsFromLine(childLine);
                        TrimLeadingSpaces(childRuns);
                        var clipped = ClipOrPadRuns(childRuns, inner, childLine.Align);
                        runs.Add(new TermRun(marker, childLine.Foreground, childLine.Background));
                        runs.AddRange(clipped);
                    }
                }
                ctx.Lines.Add(new TermLine(string.Empty, ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left) { Runs = runs });
            }

            int rowLen = Math.Max(1, maxRows);
            ctx.KeyMap[node.Key] = new TermRegion(startLine, rowLen);
            for (int i = 0; i < Math.Min(node.Children.Count, widths.Count); i++)
                ctx.KeyMap[node.Children[i].Key] = new TermRegion(startLine, rowLen);
            for (int i = 0; i < childMaps.Count; i++)
            {
                foreach (var kv in childMaps[i])
                {
                    int newStart = startLine + Math.Max(0, Math.Min(maxRows, kv.Value.StartLine));
                    int newLen   = Math.Max(0, Math.Min(kv.Value.LineCount, Math.Max(0, startLine + maxRows - newStart)));
                    if (newLen > 0)
                        ctx.KeyMap[kv.Key] = new TermRegion(newStart, newLen);
                }
            }
        }

        private static void RenderColumn(TermCtx ctx, UiNode node)
        {
            if (node.Children.Count >= 1 &&
                node.Props.TryGetValue(UiProperty.Layout, out var layoutCol) &&
                string.Equals(layoutCol?.ToString(), "dock-bottom", StringComparison.OrdinalIgnoreCase))
            {
                RenderDockBottom(ctx, node);
            }
            else if (node.Props.TryGetValue(UiProperty.Layout, out var gl) &&
                     string.Equals(gl?.ToString(), "grid", StringComparison.OrdinalIgnoreCase) &&
                     node.Props.TryGetValue(UiProperty.Columns, out _) && node.Children.Count > 0)
            {
                RenderGrid(ctx, node);
            }
            else
            {
                foreach (var child in node.Children)
                    ctx.LayoutChild(child, ctx.Indent, ctx.Width);
            }
        }

        private static void RenderRow(TermCtx ctx, UiNode node)
        {
            if (node.Children.Count == 2 &&
                node.Props.TryGetValue(UiProperty.Layout, out var layoutRow) &&
                string.Equals(layoutRow?.ToString(), "row-justify", StringComparison.OrdinalIgnoreCase))
            {
                RenderRowJustify(ctx, node);
            }
            else if (node.Props.TryGetValue(UiProperty.Layout, out var gl) &&
                     string.Equals(gl?.ToString(), "grid", StringComparison.OrdinalIgnoreCase) &&
                     node.Props.TryGetValue(UiProperty.Columns, out _) && node.Children.Count > 0)
            {
                RenderGrid(ctx, node);
            }
            else
            {
                foreach (var child in node.Children)
                    ctx.LayoutChild(child, ctx.Indent + 1, ctx.Width);
            }
        }

        // ─── Box assembly helper ─────────────────────────────────────────────────

        /// <summary>
        /// Assembles the run-lines for a bordered overlay box: top border, pinned header rows,
        /// scrollable body rows (with optional scrollbar on the right border), pinned footer rows,
        /// and bottom border.
        /// </summary>
        private static List<List<TermRun>> BuildBoxRunLines(
            IReadOnlyList<TermLine> innerLines, int contentWidth,
            int headerVisible, int bodyStart, int bodyLen, int bodyVisible, int footerVisible,
            int offset, ScrollMetrics scroll,
            ConsoleColor borderFg, ConsoleColor borderBg)
        {
            int totalContent = innerLines.Count;
            var result = new List<List<TermRun>>();

            // Top border
            result.Add(new List<TermRun> { new TermRun("┌" + new string('─', contentWidth) + "┐", borderFg, borderBg) });

            // Header rows (pinned, from start of innerLines)
            for (int i = 0; i < headerVisible; i++)
            {
                var il = innerLines[i];
                var ilRuns = RunsFromLine(il);
                TrimLeadingSpaces(ilRuns);
                var clipped = ClipOrPadRuns(ilRuns, contentWidth, il.Align);
                var rowRuns = new List<TermRun>();
                rowRuns.Add(new TermRun("│", borderFg, borderBg));
                rowRuns.AddRange(clipped);
                rowRuns.Add(new TermRun("│", borderFg, borderBg));
                result.Add(rowRuns);
            }

            // Body rows (scrollable)
            for (int j = 0; j < bodyVisible; j++)
            {
                int contentIndex = Math.Min(totalContent - 1, bodyStart + Math.Min(Math.Max(0, bodyLen - 1), offset + j));
                var il = innerLines[contentIndex];
                var ilRuns = RunsFromLine(il);
                TrimLeadingSpaces(ilRuns);
                var clipped = ClipOrPadRuns(ilRuns, contentWidth, il.Align);
                var rowRuns = new List<TermRun>();
                rowRuns.Add(new TermRun("│", borderFg, borderBg));
                rowRuns.AddRange(clipped);
                // Right border doubles as scrollbar track for body rows when overflowing
                char rightGlyph = scroll.Overflow ? ScrollbarGlyph(j, scroll) : '│';
                rowRuns.Add(new TermRun(rightGlyph.ToString(), borderFg, borderBg));
                result.Add(rowRuns);
            }

            // Footer rows (pinned, after body in innerLines)
            int footerStart = bodyStart + bodyLen;
            for (int k = 0; k < footerVisible; k++)
            {
                int idx = footerStart + k;
                if (idx < 0 || idx >= totalContent) break;
                var il = innerLines[idx];
                var ilRuns = RunsFromLine(il);
                TrimLeadingSpaces(ilRuns);
                var clipped = ClipOrPadRuns(ilRuns, contentWidth, il.Align);
                var rowRuns = new List<TermRun>();
                rowRuns.Add(new TermRun("│", borderFg, borderBg));
                rowRuns.AddRange(clipped);
                rowRuns.Add(new TermRun("│", borderFg, borderBg));
                result.Add(rowRuns);
            }

            // Bottom border
            result.Add(new List<TermRun> { new TermRun("└" + new string('─', contentWidth) + "┘", borderFg, borderBg) });

            return result;
        }

        // ─── Shared scroll primitives ─────────────────────────────────────────────

        private readonly struct ScrollMetrics
        {
            public bool Overflow { get; init; }
            public int  ThumbTop { get; init; }
            public int  ThumbH   { get; init; }
        }

        /// <summary>Computes scrollbar thumb position and size given total/visible item counts and the current offset (from top).</summary>
        private static ScrollMetrics ComputeScrollMetrics(int totalItems, int visibleItems, int offset)
        {
            if (totalItems <= visibleItems)
                return new ScrollMetrics { Overflow = false, ThumbTop = 0, ThumbH = 1 };

            int trackH      = visibleItems;
            int thumbH      = Math.Max(1, (int)Math.Round(trackH * (visibleItems / (double)Math.Max(1, totalItems))));
            thumbH          = Math.Min(thumbH, trackH);
            int maxThumbTop = Math.Max(0, trackH - thumbH);
            int scrollRange = Math.Max(1, totalItems - visibleItems);
            int thumbTop    = (int)Math.Round(offset / (double)scrollRange * maxThumbTop);
            thumbTop        = Math.Clamp(thumbTop, 0, maxThumbTop);
            return new ScrollMetrics { Overflow = true, ThumbTop = thumbTop, ThumbH = thumbH };
        }

        /// <summary>Returns '█' for thumb rows and '│' for track rows.</summary>
        private static char ScrollbarGlyph(int rowIndex, ScrollMetrics m)
            => (rowIndex >= m.ThumbTop && rowIndex < m.ThumbTop + m.ThumbH) ? '█' : '│';

        /// <summary>
        /// Returns the scroll offset (from-top, 0 = top) given the scroll state.
        /// autoScroll=true → pin to bottom.
        /// requestedOffset provided → use it, then center focus if outside visible window.
        /// requestedOffset null → default to top (0), then center focus if applicable.
        /// focusedRelativeRow &lt; 0 disables focus-centering.
        /// </summary>
        private static int ResolveScrollOffset(int totalRows, int visibleRows,
            bool autoScroll, int? requestedOffset, int focusedRelativeRow)
        {
            int maxOffset = Math.Max(0, totalRows - visibleRows);

            if (autoScroll)
                return maxOffset;   // pin to bottom; ignore focus when streaming

            int offset = requestedOffset.HasValue ? Math.Clamp(requestedOffset.Value, 0, maxOffset) : 0;

            if (focusedRelativeRow >= 0 && maxOffset > 0)
            {
                int visEnd = offset + visibleRows - 1;
                if (focusedRelativeRow < offset || focusedRelativeRow > visEnd)
                {
                    int centered = focusedRelativeRow - visibleRows / 2;
                    offset = Math.Clamp(centered, 0, maxOffset);
                }
            }

            return offset;
        }
    }

    /// <summary>
    /// Snapshot of the terminal state at a point in time
    /// </summary>
    private sealed record TermSnapshot(
        IReadOnlyList<TermLine> Lines,
        IReadOnlyDictionary<string, TermRegion> KeyMap
    );

    /// <summary>
    /// Alignment for a rendered terminal line
    /// </summary>
    private enum TextAlign { Left, Center }

    /// <summary>
    /// A single line in the terminal with styling and alignment metadata
    /// </summary>
    private sealed record TermRun(string Text, ConsoleColor Foreground, ConsoleColor Background);

    private sealed record TermLine(
        string Text,
        ConsoleColor Foreground,
        ConsoleColor Background,
        TextAlign Align
    )
    {
        // Optional segmented runs for per-span coloring. When set, Apply() uses Runs instead of Text/Foreground/Background.
        public IReadOnlyList<TermRun>? Runs { get; init; }
    }

    /// <summary>
    /// Region occupied by a UiNode in the terminal
    /// </summary>
    private sealed record TermRegion(
        int StartLine,
        int LineCount
    );

    /// <summary>
    /// An edit operation to apply to the terminal
    /// </summary>
    private sealed record TermEdit(
        int LineIndex,
        TermLine Line
    );
}

/// <summary>
/// Terminal implementation of IInputRouter
/// Maps key events to unified input submission (Enter submits, Shift+Enter inserts newline)
/// Integrates with UiNode system by updating the "input" node props as user types
/// </summary>
public sealed class TerminalInputRouter : IInputRouter
{
    /// <summary>
    /// Non-blocking poll for key input. Returns ConsoleKeyInfo if a key is available, otherwise null.
    /// </summary>
    public ConsoleKeyInfo? TryReadKey()
    {
        if (Console.KeyAvailable)
            return Console.ReadKey(intercept: true);
        return null;
    }
}