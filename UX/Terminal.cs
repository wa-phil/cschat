using System;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Terminal : CUiBase
{
    private TerminalInputRouter? _inputRouter;

    public sealed class Progress
    {
        public enum ProgressState { Queued, Running, Failed, Canceled, Completed }

        private static void WriteFullWidth(string s, ConsoleColor fg)
        {
            int width = Math.Max(10, Program.ui.Width - 1);
            Program.ui.ForegroundColor = fg;
            if (s.Length > width) s = s.Substring(0, width);
            if (s.Length < width) s = s.PadRight(width);
            Program.ui.WriteLine(s);
            Program.ui.ResetColor();
        }

        private static string CenterOrTrim(string s, int innerWidth)
        {
            if (innerWidth <= 0) return string.Empty;
            if (s.Length > innerWidth) s = s.Substring(0, Math.Max(0, innerWidth - 3)) + "...";
            int pad = innerWidth - s.Length;
            int left = pad / 2;
            int right = pad - left;
            return new string(' ', left) + s + new string(' ', right);
        }

        private static string ComposeLeftRight(string left, string right, int width)
        {
            // Ensure the right label fits; truncate left with ellipsis if needed
            int rightLen = right.Length;
            int availLeft = Math.Max(0, width - rightLen - 1); // at least one space gap
            if (left.Length > availLeft)
            {
                // leave room for ellipsis
                int keep = Math.Max(0, availLeft - 3);
                left = (keep > 0 ? left.Substring(0, keep) : "") + (availLeft > 0 ? "..." : "");
            }
            int pad = Math.Max(1, width - left.Length - rightLen);
            return left + new string(' ', pad) + right;
        }

        public static void DrawBoxedHeader(string text)
        {
            int width = Math.Max(10, Program.ui.Width - 1);
            Program.ui.SetCursorPosition(0, 0);

            // Top border
            var top = "┌" + new string('─', Math.Max(0, width - 2)) + "┐";
            WriteFullWidth(top, ConsoleColor.DarkGray);

            // Middle line: │  title  │  (borders dark gray, title green)
            int inner = Math.Max(0, width - 2);
            var centered = CenterOrTrim(text, inner);

            // left border
            Program.ui.ForegroundColor = ConsoleColor.DarkGray;
            Program.ui.Write("│");
            // title area
            Program.ui.ForegroundColor = ConsoleColor.Green;
            if (centered.Length < inner) centered = centered.PadRight(inner);
            if (centered.Length > inner) centered = centered.Substring(0, inner);
            Program.ui.Write(centered);
            // right border
            Program.ui.ForegroundColor = ConsoleColor.DarkGray;
            Program.ui.WriteLine("│");
            Program.ui.ResetColor();

            // Separator
            var sep = "├" + new string('─', Math.Max(0, width - 2)) + "┤";
            WriteFullWidth(sep, ConsoleColor.DarkGray);
        }

        public static void DrawProgressRow(string name, double percent, ProgressState state, string? note, int done, int total)
        {
            int width = Math.Max(10, Program.ui.Width - 1);
            int row = Program.ui.CursorTop;

            var glyph = state switch
            {
                ProgressState.Running => "▶",
                ProgressState.Completed => "✓",
                ProgressState.Failed => "✖",
                ProgressState.Canceled => "■",
                _ => "•"
            };

            string left = $"{glyph} {name}";
            string right = total > 0
                ? $"{percent,6:0.0}% ({done}/{total})"
                : $"{percent,6:0.0}%";

            string line = ComposeLeftRight(left, right, width);

            var barBack = state switch
            {
                ProgressState.Failed => ConsoleColor.DarkRed,
                ProgressState.Canceled => ConsoleColor.DarkGray,
                ProgressState.Completed => ConsoleColor.DarkGray,
                ProgressState.Running => ConsoleColor.DarkGray,
                _ => ConsoleColor.DarkBlue,
            };

            var fore = state switch
            {
                ProgressState.Failed => ConsoleColor.Yellow,
                ProgressState.Canceled => ConsoleColor.Gray,
                _ => ConsoleColor.Gray
            };

            int fill = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * width);

            // Draw the two segments so the background fill remains visible "behind" the text.
            Program.ui.SetCursorPosition(0, row);

            // segment 1 (within fill)
            var seg1 = line.Substring(0, Math.Min(fill, line.Length));
            Program.ui.BackgroundColor = barBack;
            Program.ui.ForegroundColor = fore;
            Program.ui.Write(seg1);

            // segment 2 (rest of line)
            var seg2Len = Math.Max(0, width - seg1.Length);
            var seg2 = seg1.Length < line.Length ? line.Substring(seg1.Length) : new string(' ', seg2Len);
            Program.ui.BackgroundColor = ConsoleColor.Black;
            Program.ui.ForegroundColor = fore;
            Program.ui.Write(seg2.PadRight(seg2Len));

            Program.ui.ResetColor();

            // Move to next line for the caller
            Program.ui.SetCursorPosition(0, row + 1);
        }

        public static void DrawFooterStats(int running, int queued, int completed, int failed, int canceled, string hint)
        {
            int width = Math.Max(10, Program.ui.Width - 1);

            string stats = $"in-flight: {running}   queued: {queued}   completed: {completed}   failed: {failed}   canceled: {canceled}";
            WriteFullWidth(stats, ConsoleColor.DarkGray);
            WriteFullWidth(hint, ConsoleColor.DarkGray);
        }
    }

    public override Task<bool> ConfirmAsync(string question, bool defaultAnswer = false)
    {
        Write($"{question} {(defaultAnswer ? "[Y/n]" : "[y/N]")} ");
        while (true)
        {
            var input = Console.ReadLine() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return Task.FromResult(defaultAnswer);
            input = input.Trim().ToLowerInvariant();
            if (input == "y" || input == "yes") return Task.FromResult(true);
            if (input == "n" || input == "no") return Task.FromResult(false);
            Write("Please enter y or n: ");
        }
    }

    public override async Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt)
    {
        var path = await ReadPathWithAutocompleteAsync(false);

        if (string.IsNullOrWhiteSpace(path) || (opt.Mode != PathPickerMode.SaveFile && !System.IO.File.Exists(path)))
            return Array.Empty<string>();

        return new List<string> { path };
    }

    private sealed class TermProg
    {
        public string Title = "";
        public int RegionTop = 0;
        public int RegionHeight = 3 + Program.config.MaxMenuItems + 2;
        public int LastWidth;
        public CancellationTokenSource Cts = new();
    }

    private readonly Dictionary<string, TermProg> _progress = new();

    public override string StartProgress(string title, CancellationTokenSource cts)
    {
        var id = Guid.NewGuid().ToString("n");
        var tp = new TermProg { Title = title, Cts = cts, LastWidth = Math.Max(10, Width - 1) };
        _progress[id] = tp;

        // claim the top region & paint header once
        SetCursorPosition(0, tp.RegionTop);
        Progress.DrawBoxedHeader(title);
        // leave rows + footer area blank
        for (int i = 0; i < Program.config.MaxMenuItems + 2; i++) WriteLine(new string(' ', tp.LastWidth));
        SetCursorPosition(0, tp.RegionTop);
        return id;
    }

    public override void UpdateProgress(string id, ProgressSnapshot s)
    {
        if (!_progress.TryGetValue(id, out var tp)) return;

        // ESC cancels
        while (KeyAvailable)
        {
            var key = ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape) { try { tp.Cts.Cancel(); } catch { } }
        }

        // width changes -> clear region
        int width = Math.Max(10, Width - 1);
        if (width != tp.LastWidth)
        {
            SetCursorPosition(0, tp.RegionTop);
            for (int i = 0; i < tp.RegionHeight; i++)
            {
                Write(new string(' ', width));
                if (i < tp.RegionHeight - 1) WriteLine();
            }
            tp.LastWidth = width;
        }

        // paint at region top
        SetCursorPosition(0, tp.RegionTop);
        Progress.DrawBoxedHeader(tp.Title);

        // Show only *active* work. Keep failures visible; hide completed/canceled.
        var topN = Program.config.MaxMenuItems;
        static int Rank(ProgressState s) => s switch
        {
            ProgressState.Running => 3,
            ProgressState.Queued => 2,
            ProgressState.Failed => 1,   // keep failed items visible
            ProgressState.Canceled => 0,   // hide from main list (shown in footer stats)
            ProgressState.Completed => -1,  // hide from main list
            _ => 0
        };

        var rows = s.Items
            .Where(x => x.state != ProgressState.Completed && x.state != ProgressState.Canceled)
            .OrderByDescending(x => Rank(x.state)) // Running > Queued > Failed
            .ThenBy(x => x.percent)                // least progress first (more informative)
            .ThenBy(x => x.name)
            .Take(topN)
            .ToList();

        foreach (var r in rows)
        {
            var apState = r.state switch
            {
                ProgressState.Running => Progress.ProgressState.Running,
                ProgressState.Queued => Progress.ProgressState.Queued,
                ProgressState.Completed => Progress.ProgressState.Completed,
                ProgressState.Failed => Progress.ProgressState.Failed,
                ProgressState.Canceled => Progress.ProgressState.Canceled,
                _ => Progress.ProgressState.Queued
            };
            Progress.DrawProgressRow(r.name, r.percent, apState, r.note, r.steps.done, r.steps.total);
        }

        // clear leftover rows
        for (int i = rows.Count; i < topN; i++)
        {
            WriteLine(new string(' ', width));
        }

        var hint = "Press ESC to cancel";
        Progress.DrawFooterStats(s.Stats.running, s.Stats.queued, s.Stats.completed, s.Stats.failed, s.Stats.canceled, (s.EtaHint is { } eta ? $"ETA: {eta} • " : "") + hint);

        // park cursor at top so we never scroll the buffer
        SetCursorPosition(0, tp.RegionTop);
    }

    public override void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown)
    {
        if (_progress.Remove(id, out var tp))
        {
            // clear region and print the artifact as a Tool message
            SetCursorPosition(0, tp.RegionTop);
            for (int i = 0; i < tp.RegionHeight; i++)
            {
                Write(new string(' ', tp.LastWidth));
                if (i < tp.RegionHeight - 1) WriteLine();
            }
            SetCursorPosition(0, tp.RegionTop);
        }

        // Display the same summary that was persisted to Context by Progress.cs
        RenderChatMessage(new ChatMessage { Role = Roles.Tool, Content = artifactMarkdown });
    }

    private async Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory)
    {
        await Task.CompletedTask; // Simulate asynchronous behavior
        var buffer = new List<char>();
        while (true)
        {
            var key = ReadKey(intercept: true);

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
        return string.IsNullOrWhiteSpace(result) ? null : Path.GetFullPath(result);
    }

    public override IInputRouter GetInputRouter()
    {
        if (_inputRouter == null)
        {
            _inputRouter = new TerminalInputRouter();
        }
        return _inputRouter;
    }

    public override void RenderTable(Table table, string? title = null)
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
        RenderChatMessage(message);
    }

    public override void RenderReport(Report report)
    {
        var width = Math.Max(20, Width - 1);
        var text = report?.ToPlainText(width) ?? "";
        var msg = new ChatMessage { Role = Roles.Tool, Content = text };
        Program.Context.AddToolMessage(text);
        RenderChatMessage(msg);
    }

    private sealed class TermRealtimeWriter : IRealtimeWriter
    {
        public void Write(string text) => Console.Write(text ?? "");
        public void WriteLine(string? text = null) => Console.WriteLine(text);
        public void Dispose() { /* no-op */ }
    }

    public override IRealtimeWriter BeginRealtime(string title)
    {
        // Give a small heading so it's visible, then pass-through
        if (!string.IsNullOrWhiteSpace(title))
        {
            WriteLine(title);
            WriteLine(new string('-', Math.Min(title.Length, Math.Max(10, Width - 1))));
        }
        return new TermRealtimeWriter();
    }

    // Renders a menu at the current cursor position, allows arrow key navigation, and returns the selected string or null if cancelled
    public override string? RenderMenu(string header, List<string> choices, int selected = 0)
    {
        // Use MenuOverlay for UiNode-based menu rendering
        // This is a synchronous wrapper around the async ShowAsync method
        return MenuOverlay.ShowAsync(this, header, choices, selected).GetAwaiter().GetResult();
    }

    public override ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);

    public override void RenderChatMessage(ChatMessage message)
    {
        // Use ChatSurface to render the message via patch
        // Get current message count to determine the index
        var currentMessages = Program.Context?.Messages(InluceSystemMessage: false).ToList() ?? new List<ChatMessage>();
        var index = currentMessages.Count > 0 ? currentMessages.Count - 1 : 0;
        
        // Apply patch to append the message
        var patch = ChatSurface.AppendMessage(message, index);
        PatchAsync(patch).GetAwaiter().GetResult();
    }

    public override void RenderChatHistory(IEnumerable<ChatMessage> messages)
    {
        // Use ChatSurface to render all messages via patch
        var messageList = messages.ToList();
        
        // Apply patch to update all messages
        var patch = ChatSurface.UpdateMessages(messageList);
        PatchAsync(patch).GetAwaiter().GetResult();
    }

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

    public override Task RunAsync(Func<Task> appMain) => appMain();

    // Declarative control layer - override base implementations for Terminal-specific rendering
    private readonly TermDom _termDom = new();
    private TermSnapshot? _lastSnapshot;

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

    protected override Task PostPatchAsync(UiPatch patch)
    {
        // Use incremental rendering if enabled, otherwise fall back to full re-render
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

        return Task.CompletedTask;
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
    /// Renders a UiNode and its children to the terminal
    /// </summary>
    private void RenderNode(UiNode node, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        var isFocused = node.Key == _uiTree.FocusedKey;

        switch (node.Kind)
        {
            case UiKind.Column:
            case UiKind.Row:
                // Layout containers - just render children
                foreach (var child in node.Children)
                {
                    RenderNode(child, node.Kind == UiKind.Column ? indent : indent + 1);
                }
                break;

            case UiKind.Label:
                if (node.Props.TryGetValue(UiProperty.Text, out var labelText))
                {
                    ForegroundColor = node.Props.TryGetValue(UiProperty.Color, out var color) && color is ConsoleColor cc
                        ? cc
                        : ConsoleColor.Gray;
                    WriteLine($"{indentStr}{labelText}");
                    ResetColor();
                }
                break;

            case UiKind.Button:
                if (node.Props.TryGetValue(UiProperty.Text, out var btnText))
                {
                    if (isFocused)
                    {
                        ForegroundColor = ConsoleColor.Black;
                        BackgroundColor = ConsoleColor.White;
                    }
                    else
                    {
                        ForegroundColor = ConsoleColor.Cyan;
                    }
                    WriteLine($"{indentStr}[{btnText}]");
                    ResetColor();
                }
                break;

            case UiKind.TextBox:
            case UiKind.TextArea:
                var text = node.Props.TryGetValue(UiProperty.Text, out var t) ? t?.ToString() : "";
                var placeholder = node.Props.TryGetValue(UiProperty.Placeholder, out var ph) ? ph?.ToString() : "";
                var displayText = string.IsNullOrEmpty(text) ? $"({placeholder})" : text;

                if (isFocused)
                {
                    ForegroundColor = ConsoleColor.Black;
                    BackgroundColor = ConsoleColor.White;
                }
                else
                {
                    ForegroundColor = ConsoleColor.Gray;
                }
                WriteLine($"{indentStr}{displayText}");
                ResetColor();
                break;

            case UiKind.CheckBox:
            case UiKind.Toggle:
                var isChecked = node.Props.TryGetValue(UiProperty.Checked, out var chk) && chk is bool b && b;
                var cbLabel = node.Props.TryGetValue(UiProperty.Label, out var lbl) ? lbl?.ToString() : "";
                var checkbox = isChecked ? "[X]" : "[ ]";

                if (isFocused)
                {
                    ForegroundColor = ConsoleColor.Black;
                    BackgroundColor = ConsoleColor.White;
                }
                WriteLine($"{indentStr}{checkbox} {cbLabel}");
                ResetColor();
                break;

            case UiKind.ListView:
                if (node.Props.TryGetValue(UiProperty.Items, out var itemsObj) && itemsObj is IEnumerable<object> items)
                {
                    var selectedIndex = node.Props.TryGetValue(UiProperty.SelectedIndex, out var si) && si is int idx ? idx : -1;
                    var itemList = items.ToList();

                    for (int i = 0; i < itemList.Count; i++)
                    {
                        var isSelected = i == selectedIndex;
                        if (isSelected || isFocused)
                        {
                            ForegroundColor = ConsoleColor.Black;
                            BackgroundColor = ConsoleColor.White;
                        }
                        WriteLine($"{indentStr}  {(isSelected ? ">" : " ")} {itemList[i]}");
                        ResetColor();
                    }
                }
                break;

            case UiKind.Html:
                // Render as plain text in terminal
                if (node.Props.TryGetValue(UiProperty.Content, out var htmlContent))
                {
                    WriteLine($"{indentStr}{htmlContent}");
                }
                break;

            case UiKind.Spacer:
                var height = node.Props.TryGetValue(UiProperty.Height, out var h) && h is int ht ? ht : 1;
                for (int i = 0; i < height; i++)
                {
                    WriteLine();
                }
                break;

            case UiKind.Accordion:
                var title = node.Props.TryGetValue(UiProperty.Title, out var t2) ? t2?.ToString() : "Accordion";
                var isExpanded = node.Props.TryGetValue(UiProperty.Expanded, out var exp) && exp is bool e && e;

                ForegroundColor = isFocused ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                WriteLine($"{indentStr}{(isExpanded ? "▼" : "▶")} {title}");
                ResetColor();

                if (isExpanded)
                {
                    foreach (var child in node.Children)
                    {
                        RenderNode(child, indent + 1);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Terminal Virtual-DOM for incremental rendering
    /// Flattens UiNode -> lines with attributes, maintains key→region map, computes minimal edits
    /// </summary>
    private sealed class TermDom
    {
        /// <summary>
        /// Flattens UiNode -> lines with attributes (fg/bg), maintains key→region map
        /// </summary>
        public TermSnapshot Layout(UiNode root, int width, int screenHeight, string? focusedKey)
        {
            var lines = new List<TermLine>();
            var keyMap = new Dictionary<string, TermRegion>();

            // Base layout (header + content). Skip overlays container; we'll composite overlays later.
            UiNode? overlaysContainer = null;
            if (root.Key == "frame.root")
            {
                foreach (var child in root.Children)
                {
                    if (child.Key == "frame.overlays")
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
                // Sort by zIndex if present, stable otherwise.
                IEnumerable<UiNode> overlayNodes = overlaysContainer.Children;
                overlayNodes = overlayNodes
                    .Select(n => (n, z: TryGetIntProp(n.Props, UiProperty.ZIndex) ?? 0))
                    .OrderBy(t => t.z)
                    .Select(t => t.n);

                foreach (var overlay in overlayNodes)
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
                else if (oldLine != null && newLine != null && !oldLine.Equals(newLine))
                {
                    // Line changed
                    edits.Add(new TermEdit(i, newLine));
                }
            }

            return edits;
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
                Console.ForegroundColor = edit.Line.Foreground;
                Console.BackgroundColor = edit.Line.Background;

                // Pad or truncate to console width
                var text = edit.Line.Text;
                var width = Console.WindowWidth;
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
            var indentStr = new string(' ', indent * 2);
            var isFocused = node.Key == focusedKey;

            // Helpers to resolve styles with legacy fallback
            static bool TryParseColor(object? val, out ConsoleColor color)
            {
                if (val is ConsoleColor cc) { color = cc; return true; }
                if (val is string s && Enum.TryParse<ConsoleColor>(s, true, out var parsed)) { color = parsed; return true; }
                color = ConsoleColor.Gray; return false;
            }

            ConsoleColor ResolveFg(UiNode n, ConsoleColor @default)
            {
                if (n.Styles.Get<object?>(UiStyleKey.ForegroundColor) is object st && TryParseColor(st, out var c)) return c;
                if (n.Props.TryGetValue(UiProperty.Color, out var legacy) && TryParseColor(legacy, out var c2)) return c2;
                // Bold hint if no explicit color
                var boldObj = n.Styles.Get<object?>(UiStyleKey.Bold);
                if (boldObj is bool isBold && isBold) return ConsoleColor.White;
                // simple style hint: dim
                var styleStr = n.Styles.Get<string>(UiStyleKey.Style) ?? (n.Props.TryGetValue(UiProperty.Style, out var ls) ? ls as string : null);
                if (string.Equals(styleStr, "dim", StringComparison.OrdinalIgnoreCase)) return ConsoleColor.DarkGray;
                return @default;
            }

            ConsoleColor ResolveBg(UiNode n, ConsoleColor @default)
            {
                if (n.Styles.Get<object?>(UiStyleKey.BackgroundColor) is object st && TryParseColor(st, out var c)) return c;
                return @default;
            }

            TextAlign ResolveAlign(UiNode n)
            {
                var a = n.Styles.Get<string>(UiStyleKey.Align) ?? (n.Props.TryGetValue(UiProperty.Align, out var la) ? la?.ToString() : null);
                return string.Equals(a, "center", StringComparison.OrdinalIgnoreCase) ? TextAlign.Center : TextAlign.Left;
            }

            bool ResolveWrap(UiNode n)
            {
                var sv = n.Styles.Get<object?>(UiStyleKey.Wrap);
                if (sv is bool b) return b;
                if (n.Props.TryGetValue(UiProperty.Wrap, out var lv) && lv is bool lb) return lb;
                return false;
            }

            switch (node.Kind)
            {
                case UiKind.Column:
                case UiKind.Row:
                    // Support special row layout that composes two children inline with right child right-aligned.
                    if (node.Kind == UiKind.Row &&
                        node.Children.Count == 2 &&
                        node.Props.TryGetValue(UiProperty.Layout, out var layoutRow) &&
                        string.Equals(layoutRow?.ToString(), "row-justify", StringComparison.OrdinalIgnoreCase))
                    {
                        int contentWidth = Math.Max(10, width - indent * 2);

                        // Helper to render a compact, single-line representation of a child control
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
                                case UiKind.Label:
                                    return c.Props.TryGetValue(UiProperty.Text, out var lt) ? lt?.ToString() ?? string.Empty : string.Empty;
                                default:
                                    return string.Empty;
                            }
                        }

                        var leftStr = RenderInline(node.Children[0]) ?? string.Empty;
                        var rightStr = RenderInline(node.Children[1]) ?? string.Empty;

                        // Determine focus coloring: if any child focused, invert like other controls
                        bool leftFocused = node.Children[0].Key == focusedKey;
                        bool rightFocused = node.Children[1].Key == focusedKey;
                        var lineFg = (leftFocused || rightFocused) ? ConsoleColor.Black : ConsoleColor.Gray;
                        var lineBg = (leftFocused || rightFocused) ? ConsoleColor.White : ConsoleColor.Black;

                        // Fit right segment first, then allocate remainder to left
                        rightStr = FitToWidth(TrimLeadingSpaces(rightStr), Math.Max(0, contentWidth / 3)); // clamp to a reasonable width
                        int availForLeft = Math.Max(0, contentWidth - rightStr.Length - 1); // 1 space gap
                        leftStr = FitToWidth(TrimLeadingSpaces(leftStr), availForLeft);
                        int gap = Math.Max(1, contentWidth - leftStr.Length - rightStr.Length);

                        var composed = indentStr + leftStr + new string(' ', gap) + rightStr;
                        lines.Add(new TermLine(composed, lineFg, lineBg, TextAlign.Left));

                        // Record mapping for row and its children to this single composed line
                        keyMap[node.Key] = new TermRegion(startLine, 1);
                        keyMap[node.Children[0].Key] = new TermRegion(startLine, 1);
                        keyMap[node.Children[1].Key] = new TermRegion(startLine, 1);
                    }
                    // Support bottom-dock layout for column containers: last child pinned to bottom
                    else if (node.Kind == UiKind.Column &&
                             node.Children.Count >= 1 &&
                             node.Props.TryGetValue(UiProperty.Layout, out var layoutCol) &&
                             string.Equals(layoutCol?.ToString(), "dock-bottom", StringComparison.OrdinalIgnoreCase))
                    {
                        int availableHeight = Math.Max(1, screenHeight - startLine);

                        // Render top children (all except last) into temp buffer
                        var topLines = new List<TermLine>();
                        var topMap = new Dictionary<string, TermRegion>();
                        for (int i = 0; i < node.Children.Count - 1; i++)
                        {
                            LayoutNode(node.Children[i], indent, width, screenHeight, focusedKey, topLines, topMap);
                        }

                        // Render bottom child into temp buffer
                        var bottomLines = new List<TermLine>();
                        var bottomMap = new Dictionary<string, TermRegion>();
                        LayoutNode(node.Children[^1], indent, width, screenHeight, focusedKey, bottomLines, bottomMap);

                        // Determine visible space for top section
                        int maxTopVisible = Math.Max(0, availableHeight - bottomLines.Count);

                        // Honor AutoScroll/Min (used as scroll offset) when provided on the messages child
                        UiNode? messagesChild = null;
                        for (int i = 0; i < node.Children.Count - 1; i++)
                        {
                            if (node.Children[i].Key == "messages") { messagesChild = node.Children[i]; break; }
                        }
                        bool autoScroll = messagesChild != null && messagesChild.Props.TryGetValue(UiProperty.AutoScroll, out var asv) && asv is bool ab && ab;
                        int requestedScroll = (messagesChild != null) ? (TryGetIntProp(messagesChild.Props, UiProperty.Min) ?? 0) : 0; // 0=bottom, higher=scroll up
                        requestedScroll = Math.Max(0, requestedScroll);

                        int totalTop = topLines.Count;
                        int dropFromTop;
                        if (!autoScroll && maxTopVisible > 0)
                        {
                            // Choose a window so that 'requestedScroll' lines remain above the bottom of top
                            dropFromTop = Math.Clamp(totalTop - maxTopVisible - requestedScroll, 0, Math.Max(0, totalTop - maxTopVisible));
                        }
                        else
                        {
                            // Auto-scroll to bottom
                            int overflow = Math.Max(0, (topLines.Count + bottomLines.Count) - availableHeight);
                            dropFromTop = Math.Min(overflow, topLines.Count);
                        }

                        // Build visible subset of top
                        var visibleTopLines = (dropFromTop > 0)
                            ? topLines.Skip(dropFromTop).Take(maxTopVisible).ToList()
                            : topLines.Take(maxTopVisible).ToList();

                        // Append top
                        int topStart = lines.Count;
                        lines.AddRange(visibleTopLines);

                        // Remap child key regions within visible window
                        foreach (var kv in topMap)
                        {
                            int relStart = kv.Value.StartLine;
                            int relEnd = kv.Value.StartLine + kv.Value.LineCount;
                            int newStart = Math.Max(0, relStart - dropFromTop);
                            int newEnd = Math.Max(0, relEnd - dropFromTop);
                            newStart = Math.Min(newStart, maxTopVisible);
                            newEnd = Math.Min(newEnd, maxTopVisible);
                            int len = Math.Max(0, newEnd - newStart);
                            if (len > 0)
                                keyMap[kv.Key] = new TermRegion(topStart + newStart, len);
                        }

                        // Add filler to pin bottom child to the bottom when there's spare space
                        int usedTop = visibleTopLines.Count;
                        int usedTotal = usedTop + bottomLines.Count;
                        int filler = Math.Max(0, availableHeight - usedTotal);
                        for (int i = 0; i < filler; i++)
                        {
                            lines.Add(new TermLine("", ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left));
                        }

                        // Simple right-edge scrollbar when top overflowed
                        bool overflowed = totalTop > maxTopVisible;
                        if (overflowed && maxTopVisible > 0)
                        {
                            int trackH = maxTopVisible;
                            int thumbH = Math.Max(1, (int)Math.Round(trackH * (maxTopVisible / (double)Math.Max(1, totalTop))));
                            int maxThumbTop = Math.Max(0, trackH - thumbH);
                            int scrolled = Math.Clamp(totalTop - maxTopVisible - dropFromTop, 0, Math.Max(0, totalTop - maxTopVisible));
                            int scrollRange = Math.Max(1, totalTop - maxTopVisible);
                            int thumbTop = (int)Math.Round(scrolled / (double)scrollRange * maxThumbTop);

                            for (int j = 0; j < maxTopVisible; j++)
                            {
                                int idx = topStart + j;
                                var l = lines[idx];
                                var baseText = l.Text ?? string.Empty;
                                var ensured = baseText.Length < width ? baseText.PadRight(width) : (baseText.Length > width ? baseText.Substring(0, width) : baseText);
                                char sb = (j >= thumbTop && j < thumbTop + thumbH) ? '█' : '│';
                                if (ensured.Length > 0)
                                    ensured = ensured.Substring(0, Math.Max(0, width - 1)) + sb;
                                lines[idx] = new TermLine(ensured, l.Foreground, l.Background, l.Align);
                            }
                        }

                        // Append bottom (composer)
                        int bottomStart = lines.Count;
                        lines.AddRange(bottomLines);
                        foreach (var kv in bottomMap)
                        {
                            keyMap[kv.Key] = new TermRegion(bottomStart + kv.Value.StartLine, kv.Value.LineCount);
                        }

                        // Region for this node
                        keyMap[node.Key] = new TermRegion(startLine, Math.Max(1, lines.Count - startLine));
                    }
                    // Support split layout (label/control 50/50) with wrapping
                    else if (node.Props.TryGetValue(UiProperty.Layout, out var layout) && layout?.ToString() == "split-50-50" && node.Children.Count >= 2)
                    {
                        int contentWidth = Math.Max(10, width - indent * 2);
                        int colWidth = contentWidth / 2;

                        // Render label and control into temporary lines
                        var leftLines = new List<TermLine>();
                        var rightLines = new List<TermLine>();
                        var tmpMapL = new Dictionary<string, TermRegion>();
                        var tmpMapR = new Dictionary<string, TermRegion>();

                        // Left (label)
                        LayoutNode(node.Children[0], 0, colWidth, screenHeight, focusedKey, leftLines, tmpMapL);
                        // Right (control)
                        LayoutNode(node.Children[1], 0, colWidth, screenHeight, focusedKey, rightLines, tmpMapR);

                        bool leftFocused = !string.IsNullOrEmpty(focusedKey) && tmpMapL.ContainsKey(focusedKey!);
                        bool rightFocused = !string.IsNullOrEmpty(focusedKey) && tmpMapR.ContainsKey(focusedKey!);
                        var lineFg = (leftFocused || rightFocused) ? ConsoleColor.Black : ConsoleColor.Gray;
                        var lineBg = (leftFocused || rightFocused) ? ConsoleColor.White : ConsoleColor.Black;

                        int maxLines = Math.Max(leftLines.Count, rightLines.Count);
                        for (int i = 0; i < maxLines; i++)
                        {
                            var leftText = i < leftLines.Count ? leftLines[i].Text : string.Empty;
                            var rightText = i < rightLines.Count ? rightLines[i].Text : string.Empty;
                            leftText = TrimLeadingSpaces(leftText);
                            rightText = TrimLeadingSpaces(rightText);
                            if (leftText.Length > colWidth) leftText = leftText.Substring(0, Math.Max(0, colWidth - 1)) + "…";
                            if (rightText.Length > colWidth) rightText = rightText.Substring(0, Math.Max(0, colWidth - 1)) + "…";
                            var composed = indentStr + leftText.PadRight(colWidth) + rightText.PadRight(colWidth);
                            lines.Add(new TermLine(composed, lineFg, lineBg, TextAlign.Left));
                        }

                        // Record mapping for row key to the composed region
                        keyMap[node.Key] = new TermRegion(startLine, Math.Max(1, maxLines));
                    }
                    else
                    {
                        foreach (var child in node.Children)
                        {
                            LayoutNode(child, node.Kind == UiKind.Column ? indent : indent + 1, width, screenHeight, focusedKey, lines, keyMap);
                        }
                    }
                    break;

                case UiKind.Label:
                    if (node.Props.TryGetValue(UiProperty.Text, out var labelText))
                    {
                        var align = ResolveAlign(node);
                        var wrap = ResolveWrap(node);
                        var fg = ResolveFg(node, ConsoleColor.Gray);
                        var bg = ResolveBg(node, isFocused ? ConsoleColor.DarkGray : ConsoleColor.Black);

                        string raw = labelText?.ToString() ?? string.Empty;
                        if (wrap)
                        {
                            int labAvail = Math.Max(1, width - indent * 2);
                            // Wrap while respecting explicit newlines
                            foreach (var seg in WrapText(raw, labAvail))
                            {
                                var textOut = (align == TextAlign.Center) ? seg : ($"{indentStr}{seg}");
                                lines.Add(new TermLine(textOut, fg, bg, align));
                            }
                        }
                        else
                        {
                            // Even without wrapping, respect explicit newlines to avoid emitting '\n' inside a single TermLine
                            var parts = (raw ?? string.Empty).Replace("\r\n", "\n").Split('\n');
                            foreach (var part in parts)
                            {
                                var textOut = (align == TextAlign.Center) ? part : ($"{indentStr}{part}");
                                lines.Add(new TermLine(textOut, fg, bg, align));
                            }
                        }
                    }
                    break;

                case UiKind.Button:
                    if (node.Props.TryGetValue(UiProperty.Text, out var btnText))
                    {
                        var fg = isFocused ? ConsoleColor.Black : ConsoleColor.White;
                        var bg = isFocused ? ConsoleColor.White : ConsoleColor.DarkGray;

                        lines.Add(new TermLine($"{indentStr}[ {btnText} ]", fg, bg, TextAlign.Left));
                    }
                    break;

                case UiKind.TextBox:
                case UiKind.TextArea:
                    // Prefer "text" prop (UiNode-based), fallback to legacy "value"
                    var value = node.Props.TryGetValue(UiProperty.Text, out var v) ? v?.ToString() : (node.Props.TryGetValue(UiProperty.Value, out var v2) ? v2?.ToString() : "");
                    var placeholder = node.Props.TryGetValue(UiProperty.Placeholder, out var p) ? p?.ToString() : "";
                    var displayText = string.IsNullOrEmpty(value) ? placeholder : value;

                    var textFg = isFocused ? ConsoleColor.Black : (string.IsNullOrEmpty(value) ? ConsoleColor.DarkGray : ResolveFg(node, ConsoleColor.White));
                    var textBg = isFocused ? ConsoleColor.White : ResolveBg(node, ConsoleColor.Black);

                    // Wrap display text to available width
                    int avail = Math.Max(1, width - indent * 2);
                    if (string.IsNullOrEmpty(displayText)) displayText = "";
                    var wrapped = WrapText(displayText, avail);
                    foreach (var wline in wrapped)
                    {
                        lines.Add(new TermLine($"{indentStr}{wline}", textFg, textBg, TextAlign.Left));
                    }
                    break;

                case UiKind.CheckBox:
                case UiKind.Toggle:
                    var isChecked = node.Props.TryGetValue(UiProperty.Checked, out var chk) && chk is bool c && c;
                    var checkbox = isChecked ? "[✓]" : "[ ]";
                    var cbLabel = node.Props.TryGetValue(UiProperty.Text, out var cbt) ? cbt?.ToString() : "";

                    var cbFg = isFocused ? ConsoleColor.Black : ResolveFg(node, ConsoleColor.Gray);
                    var cbBg = isFocused ? ConsoleColor.White : ResolveBg(node, ConsoleColor.Black);

                    lines.Add(new TermLine($"{indentStr}{checkbox} {cbLabel}", cbFg, cbBg, TextAlign.Left));
                    break;

                case UiKind.ListView:
                    if (node.Props.TryGetValue(UiProperty.Items, out var itemsObj) && itemsObj is IEnumerable<object> items)
                    {
                        var selectedIndex = node.Props.TryGetValue(UiProperty.SelectedIndex, out var si) && si is int idx ? idx : -1;
                        var itemList = items.ToList();

                        // Fixed viewport height for list view so surrounding content doesn't shift
                        int maxVisible = TryGetIntProp(node.Props, UiProperty.Height) ?? Program.config.MaxMenuItems;
                        maxVisible = Math.Max(1, maxVisible);

                        int count = itemList.Count;
                        if (count == 0)
                        {
                            // Render an empty placeholder line to keep consistent height
                            var fgEmpty = isFocused ? ConsoleColor.Black : ResolveFg(node, ConsoleColor.DarkGray);
                            var bgEmpty = isFocused ? ConsoleColor.DarkGray : ResolveBg(node, ConsoleColor.Black);
                            var emptyText = $"{indentStr}  (empty)";
                            // Ensure line width to keep columns aligned (esp. inside split layout)
                            emptyText = EnsureWidth(emptyText, width);
                            lines.Add(new TermLine(emptyText, fgEmpty, bgEmpty, TextAlign.Left));
                            break;
                        }

                        // Clamp selection
                        if (selectedIndex < 0) selectedIndex = 0;
                        if (selectedIndex >= count) selectedIndex = count - 1;

                        int visibleCount = Math.Min(maxVisible, count);

                        // Compute scrolling offset so selected item remains visible
                        int offset = Math.Min(Math.Max(0, selectedIndex - visibleCount + 1), Math.Max(0, count - visibleCount));

                        bool showScrollbar = count > visibleCount;
                        int contentRight = showScrollbar ? (width - 1) : width; // reserve 1 col for scrollbar when needed

                        // Scrollbar metrics
                        int trackHeight = visibleCount;
                        int thumbHeight = 1;
                        int thumbTop = 0;
                        if (showScrollbar)
                        {
                            thumbHeight = Math.Max(1, (int)Math.Round((double)trackHeight * visibleCount / Math.Max(1, count)));
                            thumbHeight = Math.Min(thumbHeight, trackHeight);
                            int maxThumbTop = Math.Max(0, trackHeight - thumbHeight);
                            int scrollRange = Math.Max(1, count - visibleCount);
                            thumbTop = (int)Math.Round((double)offset / scrollRange * maxThumbTop);
                            thumbTop = Math.Clamp(thumbTop, 0, maxThumbTop);
                        }

                        for (int j = 0; j < visibleCount; j++)
                        {
                            int i = offset + j;
                            var isSelected = i == selectedIndex;
                            var fg = isSelected ? ConsoleColor.Black : (isFocused ? ConsoleColor.Black : ResolveFg(node, ConsoleColor.Gray));
                            var bg = isSelected ? ConsoleColor.White : (isFocused ? ConsoleColor.DarkGray : ResolveBg(node, ConsoleColor.Black));

                            // Compose content within available width, placing scrollbar at right edge when shown
                            var prefix = $"{indentStr}  {(isSelected ? ">" : " ")} ";
                            int availForItem = Math.Max(1, contentRight - prefix.Length);
                            var itemText = itemList[i]?.ToString() ?? string.Empty;
                            // Fit item text
                            if (itemText.Length > availForItem)
                                itemText = itemText.Substring(0, Math.Max(0, availForItem - 1)) + "…";

                            var rowText = prefix + itemText;
                            if (rowText.Length < contentRight)
                                rowText = rowText.PadRight(contentRight);
                            else if (rowText.Length > contentRight)
                                rowText = rowText.Substring(0, contentRight);

                            if (showScrollbar)
                            {
                                // Choose scrollbar glyph for this row
                                char sb = (j >= thumbTop && j < thumbTop + thumbHeight) ? '█' : '│';
                                rowText += sb;
                            }

                            // Ensure total width alignment for parent composers
                            rowText = EnsureWidth(rowText, width);
                            lines.Add(new TermLine(rowText, fg, bg, TextAlign.Left));
                        }
                    }
                    break;

                case UiKind.Html:
                    if (node.Props.TryGetValue(UiProperty.Content, out var htmlContent))
                    {
                        lines.Add(new TermLine($"{indentStr}{htmlContent}", ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left));
                    }
                    break;

                case UiKind.Spacer:
                    var height = node.Props.TryGetValue(UiProperty.Height, out var h) && h is int ht ? ht : 1;
                    for (int i = 0; i < height; i++)
                    {
                        lines.Add(new TermLine("", ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left));
                    }
                    break;

                case UiKind.Accordion:
                    var title = node.Props.TryGetValue(UiProperty.Title, out var t2) ? t2?.ToString() : "Accordion";
                    var isExpanded = node.Props.TryGetValue(UiProperty.Expanded, out var exp) && exp is bool e && e;

                    var accFg = isFocused ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                    lines.Add(new TermLine($"{indentStr}{(isExpanded ? "▼" : "▶")} {title}", accFg, ConsoleColor.Black, TextAlign.Left));

                    if (isExpanded)
                    {
                        foreach (var child in node.Children)
                        {
                            LayoutNode(child, indent + 1, width, screenHeight, focusedKey, lines, keyMap);
                        }
                    }
                    break;
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

            // Render overlay content into temporary lines (no outer indentation)
            var innerLines = new List<TermLine>();
            var tmpMap = new Dictionary<string, TermRegion>();
            foreach (var child in overlay.Children)
            {
                LayoutNode(child, 0, screenWidth, screenHeight, focusedKey, innerLines, tmpMap);
            }

            // Convert to strings and clamp to content width
            int contentWidth = Math.Max(1, boxWidth - 2); // borders on left/right
            var content = innerLines
                .Select(l => (text: TrimLeadingSpaces(l.Text), align: l.Align))
                .Select(t =>
                {
                    var fit = FitToWidth(t.text ?? string.Empty, contentWidth);
                    if (t.align == TextAlign.Center)
                    {
                        int pad = Math.Max(0, contentWidth - fit.Length);
                        int left = pad / 2;
                        return new string(' ', left) + fit;
                    }
                    return fit;
                })
                .ToList();

            // Build box lines (top border, content, bottom border)
            var boxLines = new List<string>();
            string top = "┌" + new string('─', contentWidth) + "┐";
            string bottom = "└" + new string('─', contentWidth) + "┘";
            boxLines.Add(top);
            if (content.Count == 0)
            {
                content.Add("");
            }
            foreach (var line in content)
            {
                boxLines.Add("│" + line.PadRight(contentWidth) + "│");
            }
            boxLines.Add(bottom);

            // Compute vertical placement: center in current visible area approximation
            int viewportHeight = Math.Max(10, screenHeight);
            int boxHeight = boxLines.Count;
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
                var baseText = baseLines[lineIndex].Text;
                var row = EnsureWidth(baseText, screenWidth);
                var overlayRow = boxLines[i];
                // Build new row: left + overlay + right
                string left = row.Substring(0, Math.Min(xStart, row.Length));
                string right = (xStart + boxWidth <= row.Length) ? row.Substring(xStart + boxWidth) : string.Empty;
                string newRow = left + overlayRow.PadRight(boxWidth) + right;
                // Clamp to screen width
                newRow = EnsureWidth(newRow, screenWidth);
                baseLines[lineIndex] = new TermLine(newRow, ConsoleColor.Gray, ConsoleColor.Black, TextAlign.Left);
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
    private sealed record TermLine(
        string Text,
        ConsoleColor Foreground,
        ConsoleColor Background,
        TextAlign Align
    );

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
    private ConsoleKeyInfo _lastKey;

    /// <summary>
    /// Non-blocking poll for key input. Returns ConsoleKeyInfo if a key is available, otherwise null.
    /// </summary>
    public ConsoleKeyInfo? TryReadKey()
    {
        if (Console.KeyAvailable)
        {
            _lastKey = Console.ReadKey(intercept: true);
            return _lastKey;
        }
        return null;
    }
}