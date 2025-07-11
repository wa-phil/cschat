using System;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class PlanStep
{
    public int    EstimatedRemainingSteps { get; set; } = 0;
    public string ToolName { get; set; } = string.Empty;
    public string ToolInput { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public class Planner
{
    public MemoryContextManager ContextManager = new MemoryContextManager();
    List<string> results = new List<string>();

    public string Description => "Dynamically plans and executes multi-step tasks using available tools.";
    public string Usage => "Input: A natural language goal, e.g., 'Summarize the repo contents'.";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        memory.AddUserMessage(input);
        var working = memory.Clone();        
        int stepCounter = 0;
        foreach (var step in Steps(input, working))
        {
            stepCounter++;
            Console.Write($"Step {stepCounter}: {step.Summary}... ");

            var result = await ToolRegistry.InvokeInternalAsync(step.ToolName, step.ToolInput, working, input);
            var status = result.Succeeded ? "✅" : "❌";
            Console.WriteLine($" - {status}\n--- context start --- \n{result.Response}\n--- context end ---");

            // update working and memory with the step summary
            var stepSummary = $"--- step {stepCounter}: {step.Summary} ---\n{result.Response}\n--- end step {stepCounter} ---";
            results.Add(stepSummary);
            working.AddAssistantMessage(stepSummary);

            memory.AddAssistantMessage($"Step {stepCounter}: {step.Summary} {status}"); 
        }

        // Final summarization with feedback loop
        working.SetSystemMessage($"""
The user asked {input}.
Below is a log of the steps taken to achieve that goal, and their results.
Summarize the results of the steps below for the user. 
""");

        var context = stepCounter > 0 ? working : memory;
        var final = await Engine.Provider!.PostChatAsync(context, Program.config.Temperature);
        ctx.Succeeded();
        return ToolResult.Success(final, memory);
    });

    public IEnumerable<Task<PlanStep>> Steps(string goal, Memory memory)
    {
        var remainingSteps = 3; // Set a limit on the number of steps to prevent infinite loops
        while (0 < remainingSteps)
        {
            --remainingSteps;
            // Get the next step based on the goal and current context
            var step = await GetNextStep(goal, memory);
            if (step != null && string.IsNullOrEmpty(step.ToolName) && !step.ToolName.StartsWith("No further action required", StringComparison.OrdinalIgnoreCase))
            {
                remainingSteps = step.EstimatedRemainingSteps;
                yield return step;
            }
            else
            {
                Console.WriteLine("No further actionable steps found.");
                yield break;
            }
        }
    }

    private async Task<PlanStep?> GetNextStep(string goal, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        var lastMessage = memory.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? goal;
        await ContextManager.InvokeAsync(lastMessage, memory);

        var noActionRequired = new PlanStep
        {
            EstimatedRemainingSteps = 0,
            ToolName = string.Empty,
            ToolInput = string.Empty,
            Summary = "No further action required."
        }.ToString();

        var takeFollowingAction = new PlanStep
        {
            EstimatedRemainingSteps = 1,
            ToolName = "ToolName",
            ToolInput = "ToolInput",
            Summary = "..."
        }.ToString();

        var context = string.Join("\n", results.TakeLast(5));
        var tools = ToolRegistry.GetToolDescriptions();

        var prompt = $"""
You are a stepwise planner. Respond only with a single next step."
The user has a stated goal: "{goal}"

{(results.Count > 0 ? 
    $"You have the following knowledge of progress against the goal thusfar:\n{context}" : 
    "You haven't taken a single step yet."
)}

Determine the next best step to help reach the user's goal. 
If a step previously failed, consider what other information or actions might be required to succeed before trying it again. 
Reflect on which steps have worked well so far and which have not, and adapt your plan accordingly.

If no further action is needed, respond with:
{noActionRequired}

Otherwise, respond with:
{takeFollowingAction}

Where: 
* "EstimatedRemainingSteps" is the number of steps remaining to complete the goal
* "ToolName" is the name of the tool to use
* "ToolInput" is the input to provide to the tool
* "Summary" is a brief description of what the step does.

Only use tools from this list exactly as named:
{string.Join("\n", tools)}

Any and all other context you have about the user and their goal is available to you below, including any relevant files or information in the knowledge base.
""";

        var planMemory = memory.Clone();
        planMemory.SetSystemMessage(prompt);

        var response = await Engine.Provider!.PostChatAsync(planMemory, 0.0f);
        if (string.IsNullOrWhiteSpace(response))
        {
            ctx.Failed("Received empty response from planner.", Error.EmptyResponse);
            return null;
        }

        try
        {
            var step = response.FromJson<PlanStep>();
            ctx.Append(Log.Data.Response, response);
            ctx.Succeeded();
            return step;
        }
        catch (Exception ex)
        {
            ctx.Append(Log.Data.Response, response);
            ctx.Failed("Failed to parse next plan step.", ex);
            return null;
        }
    });
}

public static class Engine
{
    public static IVectorStore VectorStore = new InMemoryVectorStore();
    public static IChatProvider? Provider = null;
    public static ITextChunker? TextChunker = null;

    public static List<string> supportedFileTypes = new List<string>
    {
        ".bash", ".bat",
        ".c", ".cpp", ".cs", ".csproj", ".csv",
        ".h", ".html",
        ".ignore",
        ".js",
        ".log",
        ".md",
        ".py",
        ".sh", ".sln",
        ".ts", ".txt",
        ".xml",
        ".yml"
    };

