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
        new ChatMessage { Role = Roles.System, Content = config.SystemPrompt }
    };
    public static CommandManager commandManager = CommandManager.CreateDefaultCommands();
    public static Dictionary<string, Type> Providers = DiscoverProviders();
    public static IChatProvider Provider = null;

    static Dictionary<string, Type> DiscoverProviders() =>
        Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IChatProvider).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => new {
                Type = t,
                Attr = t.GetCustomAttribute<ProviderNameAttribute>()
            })
            .Where(x => x.Attr != null)
            .ToDictionary(x => x.Attr.Name, x => x.Type, StringComparer.OrdinalIgnoreCase);

    public static void SetProvider(string providerName)
    {
        if (Providers.TryGetValue(providerName, out var type))
        {
            Provider = (IChatProvider)Activator.CreateInstance(type, new object[] { config });
        }
        else if (Providers.Count > 0)
        {
            var first = Providers.First();
            Provider = (IChatProvider)Activator.CreateInstance(first.Value, new object[] { config });
        }
        else
        {
            Provider = null;
        }
        config.Provider = providerName;
    }

    public static async Task<string> SelectModelAsync()
    {
        var models = await Provider.GetAvailableModelsAsync();
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

        Console.WriteLine($"Connecting to {config.Provider} at {config.Host} using model '{config.Model}'");
        Console.WriteLine("Type your message and press Enter. Press '/' to bring up available commands, /? for help. (Shift+Enter for new line)");
        Console.WriteLine();
        while (true)
        {
            Console.Write("> ");
            var userInput = await User.ReadInputWithFeaturesAsync(commandManager);
            if (string.IsNullOrWhiteSpace(userInput)) continue;
            history.Add(new ChatMessage { Role = Roles.User, Content = userInput });
            string response = await Provider.PostChatAsync(history);
            Console.WriteLine(response);
            history.Add(new ChatMessage { Role = Roles.Assistant, Content = response });
        }
    }
}