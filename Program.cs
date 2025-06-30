using System;
using TinyJson;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Options;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;


class ChatMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}

static class Program
{
    public static string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ollama_config.json");

    public static Config config = Config.Load(ConfigFilePath);

    public static async Task<List<string>> GetAvailableModelsAsync()
    {
        using var client = new HttpClient();
        try
        {
            var resp = await client.GetStringAsync($"{config.Host}/api/tags");
            dynamic parsed = resp.FromJson<dynamic>();
            var models = new List<string>();
            foreach (var model in parsed["models"])
            {
                models.Add((string)model["name"]);
            }
            return models;
        }
        catch
        {
            Console.WriteLine("Failed to fetch models from host.");
            return new List<string>();
        }
    }

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

    public static async Task<string> SelectModelAsync()
    {
        var models = await GetAvailableModelsAsync();
        if (models.Count == 0)
        {
            Console.WriteLine("No models available.");
            return null;
        }

        Console.WriteLine("Available models:");
        var selected = RenderMenu(models, models.IndexOf(config.Model));
        return selected;
    }

    public static List<ChatMessage> history = new List<ChatMessage>
    {
        new ChatMessage { Role = "system", Content = config.SystemPrompt }
    };

    public static CommandManager commandManager = CommandManager.CreateDefaultCommands();

    static async Task<string> ReadInputWithFeaturesAsync()
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

    static async Task Main(string[] args)
    {
        bool showHelp = false;

        var options = new OptionSet {
            { "h|host=", "Ollama server host (default: http://localhost:11434)", v => { if (v != null) config.Host = v; } },
            { "m|model=", "Model name", v => { if (v != null) config.Model = v; } },
            { "s|system=", "System prompt", v => { if (v != null) config.SystemPrompt = v; } },
            { "?|help", "Show help", v => showHelp = v != null }
        };

        options.Parse(args);

        if (showHelp)
        {
            options.WriteOptionDescriptions(Console.Out);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.Model))
        {
            var selected = await SelectModelAsync();
            if (selected == null) return;
            config.Model = selected;
        }

        Config.Save(config, ConfigFilePath);

        Console.WriteLine($"Connecting to Ollama at {config.Host} using model '{config.Model}'");
        Console.WriteLine("Type your message and press Enter. Commands: /model /host /exit /? (Shift+Enter for new line)");
        Console.WriteLine();

        while (true)
        {
            Console.Write("> ");
            var userInput = await ReadInputWithFeaturesAsync();
            if (string.IsNullOrWhiteSpace(userInput)) continue;

            if (userInput.StartsWith("/"))
            {
                var cmdName = userInput.Split(' ')[0].Substring(1);
                var cmd = commandManager.Find("/" + cmdName);
                if (cmd != null)
                {
                    await cmd.Action();
                    continue;
                }
                Console.WriteLine("Unknown command. Type /? for help.");
                continue;
            }

            var response = await PostChat(history, userInput);
            Console.WriteLine(response);
            history.Add(new ChatMessage { Role = "assistant", Content = response });
        }
    }

    static async Task<string> PostChat(List<ChatMessage> history, string userInput)
    {
        history.Add(new ChatMessage { Role = "user", Content = userInput });
        using var client = new HttpClient();
        var requestBody = new
        {
            model = config.Model,
            messages = history.ToArray()
        };

        var content = new StringContent(requestBody.ToJson(), Encoding.UTF8, "application/json");
        try
        {
            var response = await client.PostAsync($"{config.Host}/v1/chat/completions", content);
            if (!response.IsSuccessStatusCode)
                return $"Error: {response.StatusCode}";

            var respJson = await response.Content.ReadAsStringAsync();
            dynamic respObj = respJson.FromJson<dynamic>();
            return respObj["choices"][0]["message"]["content"];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception occurred: {ex.Message}");
            throw;
        }
    }
}