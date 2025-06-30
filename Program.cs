using System;
using System.IO;
using System.Linq;
using Mono.Options;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;


static class Program
{
    public static string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    public static Config config = Config.Load(ConfigFilePath);
    public static List<ChatMessage> history = new List<ChatMessage>
    {
        new ChatMessage { Role = "system", Content = config.SystemPrompt }
    };
    public static CommandManager commandManager = CommandManager.CreateDefaultCommands();
    public static List<IChatProvider> Providers = DiscoverProviders();
    public static IChatProvider Provider = null;

    static List<IChatProvider> DiscoverProviders() =>
        Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IChatProvider).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IChatProvider)Activator.CreateInstance(t))
            .ToList();

    public static void SetProvider(string providerName)
    {
        Provider = Providers.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (Provider == null && Providers.Count > 0)
        {
            Provider = Providers[0];
        }
        config.Provider = Provider?.Name;
    }

    public static async Task<string> SelectModelAsync()
    {
        var models = await Provider.GetAvailableModelsAsync(config);
        if (models == null || models.Count == 0)
        {
            Console.WriteLine("No models available.");
            return null;
        }
        Console.WriteLine("Available models:");
        var selected = User.RenderMenu(models, models.IndexOf(config.Model));
        return selected;
    }

    static async Task Main(string[] args)
    {
        bool showHelp = false;
        var options = new OptionSet {
            { "h|host=", "Ollama server host (default: http://localhost:11434)", v => { if (v != null) config.Host = v; } },
            { "m|model=", "Model name", v => { if (v != null) config.Model = v; } },
            { "s|system=", "System prompt", v => { if (v != null) config.SystemPrompt = v; } },
            { "p|provider=", "Provider name", v => { if (v != null) SetProvider(v); } },
            { "?|help", "Show help", v => showHelp = v != null }
        };
        options.Parse(args);
        if (showHelp)
        {
            options.WriteOptionDescriptions(Console.Out);
            return;
        }
        
        SetProvider(config.Provider);

        if (string.IsNullOrWhiteSpace(config.Model))
        {
            var selected = await SelectModelAsync();
            if (selected == null) return;
            config.Model = selected;
        }

        Config.Save(config, ConfigFilePath);

        Console.WriteLine($"Connecting to {Provider.Name} at {config.Host} using model '{config.Model}'");
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
            string response = await Provider.PostChatAsync(config, history, userInput);
            Console.WriteLine(response);
            history.Add(new ChatMessage { Role = "assistant", Content = response });
        }
    }
}