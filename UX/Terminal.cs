using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

public class Terminal : IUi
{
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

    public Task<bool> ConfirmAsync(string question, bool defaultAnswer = false)
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

    public async Task<bool> ShowFormAsync(UiForm form)
    {
        await Task.CompletedTask;
        WriteLine(form.Title);
        WriteLine(new string('-', Math.Min(Width - 1, Math.Max(8, form.Title.Length))));

        foreach (var f in form.Fields)
        {
            while (true)
            {
                var current = f.Formatter(form.Model);
                Write($"{(f.Required ? "*" : " ")}{f.Label}: ");
                if (!string.IsNullOrWhiteSpace(f.Help)) { Write(f.Help); }
                WriteLine($" [currently: {current}]");

                // If this is an enum-style field with choices, render a menu selection
                var choices = f.EnumChoices()?.ToList();
                string? err = null;
                if (f.Kind == UiFieldKind.Enum && choices != null && choices.Count > 0)
                {
                    var selected = RenderMenu($"Select {f.Label}:", choices, 0);
                    if (selected == null) { WriteLine("(cancelled)"); return false; }
                    if (!f.TrySetFromString(form.Model!, selected, out err))
                    {
                        WriteLine($"  {err}");
                        continue;
                    }
                    break;
                }

                // read line with ESC cancel (reuse your input loop style)
                var buffer = new List<char>();
                while (true)
                {
                    var k = ReadKey(intercept: true);
                    if (k.Key == ConsoleKey.Escape) { WriteLine("\n(cancelled)"); return false; }
                    if (k.Key == ConsoleKey.Enter) { WriteLine(); break; }
                    if (k.Key == ConsoleKey.Backspace && buffer.Count > 0) { buffer.RemoveAt(buffer.Count - 1); Write("\b \b"); continue; }
                    if (k.KeyChar != '\0' && !char.IsControl(k.KeyChar)) { buffer.Add(k.KeyChar); Write(k.KeyChar.ToString()); }
                }

                var raw = new string(buffer.ToArray());
                if (string.IsNullOrWhiteSpace(raw)) raw = current; // leave as-is if blank

                if (!f.TrySetFromString(form.Model!, raw, out err))
                {
                    WriteLine($"  {err}");
                    continue;
                }

                break;
            }
        }
        return true;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt)
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

    public string StartProgress(string title, CancellationTokenSource cts)
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

    public void UpdateProgress(string id, ProgressSnapshot s)
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
            ProgressState.Running  => 3,
            ProgressState.Queued   => 2,
            ProgressState.Failed   => 1,   // keep failed items visible
            ProgressState.Canceled => 0,   // hide from main list (shown in footer stats)
            ProgressState.Completed=> -1,  // hide from main list
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

