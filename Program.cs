using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mono.Options;
using TinyJson;
using System.Linq;

class OllamaConfig
{
    public string Host { get; set; } = "http://localhost:11434";
    public string Model { get; set; }
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
}

class ChatMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}

static class Program
{
    static string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ollama_config.json");

    static OllamaConfig config = LoadConfig();

    static OllamaConfig LoadConfig()
    {
        if (File.Exists(ConfigFilePath))
        {
            Console.WriteLine($"Loading configuration from {ConfigFilePath}");
            var json = File.ReadAllText(ConfigFilePath);
            Console.WriteLine($"Configuration loaded: {json}");
            return json.FromJson<OllamaConfig>() ?? new OllamaConfig();
        }
        return new OllamaConfig();
    }

    static void SaveConfig(OllamaConfig config)
    {
        Console.WriteLine($"Saving configuration to {ConfigFilePath}");
        var json = config.ToJson();
        File.WriteAllText(ConfigFilePath, json);
    }

    static async Task<List<string>> GetAvailableModelsAsync()
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

    static async Task<string> SelectModelAsync()
    {
        var models = await GetAvailableModelsAsync();
        if (models.Count == 0)
        {
            Console.WriteLine("No models available.");
            return null;
        }

        Console.WriteLine("Available models:");
        for (int i = 0; i < models.Count; i++)
        {
            Console.WriteLine($"  [{i}] {models[i]}");
        }

        Console.Write("Select a model by number: ");
        if (int.TryParse(Console.ReadLine(), out int index) && index >= 0 && index < models.Count)
        {
            return models[index];
        }

        Console.WriteLine("Invalid selection.");
        return null;
    }

    static List<ChatMessage> history = new List<ChatMessage>
    {
        new ChatMessage { Role = "system", Content = config.SystemPrompt }
    };

    static CommandManager commandManager = new CommandManager(new[]
        {
            new Command
            {
                Name = "clear", Description = "clear chat history",
                Action = async () =>
                {
                    history.Clear();
                    history.Add(new ChatMessage { Role = "system", Content = config.SystemPrompt });
                    Console.WriteLine("Chat history cleared.");
                }
            },
            new Command
            {
                Name = "history", Description = "Show chat history",
                Action = async () =>
                {
                    Console.WriteLine("Chat History:");
                    foreach (var msg in history)
                    {
                        Console.WriteLine($"{msg.Role}: {msg.Content}");
                    }
                }
            },
            new Command
            {
                Name = "model", Description = "List and select available models",
                Action = async () =>
                {
                    var selected = await SelectModelAsync();
                    if (selected != null)
                    {
                        config.Model = selected;
                        SaveConfig(config);
                        Console.WriteLine($"Switched to model '{selected}'");
                    }
                }
            },
            new Command
            {
                Name = "host", Description = "Change Ollama host",
                Action = async () =>
                {
                    Console.Write("Enter new Ollama host: ");
                    var hostInput = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(hostInput))
                    {
                        config.Host = hostInput.Trim();
                        SaveConfig(config);
                        Console.WriteLine($"Switched to host '{config.Host}'");
                    }
                }
            },
            new Command
            {
                Name = "exit", Description = "Quit the application",
                Action = () => { Environment.Exit(0); return Task.CompletedTask; }
            },
            new Command
            {
                Name = "?", Description = "Show this help message",
                Action = () => { commandManager.ShowHelp(); return Task.CompletedTask; }
            },
            new Command
            {
                Name = "help", Description = "Show this help message",
                Action = () => { commandManager.ShowHelp(); return Task.CompletedTask; }
            }
        });

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

        SaveConfig(config);

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