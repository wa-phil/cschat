using System;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class PlanStep
{
    public int    RemainingSteps { get; set; } = 0;
    public string ToolName { get; set; } = string.Empty;
    public string ToolInput { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public class PlanGoal
{
    public string Result { get; set; } = string.Empty; // "NoAction" or "ActionRequired"
    public string Goal { get; set; } = string.Empty; // The goal statement if action is required
}

public class Planner
{
    public MemoryContextManager ContextManager = new MemoryContextManager();
    List<string> results = new List<string>();

    public async Task<string> PostChatAsync(Memory memory) => await Log.MethodAsync(async ctx =>
    {
        var input = memory.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        var noAction        = "{  \"Result\": \"NoAction\",       \"Goal\": \"\"        }";
        var actionRequired  = "{  \"Result\": \"ActionRequired\", \"Goal\": \"<goal>\"  }";

        // first determine if the user's statement requires agency on the part of the planner, or if a meaningful and useful response 
        // can be generated without further action above and beyond replying to the user with the context and knowledge already available.
        var working = memory.Clone(); // do not disturb the original system message or memory, work off a temporary copy instead.
        working.SetSystemMessage($"""
The user just stated: {input}
Your task is to determine if the user's statement could benefit from tool usage to provide a more accurate or personalized response.

- If the user's query depends on runtime data (e.g., filesystem contents, current date/time, etc.), assume action is required.
- Otherwise, if a meaningful and useful static response can be generated from existing knowledge, respond with:

{noAction}

If action is required, respond with:

{actionRequired}

Where <goal> is a statement of what the user is trying to achieve, e.g., "Summarize the repo contents" or "Help me plan a trip to Paris".
At this point, you don't need to know how to achieve the goal, you just need to clearly state the goal as you understand it, so that you can plan the steps to achieve it later.
""");
        var response = await Engine.Provider!.PostChatAsync(working, 0.1f);
        ctx.Append(Log.Data.Response, response);
        var planGoal = response.FromJson<PlanGoal>();
        ctx.Append(Log.Data.Goal, planGoal != null ? planGoal.ToJson() : "<null>");

        int stepsTaken = 0, maxAllowedSteps = Program.config.MaxSteps;
        if (null != planGoal && planGoal.Result.Equals("ActionRequired", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(planGoal.Goal))
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"working on: {planGoal.Goal}");
            Console.ResetColor();

            ctx.Append(Log.Data.Message, "Taking steps to achieve the goal.");
            // Conversation implies action on the part of the planner.
            results = new List<string>();
            await foreach (var step in Steps(planGoal.Goal, working))
            {
                stepsTaken++;
                ctx.Append(Log.Data.Count, stepsTaken);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"Step {stepsTaken}: {step.Summary}...");
                Console.ResetColor();

                var result = await ToolRegistry.InvokeInternalAsync(step.ToolName, step.ToolInput, working, planGoal.Goal);                
                var status = result.Succeeded ? "✅" : "❌";

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"{status}\n--- context start --- \n{result.Response}\n--- context end ---");
                Console.ResetColor();

                // update working and memory with the step summary
                var stepSummary = $"--- step {stepsTaken}: {step.Summary} ---\n{result.Response}\n--- end step {stepsTaken} ---";
                results.Add(stepSummary);
                working.AddAssistantMessage(stepSummary);
                memory.AddAssistantMessage($"Step {stepsTaken}: {step.Summary} {status}"); 
            }

            // Final summarization with feedback loop
            working.SetSystemMessage($"""
Below is a conversation leading up to and including the steps taken, and their results, to achieve that goal.
The implied goal before action was taken was: {planGoal.Goal}

The user stated: {input}.

Use the following context to inform your response to the user's statement.
""");

            working.AddUserMessage($"Use a summary of the results of the steps taken to {planGoal.Goal} to inform your response to the original statement: {input}");
        }

        // Finally determine the response to the user, using working memory if actions were taken, or memory if no actions were taken.
        var context = stepsTaken > 0 ? working : memory;
        var final = await Engine.Provider!.PostChatAsync(context, Program.config.Temperature);
        ctx.Succeeded();
        return final;
    });

    public async IAsyncEnumerable<PlanStep> Steps(string goal, Memory memory)
    {
        var remainingSteps = 3; // Set a limit on the number of steps to prevent infinite loops
        while (0 < remainingSteps)
        {
            --remainingSteps;
            // Get the next step based on the goal and current context
            var step = await GetNextStep(goal, memory);
            if (step != null && !string.IsNullOrEmpty(step.ToolName) && !step.ToolName.StartsWith("No further action required", StringComparison.OrdinalIgnoreCase))
            {
                remainingSteps += step.RemainingSteps;
                yield return step;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Done.");
                Console.ResetColor();
                yield break;
            }
        }
    }

    private async Task<PlanStep?> GetNextStep(string goal, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Goal, goal);
        var lastMessage = memory.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? goal;
        await ContextManager.InvokeAsync(lastMessage, memory);

        var noActionRequired    = "{ \"RemainingSteps\": 0, \"ToolName\": \"\",            \"ToolInput\": \"\",             \"Summary\": \"No further action required.\" }";
        var takeFollowingAction = "{ \"RemainingSteps\": 1, \"ToolName\": \"<tool_name>\", \"ToolInput\": \"<tool_input>\", \"Summary\": \"<summary>\" }";

        var context = string.Join("\n", results.TakeLast(5));
        var tools = ToolRegistry.GetToolDescriptions();

        var prompt = $"""
You are a stepwise planner. The user has stated: {goal}

{(results.Count > 0 ? 
    $"You have the following knowledge of progress against the goal thus far:\n{context}" : 
    "You haven't taken a single step yet."
)}

Determine if any action is needed to help reach the user's goal. If so, plan the next step.

⚠️ **Important:** You must avoid repeating steps that have already been taken. Do not suggest using the same tool with the same input more than once.
- If a tool previously failed, consider what new information or preconditions might make it succeed.
- If a tool succeeded, assume its result is available in context and do not re-run it unless the input has changed significantly.

Your response should be:
- A JSON object indicating the next step to take, **or**
- An object indicating no further action is needed.

Respond with **only** one of the following JSON options:

If no further action is needed:
{noActionRequired}

If action is needed:
{takeFollowingAction}

Where:
- `<tool_name>` is the exact name of the tool to invoke
- `<tool_input>` is the argument
- `<summary>` is a brief description of the step's purpose

You may use the following tools:
{string.Join("\n", tools)}

Any relevant context or results from prior steps are available to you below.
""";

        var planMemory = memory.Clone();
        planMemory.SetSystemMessage(prompt);

        var response = await Engine.Provider!.PostChatAsync(planMemory, 0.0f);
        ctx.Append(Log.Data.Response, response ?? "<null>" );
        if (string.IsNullOrWhiteSpace(response))
        {
            ctx.Failed("Received empty response from planner.", Error.EmptyResponse);
            return null;
        }

        try
        {
            var step = response.FromJson<PlanStep>();
            ctx.Append(Log.Data.Step, step != null ? step.ToJson() : "<null>");
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
    public static Planner Planner = new Planner();

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
        return await Planner.PostChatAsync(history);
    }
}