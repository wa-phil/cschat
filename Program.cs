using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mono.Options;
using TinyJson;

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
    static string ConfigFilePath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ollama_config.json");

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
            ShowHelp();
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
        Console.WriteLine("Type your message and press Enter. Commands: /model /host /exit /?");
        Console.WriteLine();

        var history = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = config.SystemPrompt }
        };

        while (true)
        {
            Console.Write("> ");
            var userInput = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userInput)) continue;

            if (userInput.StartsWith("/"))
            {
                if (userInput.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (userInput.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
                    userInput.Equals("/help", StringComparison.OrdinalIgnoreCase))
                {
                    ShowHelp();
                    continue;
                }

                if (userInput.Equals("/model", StringComparison.OrdinalIgnoreCase))
                {
                    var selected = await SelectModelAsync();
                    if (selected != null)
                    {
                        config.Model = selected;
                        SaveConfig(config);
                        Console.WriteLine($"Switched to model '{selected}'");
                        history = new List<ChatMessage> { new ChatMessage { Role = "system", Content = config.SystemPrompt } };
                    }
                    continue;
                }

                if (userInput.Equals("/host", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("Enter new Ollama host: ");
                    var hostInput = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(hostInput))
                    {
                        config.Host = hostInput.Trim();
                        SaveConfig(config);
                        Console.WriteLine($"Switched to host '{config.Host}'");
                    }
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

    static void ShowHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  /model     - List and select available models");
        Console.WriteLine("  /host      - Change Ollama host");
        Console.WriteLine("  /exit      - Quit the application");
        Console.WriteLine("  /? or /help - Show this help message");
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