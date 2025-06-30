using System;
using System.Linq;
using System.Collections.Generic;

public class User
{
    // Renders a menu at the current cursor position, allows arrow key navigation, and returns the selected string or null if cancelled
    public static string RenderMenu(List<string> choices, int selected = 0)
    {
        // Always print enough newlines to ensure space for the menu
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

    public static async Task<string> ReadInputWithFeaturesAsync(CommandManager commandManager)
    {
        var buffer = new List<char>();
        var lines = new List<string>();
        bool isCommand = false;
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
            if (key.Key == ConsoleKey.Tab)
            {
                var current = new string(buffer.ToArray());
                if (current.StartsWith("/"))
                {
                    var completions = commandManager.GetCompletions(current);
                    var match = completions.FirstOrDefault();
                    if (match != null)
                    {
                        // Complete the command
                        for (int i = cursor; i < current.Length; i++)
                            Console.Write("\b \b");
                        buffer.Clear();
                        buffer.AddRange(match);
                        cursor = buffer.Count;
                        Console.Write(match.Substring(current.Length));
                    }
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
                    // Show available commands
                    Console.WriteLine();
                    Console.WriteLine("Available commands:");
                    foreach (var cmd in commandManager.GetAll())
                        Console.WriteLine($"  /{cmd.Name} - {cmd.Description}");
                    Console.Write("> " + new string(buffer.ToArray()));
                }
            }
        }
        lines.Add(new string(buffer.ToArray()));
        return string.Join("\n", lines).Trim();
    }
}