    public void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown)
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

    public async Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory)
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

    public async Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager)
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

    public void RenderTable(Table table, string? title = null)
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

    // Renders a menu at the current cursor position, allows arrow key navigation, and returns the selected string or null if cancelled
    public string? RenderMenu(string header, List<string> choices, int selected = 0)
    {
        // Store original choices for scrolling
        var originalChoices = new List<string>(choices);
        int actualMaxVisibleItems = Program.config.MaxMenuItems, originalSelected = selected;

        // always position the menu at the top, and print the header
        Clear();
        SetCursorPosition(0, 0);
        ForegroundColor = ConsoleColor.Green;
        WriteLine(header);
        ResetColor();

        // Calculate scrolling parameters
        int scrollOffset = 0, visibleItems = Math.Min(originalChoices.Count, actualMaxVisibleItems);
        bool hasMoreAbove = false, hasMoreBelow = false;

        // Reserve space for menu lines, indicators, and input
        int indicatorLines = 2; // up to 2 lines for "more above/below" indicators
        int inputLines = 1; // input line
        int menuLines = visibleItems + indicatorLines;

        // Print placeholder lines for the menu area
        for (int i = 0; i < menuLines + inputLines; i++)
        {
            WriteLine();
        }

        int menuStartRow = CursorTop - (menuLines + inputLines);
        int inputTop = CursorTop - inputLines;

        string filter = "";
        List<string> filteredChoices = new List<string>(originalChoices);
        int filteredSelected = originalSelected;

        void DrawMenu() => Log.Method(ctx =>
        {
            ctx.OnlyEmitOnFailure();
            ctx.Append(Log.Data.ConsoleHeight, Height);
            ctx.Append(Log.Data.ConsoleWidth, Width);

            // NEW: clamp everything we draw to the usable width
            int usable = Math.Max(1, Width - 1);
            string Fit(string s)
            {
                // Hard-truncate including the ellipsis so we never exceed 'usable'
                var clipped = Utilities.TruncatePlainHard(s ?? string.Empty, usable);
                // Pad to paint over any leftovers from previous longer lines
                return clipped.PadRight(usable);
            }

            // Calculate which items to show and update scroll indicators
            visibleItems = Math.Min(filteredChoices.Count, actualMaxVisibleItems);

            // Adjust scroll offset to keep selected item visible
            if (filteredSelected < scrollOffset) scrollOffset = filteredSelected;
            else if (filteredSelected >= scrollOffset + visibleItems)
                scrollOffset = filteredSelected - visibleItems + 1;

            // Ensure scroll offset is within bounds
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, Math.Max(0, filteredChoices.Count - visibleItems)));
            hasMoreAbove = scrollOffset > 0;
            hasMoreBelow = scrollOffset + visibleItems < filteredChoices.Count;

            // Ensure we don't exceed console buffer bounds
            int maxRow = Height - 1;
            int currentRow = menuStartRow;

            // Draw "more above" indicator if needed
            if (hasMoreAbove && currentRow < maxRow)
            {
                SetCursorPosition(0, currentRow);
                ForegroundColor = ConsoleColor.DarkGray;
                var countAbove = Math.Min(scrollOffset, filteredChoices.Count);
                Write(Fit($"^^^ {countAbove} items above ^^^"));
                ResetColor();
                currentRow++;
            }

            // Draw visible menu items
            for (int i = 0; i < visibleItems && currentRow < maxRow; i++)
            {
                int choiceIndex = scrollOffset + i;
                if (choiceIndex >= filteredChoices.Count) break;

                ctx.Append(Log.Data.MenuTop, currentRow);
                SetCursorPosition(0, currentRow);
                string line;

                bool isSelected = choiceIndex == filteredSelected;
                if (isSelected)
                {
                    ForegroundColor = ConsoleColor.Black;
                    BackgroundColor = ConsoleColor.White;
                    line = (filteredChoices.Count <= 9)
                        ? $"> [{choiceIndex + 1}] {filteredChoices[choiceIndex]} "
                        : $"> {filteredChoices[choiceIndex]} ";
                }
                else
                {
                    ForegroundColor = ConsoleColor.Gray;
                    BackgroundColor = ConsoleColor.Black;
                    line = (filteredChoices.Count <= 9)
                        ? $"  [{choiceIndex + 1}] {filteredChoices[choiceIndex]} "
                        : $"  {filteredChoices[choiceIndex]} ";
                }

                Write(Fit(line));
                ResetColor();
                currentRow++;
            }

            // Draw "more below" indicator if needed
            if (hasMoreBelow && currentRow < maxRow)
            {
                SetCursorPosition(0, currentRow);
                ForegroundColor = ConsoleColor.DarkGray;
                var countBelow = Math.Max(0, filteredChoices.Count - (scrollOffset + visibleItems));
                Write(Fit($"vvv {countBelow} items below vvv"));
                ResetColor();
                currentRow++;
            }

            // Clear any leftover lines in the menu area
            while (currentRow < inputTop && currentRow < maxRow)
            {
                SetCursorPosition(0, currentRow);
                Write(new string(' ', usable));
                currentRow++;
            }

            // Draw input header only if it fits in the buffer
            if (inputTop < maxRow)
            {
                ctx.Append(Log.Data.InputTop, inputTop);
                SetCursorPosition(0, inputTop);
                var inputHeader = "[filter]> ";
                Write(Fit($"{inputHeader}{filter}"));
                // place cursor at end of filter if it fits
                int caret = Math.Min(inputHeader.Length + filter.Length, usable);
                SetCursorPosition(caret, inputTop);
            }
            ctx.Succeeded();
        });

        DrawMenu();
        ConsoleKeyInfo key;
        while (true)
        {
            key = ReadKey(true);
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (filteredSelected > 0) filteredSelected--;
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.PageUp || (key.Key == ConsoleKey.UpArrow && key.Modifiers.HasFlag(ConsoleModifiers.Shift)))
            {
                if (filteredSelected > 0)
                {
                    filteredSelected -= Math.Min(actualMaxVisibleItems, filteredSelected);
                }
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.Home)
            {
                filteredSelected = 0;
                scrollOffset = 0; // Reset scroll when going to home
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.End)
            {
                filteredSelected = Math.Max(0, filteredChoices.Count - 1);
                scrollOffset = Math.Max(0, filteredChoices.Count - actualMaxVisibleItems); // Reset scroll when going to end
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                if (filteredSelected < filteredChoices.Count - 1) filteredSelected++;
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.PageDown || (key.Key == ConsoleKey.DownArrow && key.Modifiers.HasFlag(ConsoleModifiers.Shift)))
            {
                if (filteredSelected < filteredChoices.Count - 1)
                {
                    filteredSelected += Math.Min(actualMaxVisibleItems, filteredChoices.Count - 1 - filteredSelected);
                }
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                // Safe cursor positioning for exit
                int exitRow = Math.Min(inputTop + 1, Height - 1);
                SetCursorPosition(0, exitRow);
                if (filteredChoices.Count > 0)
                    return filteredChoices[filteredSelected];
                else
                    return null;
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                // Safe cursor positioning for exit
                int exitRow = Math.Min(inputTop + 1, Height - 1);
                SetCursorPosition(0, exitRow);
                WriteLine("Selection cancelled.");
                return null;
            }
            else if (filteredChoices.Count <= 10 && key.KeyChar >= '1' && key.KeyChar <= (char)('0' + filteredChoices.Count))
            {
                int idx = key.KeyChar - '1';
                // Safe cursor positioning for exit
                int exitRow = Math.Min(inputTop + 1, Height - 1);
                SetCursorPosition(0, exitRow);
                return filteredChoices[idx];
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (filter.Length > 0)
                {
                    filter = filter.Substring(0, filter.Length - 1);
                    filteredChoices = originalChoices.Where(c => c.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (filteredSelected >= filteredChoices.Count) filteredSelected = Math.Max(0, filteredChoices.Count - 1);
                    scrollOffset = 0; // Reset scroll when filtering
                    DrawMenu();
                }
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                string newFilter = filter + key.KeyChar;
                var newFiltered = originalChoices.Where(c => c.IndexOf(newFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (newFiltered.Count > 0)
                {
                    filter = newFilter;
                    filteredChoices = newFiltered;
                    filteredSelected = 0;
                    scrollOffset = 0; // Reset scroll when filtering
                    DrawMenu();
                }
                // else: ignore input that would result in no options
            }
        }
    }

    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);

    public void RenderChatMessage(ChatMessage message)
    {
        string timestamp = message.CreatedAt.ToString("HH:mm:ss");
        string roleIndicator;
        ConsoleColor roleColor, textColor = ForegroundColor;

        switch (message.Role)
        {
            case Roles.Tool:
                roleIndicator = "[TOOL]";
                roleColor = ConsoleColor.Yellow;
                textColor = ConsoleColor.DarkGray; // Tool messages in dark gray
                break;
            case Roles.System:
                roleIndicator = "[SYSTEM]";
                roleColor = ConsoleColor.DarkBlue;
                textColor = ConsoleColor.DarkGray; // System messages in gray
                break;
            case Roles.User:
                roleIndicator = "[USER]";
                roleColor = ConsoleColor.Cyan;
                break;
            case Roles.Assistant:
                roleIndicator = "[ASSISTANT]";
                roleColor = ConsoleColor.Green;
                break;
            default:
                roleIndicator = "[UNKNOWN]";
                roleColor = ConsoleColor.Red;
                break;
        }

        // For new messages in the main loop, show all messages with timestamp and role formatting
        // Render timestamp in gray
        ForegroundColor = ConsoleColor.Gray;
        Write(timestamp);
        Write(" ");

        // Render role indicator in role-specific color
        ForegroundColor = roleColor;
        Write(roleIndicator);
        Write(" ");

        // Render content
        ForegroundColor = textColor; // Reset to original text color
        WriteLine(message.Content);
        ResetColor();
    }

    public void RenderChatHistory(IEnumerable<ChatMessage> messages)
    {
        WriteLine("Chat History:");
        WriteLine(new string('-', 50));

        foreach (var message in messages)
        {
            // Skip empty system messages in history view
            if (message.Role == Roles.System && string.IsNullOrWhiteSpace(message.Content))
                continue;

            RenderChatMessage(message);
        }

        WriteLine(new string('-', 50));
    }

    public int CursorTop { get => Console.CursorTop; }
    public int CursorLeft { get => Console.CursorLeft; }

    public int Width { get => Console.WindowWidth; }

    public int Height { get => Console.WindowHeight; }

    public bool CursorVisible { set => Console.CursorVisible = value; }
    public bool KeyAvailable { get => Console.KeyAvailable; }

    public bool IsOutputRedirected { get; } = Console.IsOutputRedirected;
    public void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);

    public ConsoleColor ForegroundColor
    {
        get => Console.ForegroundColor;
        set => Console.ForegroundColor = value;
    }

    public ConsoleColor BackgroundColor
    {
        get => Console.BackgroundColor;
        set => Console.BackgroundColor = value;
    }

    public void ResetColor() => Console.ResetColor();

    public void Write(string text) => Console.Write(text);
    public void WriteLine(string? text = null) => Console.WriteLine(text);

    public void Clear() => Console.Clear();

    public Task RunAsync(Func<Task> appMain) => appMain();
}