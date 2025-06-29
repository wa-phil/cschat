using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;
using TinyJson;

class OllamaConfig
{
    public string Host { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma3:27b";
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
            var json = File.ReadAllText(ConfigFilePath);
            return json.FromJson<OllamaConfig>() ?? new OllamaConfig();
        }
        return new OllamaConfig();
    }

    static void SaveConfig(OllamaConfig config)
    {
        var json = config.ToJson();
        File.WriteAllText(ConfigFilePath, json);
    }

    static async Task Main(string[] args)
    {
        bool showHelp = false;
        var options = new OptionSet {
            { "h|host=", "Ollama server host (default: http://localhost:11434)", v => { if (v != null) config.Host = v; } },
            { "m|model=", "Model name (default: llama2)", v => { if (v != null) config.Model = v; } },
            { "s|system=", "System prompt", v => { if (v != null) config.SystemPrompt = v; } },
            { "?|help", "Show help", v => showHelp = v != null }
        };

        options.Parse(args);

        if (showHelp)
        {
            options.WriteOptionDescriptions(Console.Out);
            return;
        }

        SaveConfig(config);

        Console.WriteLine($"Connecting to Ollama at {config.Host} using model '{config.Model}'");
        Console.WriteLine("Type your message and press Enter (Ctrl+C to exit):");

        var history = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = config.SystemPrompt }
        };

        while (true)
        {
            Console.Write("> ");
            var userInput = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userInput)) continue;

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
        var response = await client.PostAsync($"{config.Host}/v1/chat/completions", content);
        if (!response.IsSuccessStatusCode)
            return $"Error: {response.StatusCode}";

        var respJson = await response.Content.ReadAsStringAsync();
        // Simple extraction of the assistant's reply (adjust as needed for Ollama's response format)
        dynamic respObj = respJson.FromJson<dynamic>();
        try
        {
            return respObj["choices"][0]["message"]["content"];
        }
        catch
        {
            return respJson;
        }
    }
}
