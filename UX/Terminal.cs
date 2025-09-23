using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
public class Terminal : IUi
{
    private string? lastInput = null;

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

                // read line with ESC cancel (reuse your input loop style)
                var buffer = new List<char>();
                while (true)
                {
                    var k = ReadKey(intercept: true);
                    if (k.Key == ConsoleKey.Escape) { WriteLine("\n(cancelled)"); return false; }
                    if (k.Key == ConsoleKey.Enter) { WriteLine(); break; }
                    if (k.Key == ConsoleKey.Backspace && buffer.Count > 0) { buffer.RemoveAt(buffer.Count-1); Write("\b \b"); continue; }
                    if (k.KeyChar != '\0' && !char.IsControl(k.KeyChar)) { buffer.Add(k.KeyChar); Write(k.KeyChar.ToString()); }
                }

                var raw = new string(buffer.ToArray());
                if (string.IsNullOrWhiteSpace(raw)) raw = current; // leave as-is if blank

                if (!f.TrySetFromString(form.Model!, raw, out var err))
                {
                    WriteLine($"  {err}");
                    continue;
                }

                break;
            }
        }
        return true;
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

    public string? ReadLineWithHistory()
    {
        var buffer = new List<char>();
        int cursor = 0;
        ConsoleKeyInfo key;

        while (true)
        {
            key = ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buffer.RemoveAt(cursor - 1);
                    cursor--;
                    Write("\b \b");
                }
            }
            else if (key.Key == ConsoleKey.UpArrow)
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
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Write(key.KeyChar.ToString());
            }
        }

        var input = new string(buffer.ToArray()).Trim();

        // Store the input for history if it's not empty
        if (!string.IsNullOrWhiteSpace(input))
        {
            lastInput = input;
        }

        return string.IsNullOrWhiteSpace(input) ? null : input;
    }

    public string ReadLine() => Console.ReadLine() ?? string.Empty;

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