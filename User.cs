using System;
using System.Linq;
using System.Collections.Generic;

public class User
{
    // Renders a menu at the current cursor position, allows arrow key navigation, and returns the selected string or null if cancelled
    public static string? RenderMenu(string header, List<string> choices, int selected = 0) // Allow nullable return type to handle null cases
    {
        // Always print enough newlines to ensure space for the menu
        Console.WriteLine(header);
        int menuLines = choices.Count;
        for (int i = 0; i < menuLines; i++)
        {
            Console.WriteLine();
        }
        int menuTop = Console.CursorTop - menuLines;

        string filter = "";
        int inputTop = menuTop + choices.Count + 1;
        List<string> filteredChoices = new List<string>(choices);
        int filteredSelected = selected;

        void DrawMenu()
        {
            for (int i = 0; i < filteredChoices.Count; i++)
            {
                Console.SetCursorPosition(0, menuTop + i);
                string line;
                if (i == filteredSelected)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                    if (filteredChoices.Count <= 9)
                        line = $"> [{i + 1}] {filteredChoices[i]} ";
                    else
                        line = $"> {filteredChoices[i]} ";
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.BackgroundColor = ConsoleColor.Black;
                    if (filteredChoices.Count <= 9)
                        line = $"  [{i + 1}] {filteredChoices[i]} ";
                    else
                        line = $"  {filteredChoices[i]} ";
                }
                Console.Write(line.PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
            }
            // Clear any leftover menu lines
            for (int i = filteredChoices.Count; i < menuLines; i++)
            {
                Console.SetCursorPosition(0, menuTop + i);
                Console.Write(new string(' ', Console.WindowWidth - 1));
            }
            // Draw input header
            Console.SetCursorPosition(0, inputTop);
            Console.Write($"|> {filter}".PadRight(Console.WindowWidth - 1));
            Console.SetCursorPosition(3 + filter.Length, inputTop);
        }

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
            else if (key.Key == ConsoleKey.DownArrow)
            {
                if (filteredSelected < filteredChoices.Count - 1) filteredSelected++;
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                Console.SetCursorPosition(0, inputTop + 1);
                if (filteredChoices.Count > 0)
                    return filteredChoices[filteredSelected];
                else
                    return null;
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                Console.SetCursorPosition(0, inputTop + 1);
                Console.WriteLine("Selection cancelled.");
                return null;
            }
            else if (filteredChoices.Count <= 9 && key.KeyChar >= '1' && key.KeyChar <= (char)('0' + filteredChoices.Count))
            {
                int idx = key.KeyChar - '1';
                Console.SetCursorPosition(0, inputTop + 1);
                return filteredChoices[idx];
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (filter.Length > 0)
                {
                    filter = filter.Substring(0, filter.Length - 1);
                    filteredChoices = choices.Where(c => c.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (filteredSelected >= filteredChoices.Count) filteredSelected = Math.Max(0, filteredChoices.Count - 1);
                    DrawMenu();
                }
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                string newFilter = filter + key.KeyChar;
                var newFiltered = choices.Where(c => c.IndexOf(newFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (newFiltered.Count > 0)
                {
                    filter = newFilter;
                    filteredChoices = newFiltered;
                    filteredSelected = 0;
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
                Console.WriteLine();
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
                Console.Write("> ");
                buffer.Clear();
                cursor = 0;
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
}
