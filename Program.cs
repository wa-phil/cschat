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
    public static ServiceProvider serviceProvider = null!;
    public static SubsystemManager SubsystemManager = null!;
    public static UserManagedData userManagedData = null!;
    public static IUi ui = new Terminal();

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
            if (typeof(ISubsystem).IsAssignableFrom(item))
            {
                // Some subsystems (e.g. McpManager) use private constructors and expose a public static Instance property.
                // Try to register the existing Instance if present; otherwise register a factory that can create non-public ctors.
                var instanceProp = item.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instanceProp != null && item.IsAssignableFrom(instanceProp.PropertyType))
                {
                    // Register the singleton using the static Instance property
                    serviceCollection.AddSingleton(item, sp => instanceProp.GetValue(null)!);
                }
                else
                {
                    // Fallback: register a singleton factory that can create non-public constructors
                    serviceCollection.AddSingleton(item, sp => Activator.CreateInstance(item, nonPublic: true)!);
                }
            }
            else
            {
                serviceCollection.AddTransient(item);
            }
        }

        return result;
    }

    public static void Startup()
    {
        config = Config.Load(ConfigFilePath);
        Log.Initialize();

        SubsystemManager = new SubsystemManager();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(config); // Register the config instance
        var assembly = Assembly.GetExecutingAssembly();
        var types = assembly.GetTypes();

        // Register all IChatProvider implementations
        Providers = DictionaryOfTypesToNamesForInterface<IChatProvider>(serviceCollection, types);
        Chunkers = DictionaryOfTypesToNamesForInterface<ITextChunker>(serviceCollection, types);
        Tools = DictionaryOfTypesToNamesForInterface<ITool>(serviceCollection, types);
        SubsystemManager.Register(DictionaryOfTypesToNamesForInterface<ISubsystem>(serviceCollection, types));
        userManagedData = new UserManagedData();
        serviceProvider = serviceCollection.BuildServiceProvider(); // Build the service provider
        Environment.CurrentDirectory = Program.config.DefaultDirectory;
    }

    public static async Task InitProgramAsync()
    {
        Context = new Context(config.SystemPrompt);
        ToolRegistry.Initialize();

        // Initial legacy list (will be replaced by RagFileType user-managed entries after migration)
        Engine.SupportedFileTypes = config.RagSettings.SupportedFileTypes;
        Engine.SetProvider(config.Provider);
        Engine.SetTextChunker(config.RagSettings.ChunkingStrategy);
        Engine.VectorStore.Clear();

        // Create command manager before connecting subsystems so subsystems can register/unregister commands (ADO needs this)
        commandManager = CommandManager.CreateDefaultCommands();

        // Connect user-managed data first so annotated types are discovered
        // before subsystems query or load items from it.
        userManagedData.Connect();

        // Migration: if no RagFileType entries yet, populate from legacy config
        try
        {
            var existing = userManagedData.GetItems<RagFileType>();
            if (existing.Count == 0 && config.RagSettings.SupportedFileTypes.Count > 0)
            {
                foreach (var ext in config.RagSettings.SupportedFileTypes.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var rft = new RagFileType { Extension = ext, Enabled = true };
                    if (config.RagSettings.FileFilters.TryGetValue(ext, out var rules))
                    {
                        rft.Include = rules.Include.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                        rft.Exclude = rules.Exclude.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    }
                    userManagedData.AddItem(rft);
                }
                // Persist migration by clearing legacy filters (optional: keep for backward compatibility)
                Config.Save(config, ConfigFilePath);
            }
        }
        catch { /* ignore migration issues */ }

        // Ensure Engine has up-to-date list
        Engine.RefreshSupportedFileTypesFromUserManaged();
        SubsystemManager.Connect();

        // initialize chat manager to monitor thread deletions, load last active thread or create a new one
        Directory.CreateDirectory(Program.config.ChatThreadSettings.RootDirectory);
        ChatManager.Initialize(userManagedData);
        var activeName = config.ChatThreadSettings.ActiveThreadName;
        var threads = userManagedData.GetItems<ChatThread>().ToList();
        ChatThread? active = null;

        if (!string.IsNullOrWhiteSpace(activeName))
        {
            active = threads.FirstOrDefault(t => t.Name.Equals(activeName, StringComparison.OrdinalIgnoreCase));
        }

        if (active == null)
        {
            active = new ChatThread { Name = Program.config.ChatThreadSettings.DefaultNewThreadName };
            userManagedData.AddItem(active);
        }

        ChatManager.LoadThread(active);
        Program.config.ChatThreadSettings.ActiveThreadName = active.Name;
        Config.Save(Program.config, Program.ConfigFilePath);

        // Add all the tools to the context
        var toolNames = $"You can use the following tools to help the user:\n{ToolRegistry.GetRegisteredTools()
            .Select(tool => $"{tool.Name}")
            .Aggregate(new StringBuilder(), (sb, txt) => sb.AppendLine(txt))
            .ToString()}";
        if (!string.IsNullOrWhiteSpace(toolNames))
        {
            await ContextManager.AddContent(toolNames, "tool_names");
        }
        else
        {
            Program.ui.WriteLine("No tools registered. Please check your configuration.");
        }

        foreach (var tool in ToolRegistry.GetRegisteredTools())
        {
            var toolDetails = $"Tool: {tool.Name}\nDescription: {tool.Description}\nUsage: {tool.Usage}";
            await ContextManager.AddContent(toolDetails, tool.Name);
        }
    }

    [STAThread]
    static async Task Main(string[] args)
    {
        Program.ui.WriteLine($"Console# Chat v{BuildInfo.GitVersion} ({BuildInfo.GitCommitHash})");
        Startup();

        bool showHelp = false;
        var options = new OptionSet {
            { "h|host=", "Server host (default: http://localhost:11434)", v => { if (v != null) config.Host = v; } },
            { "m|model=", "Model name", v => { if (v != null) config.Model = v; } },
            { "s|system=", "System prompt", v => { if (v != null) config.SystemPrompt = v; } },
            { "p|provider=", "Provider name (default: ollama)", v => { if (v != null) config.Provider = v; } },
            { "e|embedding_model=", "Embedding model for RAG (default: nomic-embed-text)", v => { if (v != null) config.RagSettings.EmbeddingModel = v; } },
            { "u|ui=", "UI mode: terminal|gui", v => { if (!string.IsNullOrWhiteSpace(v)) config.UiMode = Enum.TryParse<UiMode>(v, true, out var mode) ? mode : UiMode.Terminal; } },
            { "?|help", "Show help", v => showHelp = v != null }
        };
        options.Parse(args);
        if (showHelp)
        {
            options.WriteOptionDescriptions(Console.Out);
            return;
        }

        // choose UI
        switch (config.UiMode)
        {
            case UiMode.Terminal:
                Console.WriteLine("Using terminal UI mode.");
                ui = new Terminal();
                break;
            case UiMode.Gui:
                Console.WriteLine("Using GUI UI mode.");
                ui = new PhotinoUi();
                break;
            default:
                ui = new Terminal();
                break;
        }

        try
        {
            // ----- Run the application loop via the UI -----
            await ui.RunAsync(async () =>
            {
                await InitProgramAsync();

                if (string.IsNullOrWhiteSpace(config.Model))
                {
                    var selected = await Engine.SelectModelAsync();
                    if (selected == null) return;
                    config.Model = selected;
                }

                Config.Save(config, ConfigFilePath);

                ui.WriteLine($"Connecting to {config.Provider} at {config.Host} using model '{config.Model}'");
                ui.WriteLine("Type your message and press Enter. Press the ESC key for the menu.");
                ui.WriteLine();

                while (true)
                {
                    if (UiMode.Terminal == config.UiMode) ui.Write("> ");
                    var userInput = await ui.ReadInputWithFeaturesAsync(commandManager);

                    // IMPORTANT: when the Photino window closes, ReadInput... returns null -> exit loop
                    if (userInput is null) break;

                    if (string.IsNullOrWhiteSpace(userInput)) continue;

                    // Add and render user message with proper formatting
                    Context.AddUserMessage(userInput);
                    var userMessage = Context.Messages().Last(); // Get the message we just added
                    ui.RenderChatMessage(userMessage);

                    var (response, updatedContext) = await Engine.PostChatAsync(Context);
                    var assistantMessage = new ChatMessage { Role = Roles.Assistant, Content = response, CreatedAt = DateTime.Now };
                    ui.RenderChatMessage(assistantMessage);
                    Context = updatedContext;
                    Context.AddAssistantMessage(response);
                }
            });
        }
        catch (Exception ex)
        {
            ui.WriteLine($"Exception: {ex.Message}");
            ui.WriteLine("Log Entries:");
            var entries = Log.GetOutput().ToList();
            ui.WriteLine($"Log Entries [{entries.Count}]:");
            entries.ToList().ForEach(entry => ui.WriteLine(entry));
            ui.WriteLine("Chat History:");
            ui.RenderChatHistory(Context.Messages());
            throw; // unhandled exceptions result in a stack trace in the Program.ui.
        }
        finally
        {
            // Set up graceful shutdown
            await McpManager.Instance.ShutdownAllAsync();
        }
    }
}