using System;
using System.IO;
using System.Linq;
using Mono.Options;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Text;


static class Program
{
    public static string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    public static Config config = null!;
    public static Context Context = null!;
    public static Dictionary<string, Type> Providers = null!; // Dictionary to hold provider types by name
    public static Dictionary<string, Type> Chunkers = null!; // Dictionary to hold chunker types by name
    public static Dictionary<string, Type> Tools = null!; // Dictionary to hold tool types by name
    public static CommandManager commandManager = null!;
    public static ServiceProvider? serviceProvider = null!;

    static Dictionary<string, Type> DictionaryOfTypesToNamesForInterface<T>(ServiceCollection serviceCollection, IEnumerable<Type> types)
        where T : class
    {
        var result = types
            .Where(t => typeof(T).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => new
            {
                Type = t,
                Attr = t.GetCustomAttribute<IsConfigurable>()
            })
            .Where(x => x.Attr != null)
            .ToDictionary(x => x.Attr?.Name ?? string.Empty, x => x.Type, StringComparer.OrdinalIgnoreCase);

        foreach (var item in result.Values)
        {
            serviceCollection.AddTransient(item);
        }
        
        return result;
    }

    public static void Startup()
    {
        config = Config.Load(ConfigFilePath);
        Log.Initialize();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(config); // Register the config instance
        var assembly = Assembly.GetExecutingAssembly();
        var types = assembly.GetTypes();

        // Register all IChatProvider implementations
        Providers = DictionaryOfTypesToNamesForInterface<IChatProvider>(serviceCollection, types);
        Chunkers = DictionaryOfTypesToNamesForInterface<ITextChunker>(serviceCollection, types);
        Tools = DictionaryOfTypesToNamesForInterface<ITool>(serviceCollection, types);

        serviceProvider = serviceCollection.BuildServiceProvider(); // Build the service provider

    }

    public static async Task InitProgramAsync()
    {
        Context = new Context(config.SystemPrompt);
        ToolRegistry.Initialize();

        // Initialize MCP manager and load servers
        await McpManager.Instance.LoadAllServersAsync();

        // Create command manager after all tools are registered
        commandManager = CommandManager.CreateDefaultCommands();

        Engine.SetProvider(config.Provider);
        Engine.SetTextChunker(config.RagSettings.ChunkingStrategy);

        // Add all the tools to the context        
        var toolDescriptions = $"Available Tools:\n{ToolRegistry.GetRegisteredTools()
            .Select(tool => $"{tool.Name}: {tool.Description}")
            .Aggregate(new StringBuilder(), (sb, txt) => sb.AppendLine(txt))
            .ToString()}";
        await ContextManager.AddContent(toolDescriptions, "tool_descriptions");

        // Add supported file types to the context
        var supportedFileTypes = $"Supported File Types:\n{Engine.supportedFileTypes
            .Select(ext => ext.ToUpperInvariant())
            .Aggregate(new StringBuilder(), (sb, txt) => sb.AppendLine(txt))
            .ToString()}";
        await ContextManager.AddContent(supportedFileTypes, "supported_file_types");
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine($"Console# Chat v{BuildInfo.GitVersion} ({BuildInfo.GitCommitHash})");
        Startup();

        bool showHelp = false;
        var options = new OptionSet {
            { "h|host=", "Server host (default: http://localhost:11434)", v => { if (v != null) config.Host = v; } },
            { "m|model=", "Model name", v => { if (v != null) config.Model = v; } },
            { "s|system=", "System prompt", v => { if (v != null) config.SystemPrompt = v; } },
            { "p|provider=", "Provider name (default: ollama)", v => { if (v != null) config.Provider = v; } },
            { "e|embedding_model=", "Embedding model for RAG (default: nomic-embed-text)", v => { if (v != null) config.RagSettings.EmbeddingModel = v; } },
            { "?|help", "Show help", v => showHelp = v != null }
        };
        options.Parse(args);
        if (showHelp)
        {
            options.WriteOptionDescriptions(Console.Out);
            return;
        }

        await InitProgramAsync();

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
        try
        {
            while (true)
            {
                Console.Write("> ");
                var userInput = await User.ReadInputWithFeaturesAsync(commandManager);
                if (string.IsNullOrWhiteSpace(userInput)) continue;

                // Add and render user message with proper formatting
                Context.AddUserMessage(userInput);
                var userMessage = Context.Messages.Last(); // Get the message we just added
                User.RenderChatMessage(userMessage);

                var (response, updatedContext) = await Engine.PostChatAsync(Context);
                var assistantMessage = new ChatMessage { Role = Roles.Assistant, Content = response, CreatedAt = DateTime.Now };
                User.RenderChatMessage(assistantMessage);
                Context = updatedContext;
                Context.AddAssistantMessage(response);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
            Console.WriteLine("Log Entries:");
            var entries = Log.GetOutput().ToList();
            Console.WriteLine($"Log Entries [{entries.Count}]:");
            entries.ToList().ForEach(entry => Console.WriteLine(entry));
            Console.WriteLine("Chat History:");
            User.RenderChatHistory(Context.Messages);
            throw; // unhandled exceptions result in a stack trace in the console.
        }
        finally
        {
            // Set up graceful shutdown
            await McpManager.Instance.ShutdownAllAsync();
        }
    }
}