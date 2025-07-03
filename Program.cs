using System;
using System.IO;
using System.Linq;
using Mono.Options;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;


static class Program
{
    public static string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    public static Config config = null!;
    public static Memory memory = null!;
    public static CommandManager commandManager = null!;
    public static ServiceProvider? serviceProvider = null!;
    public static Dictionary<string, Type> Providers
    {
        get => Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IChatProvider).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => new {
                Type = t,
                Attr = t.GetCustomAttribute<ProviderNameAttribute>()
            })
            .Where(x => x.Attr != null)
            .ToDictionary(x => x.Attr?.Name ?? string.Empty, x => x.Type, StringComparer.OrdinalIgnoreCase);
    }

    static void InitProgram()
    {
        config = Config.Load(ConfigFilePath);
        Log.Initialize();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(config); // Register the config instance
        // Register all IChatProvider implementations
        foreach (var providerType in Providers.Values)
        {
            serviceCollection.AddTransient(providerType);
        }

        serviceProvider = serviceCollection.BuildServiceProvider(); // Build the service provider
        memory = new Memory(config.SystemPrompt);
        commandManager = CommandManager.CreateDefaultCommands();
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine($"Console# Chat v{BuildInfo.GitVersion} ({BuildInfo.GitCommitHash})");
        InitProgram();

        bool showHelp = false;
        var options = new OptionSet {
            { "h|host=", "Server host (default: http://localhost:11434)", v => { if (v != null) config.Host = v; } },
            { "m|model=", "Model name", v => { if (v != null) config.Model = v; } },
            { "s|system=", "System prompt", v => { if (v != null) config.SystemPrompt = v; } },
            { "p|provider=", "Provider name (default: ollama)", v => { if (v != null) config.Provider = v; } },
            { "?|help", "Show help", v => showHelp = v != null }
        };
        options.Parse(args);
        if (showHelp)
        {
            options.WriteOptionDescriptions(Console.Out);
            return;
        }

        Engine.SetProvider(config.Provider);

        if (string.IsNullOrWhiteSpace(config.Model))
        {
            var selected = await Engine.SelectModelAsync();
            if (selected == null) return;
            config.Model = selected;
        }

        Config.Save(config, ConfigFilePath);

        Console.WriteLine($"Connecting to {config.Provider} at {config.Host} using model '{config.Model}'");
        Console.WriteLine("Type your message and press Enter. Press the escape key for the menu. (Shift+Enter for new line)");
        Console.WriteLine();
        while (true)
        {
            Console.Write("> ");
            var userInput = await User.ReadInputWithFeaturesAsync(commandManager);
            if (string.IsNullOrWhiteSpace(userInput)) continue;
            memory.AddUserMessage(userInput);
            string response = await Engine.PostChatAsync(memory);
            Console.WriteLine(response);
            memory.AddAssistantMessage(response);
        }
    }
}