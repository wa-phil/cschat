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

    public static async Task<string> SelectModelAsync()
    {
        var models = await GetAvailableModelsAsync();
        if (models.Count == 0)
        {
            Console.WriteLine("No models available.");
            return null;
        }

        Console.WriteLine("Available models:");
        var selected = User.RenderMenu(models, models.IndexOf(config.Model));
        return selected;
    }

    public static List<ChatMessage> history = new List<ChatMessage>
    {
        new ChatMessage { Role = "system", Content = config.SystemPrompt }
    };

    public static CommandManager commandManager = CommandManager.CreateDefaultCommands();

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
        Console.WriteLine("Type your message and press Enter. Press '/' to bring up available commands, /? for help. (Shift+Enter for new line)");
        Console.WriteLine();

        while (true)
        {
            Console.Write("> ");
            var userInput = await User.ReadInputWithFeaturesAsync(commandManager);
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