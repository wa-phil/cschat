using System;
using TinyJson;
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
    public static Config config = Config.Load(ConfigFilePath);
    public static Memory memory = new Memory(config.SystemPrompt); 
    public static CommandManager commandManager = CommandManager.CreateDefaultCommands();
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

    public static IChatProvider? Provider = null; // Allow nullable field
    private static ServiceProvider? serviceProvider = null;

    static Program()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(config); // Register the config instance
        // Register all IChatProvider implementations
        foreach (var providerType in Providers.Values)
        {
            serviceCollection.AddTransient(providerType);
        }

        serviceProvider = serviceCollection.BuildServiceProvider(); // Build the service provider
    }

    public static void SetProvider(string providerName) => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.Provider, providerName);
        serviceProvider.ThrowIfNull("Service provider is not initialized.");
        if (Providers.TryGetValue(providerName, out var type))
        {
            Provider = (IChatProvider?)serviceProvider.GetService(type);
        }
        else if (Providers.Count > 0)
        {
            var first = Providers.First();
            Provider = (IChatProvider?)serviceProvider.GetService(first.Value);
            ctx.Append(Log.Data.Message, $"{providerName} not found, using default provider: {first.Key}");
        }
        else
        {
            Provider = null;
            ctx.Failed($"No providers available. Please check your configuration or add a provider.", Error.ProviderNotConfigured);
            return;
        }
        config.Provider = providerName;
        ctx.Succeeded();
    });

    public static async Task<string?> SelectModelAsync() => await Log.MethodAsync(async ctx =>
    {
        if (Provider == null)
        {
            Console.WriteLine("Provider is not set.");
            ctx.Failed("Provider is not set.", Error.ProviderNotConfigured);
            return null;
        }

        var models = await Provider.GetAvailableModelsAsync();
        if (models == null || models.Count == 0)
        {
            Console.WriteLine("No models available.", Error.ModelNotFound);
            ctx.Failed("No models available.", Error.ModelNotFound);
            return null;
        }
        Console.WriteLine("Available models:");
        var selected = User.RenderMenu(models, models.IndexOf(config.Model));
        ctx.Append(Log.Data.Model, selected ?? "<nothing>");
        ctx.Succeeded();
        return selected;
    });

    static async Task Main(string[] args)
    {
        Log.Initialize();
        Console.WriteLine($"C#Chat v{BuildInfo.GitVersion} ({BuildInfo.GitCommitHash})");
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
            memory.AddUserMessage(userInput);
            if (Provider != null) // Add null check for Provider
            {
                string response = await Provider.PostChatAsync(memory);
                Console.WriteLine(response);
                memory.AddAssistantMessage(response);
            }
            else
            {
                Console.WriteLine("Provider is not set.");
            }
        }
    }
}