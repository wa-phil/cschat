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

    private string? lastInput = null;

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

    public override async Task<string?> ReadInputAsync(CommandManager commandManager)
    {
        var buffer = new List<char>();
        var lines = new List<string>();
        int cursor = 0;
        ConsoleKeyInfo key;

        while (true)
        {
            key = ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                // Soft new line
                lines.Add(new string(buffer.ToArray()));
                buffer.Clear();
                cursor = 0;
                Write("\n> ");
                continue;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                // erase the contents of the current line, and do not advance to the next line
                SetCursorPosition(0, CursorTop);
                Write(new string(' ', Width - 1));
                SetCursorPosition(0, CursorTop);
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buffer.RemoveAt(cursor - 1);
                    cursor--;
                    Write("\b \b");
                }
                continue;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                var result = await commandManager.Action();
                if (result == Command.Result.Failed)
                {
                    WriteLine("Command failed.");
                }
                Write("[press ESC to open menu]\n> ");
                buffer.Clear();
                cursor = 0;
                continue;
            }
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (lastInput != null)
                {
                    // Clear current buffer display
                    for (int i = 0; i < buffer.Count; i++)
                    {
                        Write("\b \b");
                    }

                    // Set buffer to last input
                    buffer.Clear();
                    buffer.AddRange(lastInput.ToCharArray());
                    cursor = buffer.Count;

                    // Display the recalled input
                    Write(lastInput);
                }
                continue;
            }
            if (key.KeyChar != '\0')
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Write(key.KeyChar.ToString());
            }
        }
        lines.Add(new string(buffer.ToArray()));
        var input = string.Join("\n", lines).Trim();

        // Store the input for history if it's not empty
        if (!string.IsNullOrWhiteSpace(input))
        {
            lastInput = input;
        }

        return string.IsNullOrWhiteSpace(input) ? null : input;
    }

    [Obsolete("Use ReadInputAsync instead")]
    public override async Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager)
    {
        return await ReadInputAsync(commandManager);
    }

    public override IInputRouter GetInputRouter()
    {
        if (_inputRouter == null)
        {
            _inputRouter = new TerminalInputRouter();
            _inputRouter.Attach(this);
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
        _lastSnapshot = _termDom.Layout(root, Width, _uiTree.FocusedKey);
        _termDom.Apply(_termDom.GetFullRender(_lastSnapshot));

        ctx.Succeeded();
        return Task.CompletedTask;
    });

    protected override Task PostPatchAsync(UiPatch patch)
    {
        // Use incremental rendering if enabled, otherwise fall back to full re-render
        if (_uiTree.Root != null)
        {
            var newSnapshot = _termDom.Layout(_uiTree.Root, Width, _uiTree.FocusedKey);

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
            var newSnapshot = _termDom.Layout(_uiTree.Root, Width, _uiTree.FocusedKey);

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
                if (node.Props.TryGetValue("text", out var labelText))
                {
                    ForegroundColor = node.Props.TryGetValue("color", out var color) && color is ConsoleColor cc
                        ? cc
                        : ConsoleColor.Gray;
                    WriteLine($"{indentStr}{labelText}");
                    ResetColor();
                }
                break;

            case UiKind.Button:
                if (node.Props.TryGetValue("text", out var btnText))
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
                var text = node.Props.TryGetValue("text", out var t) ? t?.ToString() : "";
                var placeholder = node.Props.TryGetValue("placeholder", out var ph) ? ph?.ToString() : "";
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
                var isChecked = node.Props.TryGetValue("checked", out var chk) && chk is bool b && b;
                var cbLabel = node.Props.TryGetValue("label", out var lbl) ? lbl?.ToString() : "";
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
                if (node.Props.TryGetValue("items", out var itemsObj) && itemsObj is IEnumerable<object> items)
                {
                    var selectedIndex = node.Props.TryGetValue("selectedIndex", out var si) && si is int idx ? idx : -1;
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
                if (node.Props.TryGetValue("content", out var htmlContent))
                {
                    WriteLine($"{indentStr}{htmlContent}");
                }
                break;

            case UiKind.Spacer:
                var height = node.Props.TryGetValue("height", out var h) && h is int ht ? ht : 1;
                for (int i = 0; i < height; i++)
                {
                    WriteLine();
                }
                break;

            case UiKind.Accordion:
                var title = node.Props.TryGetValue("title", out var t2) ? t2?.ToString() : "Accordion";
                var isExpanded = node.Props.TryGetValue("expanded", out var exp) && exp is bool e && e;

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
        public TermSnapshot Layout(UiNode root, int width, string? focusedKey)
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
                    LayoutNode(child, 0, width, focusedKey, lines, keyMap);
                }
            }
            else
            {
                LayoutNode(root, 0, width, focusedKey, lines, keyMap);
            }

            // Composite overlays on top (modal). Later overlays in the list have higher z-order.
            if (overlaysContainer != null && overlaysContainer.Children.Count > 0)
            {
                // Sort by zIndex if present, stable otherwise.
                IEnumerable<UiNode> overlayNodes = overlaysContainer.Children;
                overlayNodes = overlayNodes
                    .Select(n => (n, z: TryGetIntProp(n.Props, UiProps.ZIndex) ?? 0))
                    .OrderBy(t => t.z)
                    .Select(t => t.n);

                foreach (var overlay in overlayNodes)
                {
                    CompositeOverlayBox(overlay, width, focusedKey, lines, keyMap);
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
                    edits.Add(new TermEdit(i, new TermLine("", ConsoleColor.Gray, ConsoleColor.Black)));
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

        private void LayoutNode(UiNode node, int indent, int width, string? focusedKey, List<TermLine> lines, Dictionary<string, TermRegion> keyMap)
        {
            var startLine = lines.Count;
            var indentStr = new string(' ', indent * 2);
            var isFocused = node.Key == focusedKey;

            switch (node.Kind)
            {
                case UiKind.Column:
                case UiKind.Row:
                    // Support split layout (label/control 50/50) with wrapping
                    if (node.Props.TryGetValue("layout", out var layout) && layout?.ToString() == "split-50-50" && node.Children.Count >= 2)
                    {
                        int contentWidth = Math.Max(10, width - indent * 2);
                        int colWidth = contentWidth / 2;

                        // Render label and control into temporary lines
                        var leftLines = new List<TermLine>();
                        var rightLines = new List<TermLine>();
                        var tmpMapL = new Dictionary<string, TermRegion>();
                        var tmpMapR = new Dictionary<string, TermRegion>();

                        // Left (label)
                        LayoutNode(node.Children[0], 0, colWidth, focusedKey, leftLines, tmpMapL);
                        // Right (control)
                        LayoutNode(node.Children[1], 0, colWidth, focusedKey, rightLines, tmpMapR);

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
                            lines.Add(new TermLine(composed, lineFg, lineBg));
                        }

                        // Record mapping for row key to the composed region
                        keyMap[node.Key] = new TermRegion(startLine, Math.Max(1, maxLines));
                    }
                    else
                    {
                        foreach (var child in node.Children)
                        {
                            LayoutNode(child, node.Kind == UiKind.Column ? indent : indent + 1, width, focusedKey, lines, keyMap);
                        }
                    }
                    break;

                case UiKind.Label:
                    if (node.Props.TryGetValue("text", out var labelText))
                    {
                        var fg = node.Props.TryGetValue("color", out var color) && color is ConsoleColor cc
                            ? cc
                            : ConsoleColor.Gray;
                        var bg = isFocused ? ConsoleColor.DarkGray : ConsoleColor.Black;

                        bool center = node.Props.TryGetValue("align", out var al) && (al?.ToString()?.Equals("center", StringComparison.OrdinalIgnoreCase) == true);
                        var textOut = center ? "[[CENTER]]" + labelText?.ToString() : ($"{indentStr}{labelText}");
                        lines.Add(new TermLine(textOut, fg, bg));
                    }
                    break;

                case UiKind.Button:
                    if (node.Props.TryGetValue("text", out var btnText))
                    {
                        var fg = isFocused ? ConsoleColor.Black : ConsoleColor.White;
                        var bg = isFocused ? ConsoleColor.White : ConsoleColor.DarkGray;

                        lines.Add(new TermLine($"{indentStr}[ {btnText} ]", fg, bg));
                    }
                    break;

                case UiKind.TextBox:
                case UiKind.TextArea:
                    // Prefer "text" prop (UiNode-based), fallback to legacy "value"
                    var value = node.Props.TryGetValue("text", out var v) ? v?.ToString() : (node.Props.TryGetValue("value", out var v2) ? v2?.ToString() : "");
                    var placeholder = node.Props.TryGetValue("placeholder", out var p) ? p?.ToString() : "";
                    var displayText = string.IsNullOrEmpty(value) ? placeholder : value;

                    var textFg = isFocused ? ConsoleColor.Black : (string.IsNullOrEmpty(value) ? ConsoleColor.DarkGray : ConsoleColor.White);
                    var textBg = isFocused ? ConsoleColor.White : ConsoleColor.Black;

                    // Wrap display text to available width
                    int avail = Math.Max(1, width - indent * 2);
                    if (string.IsNullOrEmpty(displayText)) displayText = "";
                    var wrapped = WrapText(displayText, avail);
                    foreach (var wline in wrapped)
                    {
                        lines.Add(new TermLine($"{indentStr}{wline}", textFg, textBg));
                    }
                    break;

                case UiKind.CheckBox:
                case UiKind.Toggle:
                    var isChecked = node.Props.TryGetValue("checked", out var chk) && chk is bool c && c;
                    var checkbox = isChecked ? "[✓]" : "[ ]";
                    var cbLabel = node.Props.TryGetValue("text", out var cbt) ? cbt?.ToString() : "";

                    var cbFg = isFocused ? ConsoleColor.Black : ConsoleColor.Gray;
                    var cbBg = isFocused ? ConsoleColor.White : ConsoleColor.Black;

                    lines.Add(new TermLine($"{indentStr}{checkbox} {cbLabel}", cbFg, cbBg));
                    break;

                case UiKind.ListView:
                    if (node.Props.TryGetValue("items", out var itemsObj) && itemsObj is IEnumerable<object> items)
                    {
                        var selectedIndex = node.Props.TryGetValue("selectedIndex", out var si) && si is int idx ? idx : -1;
                        var itemList = items.ToList();

                        for (int i = 0; i < itemList.Count; i++)
                        {
                            var isSelected = i == selectedIndex;
                            var fg = isSelected ? ConsoleColor.Black : (isFocused ? ConsoleColor.Black : ConsoleColor.Gray);
                            var bg = isSelected ? ConsoleColor.White : (isFocused ? ConsoleColor.DarkGray : ConsoleColor.Black);

                            lines.Add(new TermLine($"{indentStr}  {(isSelected ? ">" : " ")} {itemList[i]}", fg, bg));
                        }
                    }
                    break;

                case UiKind.Html:
                    if (node.Props.TryGetValue("content", out var htmlContent))
                    {
                        lines.Add(new TermLine($"{indentStr}{htmlContent}", ConsoleColor.Gray, ConsoleColor.Black));
                    }
                    break;

                case UiKind.Spacer:
                    var height = node.Props.TryGetValue("height", out var h) && h is int ht ? ht : 1;
                    for (int i = 0; i < height; i++)
                    {
                        lines.Add(new TermLine("", ConsoleColor.Gray, ConsoleColor.Black));
                    }
                    break;

                case UiKind.Accordion:
                    var title = node.Props.TryGetValue("title", out var t2) ? t2?.ToString() : "Accordion";
                    var isExpanded = node.Props.TryGetValue("expanded", out var exp) && exp is bool e && e;

                    var accFg = isFocused ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                    lines.Add(new TermLine($"{indentStr}{(isExpanded ? "▼" : "▶")} {title}", accFg, ConsoleColor.Black));

                    if (isExpanded)
                    {
                        foreach (var child in node.Children)
                        {
                            LayoutNode(child, indent + 1, width, focusedKey, lines, keyMap);
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
        private void CompositeOverlayBox(UiNode overlay, int screenWidth, string? focusedKey, List<TermLine> baseLines, Dictionary<string, TermRegion> keyMap)
        {
            // Determine overlay box width from props (supports "80%" or absolute int), with sensible bounds
            int boxWidth = Math.Clamp(ParseWidth(overlay.Props, screenWidth), Math.Min(20, Math.Max(1, screenWidth - 2)), Math.Max(10, screenWidth - 2));
            int xStart = Math.Max(0, (screenWidth - boxWidth) / 2);

            // Render overlay content into temporary lines (no outer indentation)
            var innerLines = new List<TermLine>();
            var tmpMap = new Dictionary<string, TermRegion>();
            foreach (var child in overlay.Children)
            {
                LayoutNode(child, 0, screenWidth, focusedKey, innerLines, tmpMap);
            }

            // Convert to strings and clamp to content width
            int contentWidth = Math.Max(1, boxWidth - 2); // borders on left/right
            var content = innerLines
                .Select(l => TrimLeadingSpaces(l.Text))
                .Select(t =>
                {
                    const string CenterTag = "[[CENTER]]";
                    if (t != null && t.StartsWith(CenterTag, StringComparison.Ordinal))
                    {
                        var raw = t.Substring(CenterTag.Length);
                        // If too long, trim with ellipsis first
                        var fit = FitToWidth(raw, contentWidth);
                        // Center within content width
                        int pad = Math.Max(0, contentWidth - fit.Length);
                        int left = pad / 2;
                        return new string(' ', left) + fit; // right padding will be added later via PadRight
                    }
                    return FitToWidth(t ?? string.Empty, contentWidth);
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
            int viewportHeight = Math.Max(10, Console.WindowHeight);
            int boxHeight = boxLines.Count;
            int yStart = Math.Max(0, (Math.Min(baseLines.Count, viewportHeight) - boxHeight) / 2);

            // Ensure base lines list is long enough
            int requiredLines = yStart + boxHeight;
            while (baseLines.Count < requiredLines)
            {
                baseLines.Add(new TermLine("", ConsoleColor.Gray, ConsoleColor.Black));
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
                baseLines[lineIndex] = new TermLine(newRow, ConsoleColor.Gray, ConsoleColor.Black);
            }

            // Record region for overlay (helps focus mapping, etc.)
            keyMap[overlay.Key] = new TermRegion(yStart, boxHeight);
        }

        private static List<string> WrapText(string text, int width)
        {
            var result = new List<string>();
            if (width <= 0) { result.Add(""); return result; }
            if (string.IsNullOrEmpty(text)) { result.Add(""); return result; }

            int idx = 0;
            while (idx < text.Length)
            {
                int take = Math.Min(width, text.Length - idx);
                // Try to break on whitespace when possible
                int end = idx + take;
                if (end < text.Length && !char.IsWhiteSpace(text[end - 1]))
                {
                    int lastSpace = text.LastIndexOf(' ', end - 1, take);
                    if (lastSpace > idx)
                    {
                        end = lastSpace + 1;
                    }
                }
                var segment = text.Substring(idx, end - idx).TrimEnd();
                result.Add(segment);
                idx = end;
            }
            return result;
        }

        private static int? TryGetIntProp(IReadOnlyDictionary<string, object?> props, string key)
        {
            if (props.TryGetValue(key, out var val))
            {
                if (val is int i) return i;
                if (val is long l) return (int)l;
                if (val is string s && int.TryParse(s, out var p)) return p;
            }
            return null;
        }

        private static int ParseWidth(IReadOnlyDictionary<string, object?> props, int screenWidth)
        {
            // default 80%
            int defaultWidth = Math.Max(20, (int)(screenWidth * 0.8));
            if (!props.TryGetValue("width", out var w) || w is null) return defaultWidth;
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
    /// A single line in the terminal with styling
    /// </summary>
    private sealed record TermLine(
        string Text,
        ConsoleColor Foreground,
        ConsoleColor Background
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
    private IUi? _ui;
    private readonly StringBuilder _inputBuffer = new();
    private TaskCompletionSource<string?>? _readLineTcs;
    private CommandManager? _commands;
    private ConsoleKeyInfo _lastKey;

    public event Action<string>? OnInputChanged;

    public void Attach(IUi ui)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

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

    [Obsolete("Use TryReadKey and ReadInputAsync instead")]
    public async Task<string?> ReadLineAsync(CommandManager commands)
    {
        if (_ui == null)
            throw new InvalidOperationException("InputRouter not attached to UI");

        _commands = commands;
        _readLineTcs = new TaskCompletionSource<string?>();
        _inputBuffer.Clear();

        // Start reading from console in background
        _ = Task.Run(() => ReadConsoleInput());

        return await _readLineTcs.Task;
    }

    private async void ReadConsoleInput()
    {
        if (_ui == null || _readLineTcs == null) return;
        
        var lines = new List<string>();

        while (!_readLineTcs.Task.IsCompleted)
        {
            var maybe = TryReadKey();
            if (maybe is null)
            {
                await Task.Delay(10);
                continue;
            }

            var key = maybe.Value;

            // Handle Escape (command palette)
            if (key.Key == ConsoleKey.Escape && _commands != null)
            {
                try
                {
                    await _commands.Action();
                    // Focus back to input after command
                    await _ui.FocusAsync("input");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Command error: {ex.Message}");
                }
                continue;
            }

            // Handle Enter (submit) vs Shift+Enter (newline)
            if (key.Key == ConsoleKey.Enter)
            {
                if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                {
                    // Shift+Enter: Insert newline (add current line to buffer and start new line)
                    lines.Add(_inputBuffer.ToString());
                    _inputBuffer.Clear();
                    var multilineText = string.Join("\n", lines) + "\n";
                    
                    // Update the UI node
                    await UpdateInputNode(multilineText);
                    OnInputChanged?.Invoke(multilineText);
                }
                else
                {
                    // Enter: Submit
                    lines.Add(_inputBuffer.ToString());
                    var result = string.Join("\n", lines).Trim();
                    _readLineTcs.TrySetResult(string.IsNullOrWhiteSpace(result) ? null : result);
                    return;
                }
            }
            // Handle Backspace
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (_inputBuffer.Length > 0)
                {
                    _inputBuffer.Length--;
                    var currentText = _inputBuffer.ToString();
                    if (lines.Count > 0)
                    {
                        currentText = string.Join("\n", lines) + "\n" + currentText;
                    }
                    await UpdateInputNode(currentText);
                    OnInputChanged?.Invoke(currentText);
                }
                else if (lines.Count > 0)
                {
                    // Backspace at start of line - go back to previous line
                    var lastLine = lines[lines.Count - 1];
                    lines.RemoveAt(lines.Count - 1);
                    _inputBuffer.Clear();
                    _inputBuffer.Append(lastLine);
                    var currentText = lines.Count > 0 ? string.Join("\n", lines) + "\n" + _inputBuffer.ToString() : _inputBuffer.ToString();
                    await UpdateInputNode(currentText);
                    OnInputChanged?.Invoke(currentText);
                }
            }
            // Handle UpArrow (recall last input if available)
            else if (key.Key == ConsoleKey.UpArrow)
            {
                // Let the caller handle history - for now just ignore
                continue;
            }
            // Handle printable characters
            else if (!char.IsControl(key.KeyChar))
            {
                _inputBuffer.Append(key.KeyChar);
                var currentText = _inputBuffer.ToString();
                if (lines.Count > 0)
                {
                    currentText = string.Join("\n", lines) + "\n" + currentText;
                }
                await UpdateInputNode(currentText);
                OnInputChanged?.Invoke(currentText);
            }
        }
    }

    /// <summary>
    /// Updates the input UiNode with the current text
    /// </summary>
    private async Task UpdateInputNode(string text)
    {
        if (_ui == null) return;
        
        try
        {
            await _ui.PatchAsync(new UiPatch(
                new UpdatePropsOp("input", new Dictionary<string, object?>
                {
                    ["text"] = text,
                    ["placeholder"] = "Type a message..."
                }),
                new UpdatePropsOp("send-btn", new Dictionary<string, object?>
                {
                    ["text"] = "Send",
                    ["enabled"] = !string.IsNullOrWhiteSpace(text)
                })
            ));
        }
        catch
        {
            // Ignore errors if UI is being torn down
        }
    }
}