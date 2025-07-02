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

        // Now set menuTop to the first menu line
        int menuTop = Console.CursorTop - menuLines;

        void DrawMenu()
        {
            for (int i = 0; i < choices.Count; i++)
            {
                Console.SetCursorPosition(0, menuTop + i);
                string line;
                if (i == selected)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                    line = $"> [{i}] {choices[i]} ";
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.BackgroundColor = ConsoleColor.Black;
                    line = $"  [{i}] {choices[i]} ";
                }
                Console.Write(line.PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
            }
            // Move cursor below menu
            Console.SetCursorPosition(0, menuTop + choices.Count);
        }

        DrawMenu();

        ConsoleKeyInfo key;
        while (true)
        {
            key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (selected > 0) selected--;
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                if (selected < choices.Count - 1) selected++;
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                Console.SetCursorPosition(0, menuTop + choices.Count);
                return choices[selected];
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                Console.SetCursorPosition(0, menuTop + choices.Count);
                Console.WriteLine("Selection cancelled.");
                return null;
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
            if (key.KeyChar != '\0')
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Console.Write(key.KeyChar);
                if (cursor == 1 && key.KeyChar == '/')
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