    public static async Task AddDirectoryToVectorStore(string path) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Path, path);
        if (!Directory.Exists(path))
        {
            ctx.Failed($"Directory '{path}' does not exist.", Error.DirectoryNotFound);
            return;
        }

        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedFileTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        await Task.WhenAll(files.Select(f => AddFileToVectorStore(f)));
        Console.WriteLine($"Added {files.Count} files to vector store from directory '{path}'.");

        // Aggregate the list of relative file names into a new line delimited string and add it to the vector store.
        var knownFiles = files.Aggregate(new StringBuilder(), (sb, f) => sb.AppendLine(Path.GetRelativePath(Directory.GetCurrentDirectory(), f))).ToString().TrimEnd();
        var embeddings = new List<(string Reference, string Chunk, float[] Embedding)>();
        var chunks = TextChunker!.ChunkText("known files", knownFiles);

        await Task.WhenAll(chunks.Select(async chunk =>
            embeddings.Add((
                Reference: chunk.Reference,
                Chunk: chunk.Content,
                Embedding: await (Engine.Provider as IEmbeddingProvider)!.GetEmbeddingAsync(chunk.Content)
            ))
        ));
        Engine.VectorStore.Add(embeddings);
        Console.WriteLine($"Added directory index to vector store in {chunks.Count} chunks.");

        ctx.Append(Log.Data.Count, files.Count);
        ctx.Succeeded();
    });

    public static async Task AddContentToVectorStore(string content, string reference = "content") => await Log.MethodAsync(async ctx =>
    {
        TextChunker.ThrowIfNull("Text chunker is not set. Please configure a text chunker before adding files to the vector store.");
        IEmbeddingProvider? embeddingProvider = Engine.Provider as IEmbeddingProvider;
        embeddingProvider.ThrowIfNull("Current configured provider does not support embeddings.");

        ctx.Append(Log.Data.Reference, reference);
        var embeddings = new List<(string Reference, string Chunk, float[] Embedding)>();
        var chunks = TextChunker!.ChunkText(reference, content);
        ctx.Append(Log.Data.Count, chunks.Count);

        await Task.WhenAll(chunks.Select(async chunk =>
            embeddings.Add((
                Reference: chunk.Reference,
                Chunk: chunk.Content,
                Embedding: await embeddingProvider!.GetEmbeddingAsync(chunk.Content)
            ))
        ));

        Engine.VectorStore.Add(embeddings);
        ctx.Succeeded(embeddings.Count > 0);
    });

    public static async Task AddFileToVectorStore(string path) => await Log.MethodAsync(async ctx =>
    {
        // start a timer to measure the time taken to add the file
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await AddContentToVectorStore(File.ReadAllText(path), path);
        ctx.Append(Log.Data.FilePath, path);
        stopwatch.Stop();
        var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
        Console.WriteLine($"{elapsedTime}ms required to read file '{path}' contents.");
        ctx.Succeeded();
    });
        
    public static void SetTextChunker(string chunkerName) => Log.Method(ctx =>
    {
        Program.config.RagSettings.ChunkingStrategy = chunkerName;
        ctx.Append(Log.Data.Name, chunkerName);
        Program.serviceProvider.ThrowIfNull("Service provider is not initialized.");
        if (Program.Chunkers.TryGetValue(chunkerName, out var type))
        {
            TextChunker = (ITextChunker?)Program.serviceProvider?.GetService(type);
            ctx.Append(Log.Data.Message, $"Using chunker: {type.Name} as implementation for {chunkerName}");
        }
        else if (Program.Chunkers.Count > 0)
        {
            var first = Program.Chunkers.First();
            TextChunker = (ITextChunker?)Program.serviceProvider?.GetService(first.Value);
            ctx.Append(Log.Data.Message, $"{chunkerName} not found, using default chunker: {first.Key}");
        }
        else
        {
            TextChunker = null!;
            ctx.Failed($"No chunkers available. Please check your configuration or add a chunker.", Error.ChunkerNotConfigured);
            return;
        }
        ctx.Succeeded(TextChunker != null);
    });

    public static void SetProvider(string providerName) => Log.Method(ctx =>
    {
        Program.config.Provider = providerName;
        ctx.Append(Log.Data.Provider, providerName);
        Program.serviceProvider.ThrowIfNull("Service provider is not initialized.");
        if (Program.Providers.TryGetValue(providerName, out var type))
        {
            Provider = (IChatProvider?)Program.serviceProvider?.GetService(type);
        }
        else if (Program.Providers.Count > 0)
        {
            var first = Program.Providers.First();
            Provider = (IChatProvider?)Program.serviceProvider?.GetService(first.Value);
            ctx.Append(Log.Data.Message, $"{providerName} not found, using default provider: {first.Key}");
        }
        else
        {
            Provider = null;
            ctx.Failed($"No providers available. Please check your configuration or add a provider.", Error.ProviderNotConfigured);
            return;
        }
        ctx.Append(Log.Data.ProviderSet, Provider != null);
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

        var selected = User.RenderMenu("Available models:", models, models.IndexOf(Program.config.Model));
        ctx.Append(Log.Data.Model, selected ?? "<nothing>");
        ctx.Succeeded();
        return selected;
    });

    public static async Task<string> PostChatAsync(Memory history)
    {
        Provider.ThrowIfNull("Provider is not set.");
        
        var lastUserInput = history.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        var planner = new Planner();

        // === Check if tool is applicable ===
        var suggestion = await ToolRegistry.GetToolSuggestionAsync(lastUserInput);
        if (null != suggestion)
        {
            var toolResponse = await ToolRegistry.InvokeToolAsync(suggestion.Tool, suggestion.Input ?? string.Empty, history, lastUserInput);
            if (!string.IsNullOrEmpty(toolResponse))
            {
                return toolResponse;
            }
        }

        return await Provider!.PostChatAsync(history, Program.config.Temperature);
    }
}