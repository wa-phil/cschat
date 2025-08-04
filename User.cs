using System;
using System.Linq;
using System.Collections.Generic;

public class User
{
    private static string? lastInput = null;
    
    // Renders a menu at the current cursor position, allows arrow key navigation, and returns the selected string or null if cancelled
    public static string? RenderMenu(string header, List<string> choices, int selected = 0) // Allow nullable return type to handle null cases
    {
        int actualMaxVisibleItems = Program.config.MaxMenuItems;
        // Store original choices for scrolling
        var originalChoices = new List<string>(choices);
        int originalSelected = selected;
    
        // always position the menu at the top    
        Console.Clear();
        Console.SetCursorPosition(0, 0);
        
        // Print header
        Console.WriteLine(header);
        
        // Calculate scrolling parameters
        int scrollOffset = 0;
        int visibleItems = Math.Min(originalChoices.Count, actualMaxVisibleItems);
        bool hasMoreAbove = false;
        bool hasMoreBelow = false;
        
        // Reserve space for menu lines, indicators, and input
        int indicatorLines = 2; // up to 2 lines for "more above/below" indicators
        int inputLines = 1; // input line
        int menuLines = visibleItems + indicatorLines;
        
        // Print placeholder lines for the menu area
        for (int i = 0; i < menuLines + inputLines; i++)
        {
            Console.WriteLine();
        }
        
        int menuStartRow = Console.CursorTop - (menuLines + inputLines);
        int inputTop = Console.CursorTop - inputLines;

        string filter = "";
        List<string> filteredChoices = new List<string>(originalChoices);
        int filteredSelected = originalSelected;

        // Function to calculate which items to show and update scroll indicators
        void UpdateScrollView()
        {
            visibleItems = Math.Min(filteredChoices.Count, actualMaxVisibleItems);
            
            // Adjust scroll offset to keep selected item visible
            if (filteredSelected < scrollOffset)
            {
                scrollOffset = filteredSelected;
            }
            else if (filteredSelected >= scrollOffset + visibleItems)
            {
                scrollOffset = filteredSelected - visibleItems + 1;
            }
            
            // Ensure scroll offset is within bounds
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, Math.Max(0, filteredChoices.Count - visibleItems)));
            
            hasMoreAbove = scrollOffset > 0;
            hasMoreBelow = scrollOffset + visibleItems < filteredChoices.Count;
        }

        void DrawMenu() => Log.Method(ctx =>
        {
            ctx.OnlyEmitOnFailure();
            ctx.Append(Log.Data.ConsoleHeight, Console.BufferHeight);
            ctx.Append(Log.Data.ConsoleWidth, Console.WindowWidth);
            
            UpdateScrollView();
            
            // Ensure we don't exceed console buffer bounds
            int maxRow = Console.BufferHeight - 1;
            int currentRow = menuStartRow;

            // Draw "more above" indicator if needed
            if (hasMoreAbove && currentRow < maxRow)
            {
                Console.SetCursorPosition(0, currentRow);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var countAbove = Math.Min(scrollOffset, filteredChoices.Count);
                Console.Write($"^^^ {countAbove} items above ^^^".PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
                currentRow++;
            }

            // Draw visible menu items
            for (int i = 0; i < visibleItems && currentRow < maxRow; i++)
            {
                int choiceIndex = scrollOffset + i;
                if (choiceIndex >= filteredChoices.Count) break;

                ctx.Append(Log.Data.MenuTop, currentRow);
                Console.SetCursorPosition(0, currentRow);
                string line;
                
                bool isSelected = choiceIndex == filteredSelected;
                if (isSelected)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                    if (filteredChoices.Count <= 9)
                        line = $"> [{choiceIndex + 1}] {filteredChoices[choiceIndex]} ";
                    else
                        line = $"> {filteredChoices[choiceIndex]} ";
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.BackgroundColor = ConsoleColor.Black;
                    if (filteredChoices.Count <= 9)
                        line = $"  [{choiceIndex + 1}] {filteredChoices[choiceIndex]} ";
                    else
                        line = $"  {filteredChoices[choiceIndex]} ";
                }
                Console.Write(line.PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
                currentRow++;
            }

            // Draw "more below" indicator if needed
            if (hasMoreBelow && currentRow < maxRow)
            {
                Console.SetCursorPosition(0, currentRow);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var countBelow = Math.Max(0, filteredChoices.Count - (scrollOffset + visibleItems));
                Console.Write($"vvv {countBelow} items below vvv".PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
                currentRow++;
            }

            // Clear any leftover lines in the menu area
            while (currentRow < inputTop && currentRow < maxRow)
            {
                Console.SetCursorPosition(0, currentRow);
                Console.Write(new string(' ', Console.WindowWidth - 1));
                currentRow++;
            }

            // Draw input header only if it fits in the buffer
            if (inputTop < maxRow)
            {
                ctx.Append(Log.Data.InputTop, inputTop);
                Console.SetCursorPosition(0, inputTop);
                var inputHeader = "[filter]> ";
                Console.Write($"{inputHeader}{filter}".PadRight(Console.WindowWidth - 1));
                if (inputHeader.Length + filter.Length < Console.WindowWidth && inputTop < maxRow)
                {
                    Console.SetCursorPosition(inputHeader.Length + filter.Length, inputTop);
                }
            }
            ctx.Succeeded();
        });

        DrawMenu();
        ConsoleKeyInfo key;
        while (true)
        {
            key = Console.ReadKey(true);
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
                int exitRow = Math.Min(inputTop + 1, Console.BufferHeight - 1);
                Console.SetCursorPosition(0, exitRow);
                if (filteredChoices.Count > 0)
                    return filteredChoices[filteredSelected];
                else
                    return null;
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                // Safe cursor positioning for exit
                int exitRow = Math.Min(inputTop + 1, Console.BufferHeight - 1);
                Console.SetCursorPosition(0, exitRow);
                Console.WriteLine("Selection cancelled.");
                return null;
            }
            else if (filteredChoices.Count <= 10 && key.KeyChar >= '1' && key.KeyChar <= (char)('0' + filteredChoices.Count))
            {
                int idx = key.KeyChar - '1';
                // Safe cursor positioning for exit
                int exitRow = Math.Min(inputTop + 1, Console.BufferHeight - 1);
                Console.SetCursorPosition(0, exitRow);
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

    public static async Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager) // Allow nullable return type
    {
        var buffer = new List<char>();
        var lines = new List<string>();
        int cursor = 0;
        ConsoleKeyInfo key;

        while (true)
        {
            key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                // Soft new line
                lines.Add(new string(buffer.ToArray()));
                buffer.Clear();
                cursor = 0;
                Console.Write("\n> ");
                continue;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                // erase the contents of the current line, and do not advance to the next line
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write(new string(' ', Console.WindowWidth - 1));
                Console.SetCursorPosition(0, Console.CursorTop);
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buffer.RemoveAt(cursor - 1);
                    cursor--;
                    Console.Write("\b \b");
                }
                continue;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                var result = await commandManager.Action();
                if (result == Command.Result.Failed)
                {
                    Console.WriteLine("Command failed.");
                }
                Console.Write("[press ESC to open menu]\n> ");
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
                        Console.Write("\b \b");
                    }
                    
                    // Set buffer to last input
                    buffer.Clear();
                    buffer.AddRange(lastInput.ToCharArray());
                    cursor = buffer.Count;
                    
                    // Display the recalled input
                    Console.Write(lastInput);
                }
                continue;
            }
            if (key.KeyChar != '\0')
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Console.Write(key.KeyChar);
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

    public static async Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory)
    {
        await Task.CompletedTask; // Simulate asynchronous behavior
        var buffer = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace && buffer.Count > 0)
            {
                buffer.RemoveAt(buffer.Count - 1);
                Console.Write("\b \b");
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
                    for (int i = 0; i < partial.Length; i++) Console.Write("\b \b");
                    buffer.RemoveRange(buffer.Count - partial.Length, partial.Length);
                    buffer.AddRange(Path.GetFileName(completion));
                    Console.Write(Path.GetFileName(completion));
                }
                else if (matches.Count > 1)
                {
                    Console.WriteLine();
                    Console.WriteLine("Matches:");
                    matches.ForEach(m => Console.WriteLine("  " + m));
                    Console.Write("> " + new string(buffer.ToArray()));
                }
            }
            else if (key.KeyChar != '\0')
            {
                buffer.Add(key.KeyChar);
                Console.Write(key.KeyChar);
            }
        }

        var result = new string(buffer.ToArray());
        return string.IsNullOrWhiteSpace(result) ? null : Path.GetFullPath(result);
    }

    public static string? ReadLineWithHistory()
    {
        var buffer = new List<char>();
        int cursor = 0;
        ConsoleKeyInfo key;

        while (true)
        {
            key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buffer.RemoveAt(cursor - 1);
                    cursor--;
                    Console.Write("\b \b");
                }
            }
            else if (key.Key == ConsoleKey.UpArrow)
            {
                if (lastInput != null)
                {
                    // Clear current buffer display
                    for (int i = 0; i < buffer.Count; i++)
                    {
                        Console.Write("\b \b");
                    }
                    
                    // Set buffer to last input
                    buffer.Clear();
                    buffer.AddRange(lastInput.ToCharArray());
                    cursor = buffer.Count;
                    
                    // Display the recalled input
                    Console.Write(lastInput);
                }
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Console.Write(key.KeyChar);
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

    // Shared infrastructure for rendering chat messages with timestamps and role indicators
    public static void RenderChatMessage(ChatMessage message)
    {
        string timestamp = message.CreatedAt.ToString("HH:mm:ss");
        string roleIndicator;
        ConsoleColor roleColor, textColor = Console.ForegroundColor;
        
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
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(timestamp);
        Console.Write(" ");
        
        // Render role indicator in role-specific color
        Console.ForegroundColor = roleColor;
        Console.Write(roleIndicator);
        Console.Write(" ");

        // Render content
        Console.ForegroundColor = textColor; // Reset to original text color
        Console.WriteLine(message.Content);
        Console.ResetColor();
    }
    
    public static void RenderChatHistory(IEnumerable<ChatMessage> messages)
    {
        Console.WriteLine("Chat History:");
        Console.WriteLine(new string('-', 50));
        
        foreach (var message in messages)
        {
            // Skip empty system messages in history view
            if (message.Role == Roles.System && string.IsNullOrWhiteSpace(message.Content))
                continue;
                
            RenderChatMessage(message);
        }
        
        Console.WriteLine(new string('-', 50));
    }
}
