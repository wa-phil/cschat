using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public record PlanStep(bool Done, string ToolName, object ToolInput, string Reason)
{
    public static PlanStep Complete(string reason) => new PlanStep(true, string.Empty, new NoInput(), reason);
    public static PlanStep Run(string toolName, object toolInput, string reason) => new PlanStep(false, toolName, toolInput, reason);
}

[ExampleText("""
Respond with **only** one of the following JSON options:

If no further action is needed:

{ \"ToolName\": \"\", \"Reasoning\": \"No further action required.\" }

If a tool should be used:

{ \"ToolName\": \"<tool_name>\", \"Reasoning\": \"<reasoning>\" }

Where:
- `<tool_name>` is the exact name of the tool to use
- `<reasoning>` is a brief explanation of why this tool was selected

**Important:** 
- Your output will be parsed as JSON. Do NOT include markdown, commentary, or explanations.
- Respond with ONLY the JSON object.
- Do NOT include any additional text or explanations.
""")]
public class ToolSelection
{
    public string ToolName  { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

[ExampleText("""
If a meaningful and useful response can be generated from context obtained from previous steps, respond with:

{ \"GoalAchieved\": true }

If action is required to achieve the goal, respond with:

{ \"GoalAchieved\": false }

**Important:**
- Your output will be parsed as JSON. Do NOT include markdown, commentary, or explanations.
- Respond with ONLY the JSON object.
- Do NOT include any additional text or explanations.
""")]
public class PlanProgress
{
    public bool GoalAchieved { get; set; } = false; // Indicates if the goal was achieved
}

[ExampleText("""
If a meaningful and useful static response can be generated from existing knowledge, respond with:

{  \"TakeAction\": false, \"Goal\": \"<reason>\" }

If action is required, respond with:

{  \"TakeAction\": true,  \"Goal\": \"<goal>\"   }

Where:
  * <goal> is a statement of what the user is trying to achieve, e.g., "Summarize the repo contents" or "Help me plan a trip to Paris".
  * <reason> is a statement of why no action is required, e.g., "The user is asking for a summary of the repo contents, which can be generated from existing knowledge."

At this point, you don't need to know how to achieve the goal, just clearly state the goal as you understand it, so that you can plan the steps to achieve it later.
Only respond with the JSON object, do not include any additional text or explanation.
""")]
public class PlanObjective
{
    public bool TakeAction { get; set; } = false; 
    public string Goal { get; set; } = string.Empty; // The goal statement if action is required
}

public class Planner
{
    private bool done = false;
    private List<string> results = new List<string>();
    private HashSet<string> actionsTaken = new HashSet<string>();

    private async Task<PlanProgress> GetPlanProgress(Memory memory, string goal, string userInput) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var workingContext = memory.GetContext().Select(c => $"{c.Reference}: {c.Chunk}").ToList();
        var workingMemory = new Memory($"""
You are a plan progress evaluator.
The assistant has already replied to the user with one or more steps towards achieving the goal.
Your task is to determine if additional steps are needed to achieve the goal (e.g. is taking further action required) based on the following:
1. The goal: {goal}
2. The user's input: {userInput}
3. The current steps taken so far:
{string.Join("\n", results)}
{(workingContext.Count > 0 ? $"\n4. The current context:\n{string.Join("\n", workingContext)}" : "")}
""");
        // marshal memory context into working memory
        memory.GetContext().ForEach(c => workingMemory.AddContext(c.Reference, c.Chunk));
        workingMemory.AddUserMessage("Have we achieved the goal?");
        var response = await Engine.PostChatAndParseTypeAsync(workingMemory, typeof(PlanProgress));
        if (response == null || response is not PlanProgress progress)
        {
            ctx.Failed("Failed to parse plan progress from response.", Error.EmptyResponse);
            return new PlanProgress { GoalAchieved = false };
        }
        ctx.Append(Log.Data.Progress, response != null ? response.ToJson() : "<null>");
        ctx.Succeeded();
        return progress;
    });

    private async Task<object?> GetToolInput(string toolName, ITool tool, string goal) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Name, toolName);
        ctx.Append(Log.Data.TypeToParse, tool.InputType.Name);

        // If the tool doesn't need input, return a default instance
        if (tool.InputType == typeof(NoInput))
        {
            ctx.Succeeded();
            return new NoInput();
        }

        var memory = new Memory($"""
You are an input generator for the tool '{toolName}'.  
Your task is to generate appropriate input for this tool based on the goal and context.

The stated goal: {goal}
Tool Description: {tool.Description}
Tool Usage: {tool.Usage}
Input Type: {tool.InputType.Name}

{(results.Count > 0 ? $"Context from previous steps:\n{string.Join("\n", results.TakeLast(3))}" : "")}

Generate the appropriate input for this tool in JSON format.
The JSON should match the structure of the {tool.InputType.Name} class.
Respond with ONLY the JSON object that matches {tool.InputType.Name}.
""");

        memory.AddUserMessage("Create the inputs JSON for this tool based on the goal and context.");

        var typedInput = await Engine.PostChatAndParseTypeAsync(memory, tool.InputType);
        ctx.Append(Log.Data.ParsedInput, typedInput?.ToJson() ?? "<null>");        
        ctx.Succeeded();
        return typedInput;
    });

    private async Task<ToolSelection> GetToolSelection(string goal, Memory history) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var context = string.Join("\n", results.TakeLast(5));
        var tools = ToolRegistry.GetToolDescriptions();

        var memory = new Memory($"""
You are a tool selection agent.
Your task is to determine which tool to use based on the following:
1. The user's goal: {goal}
{(results.Count >0 ? 
    $"2. Recent steps taken: {string.Join("\n", results.TakeLast(5))}" : 
    "2. No steps taken yet."
)}
3. The available tools:
{string.Join("\n", tools)}

**Important:**
- Do not select tools that have already been used with similar inputs.
- Pick the best tool not already chosen to achieve the goal based on the context provided.
- Your output will be parsed as JSON. Do NOT include markdown, commentary, or explanations.
- Do NOT include any additional text or explanations.
""");
        // marshal memory context into working memory
        history.GetContext().ForEach(c => memory.AddContext(c.Reference, c.Chunk));
        memory.AddToolMessage($"Progress so far:\n{string.Join("\n", results)}");
        memory.AddUserMessage("What is the next tool to use?");
        var response = await Engine.PostChatAndParseTypeAsync(memory, typeof(ToolSelection));
        if (response == null || response is not ToolSelection toolSelection)
        {
            ctx.Failed("Failed to parse tool selection from response.", Error.EmptyResponse);
            return new ToolSelection { ToolName = "", Reasoning = "No further action required." };
        }
        ctx.Succeeded();
        return (ToolSelection)response;
    });

    private async Task<PlanObjective> GetObjective(Memory memory) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var input = memory.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        var working = new Memory($"""
You are a goal planner.
Your task is to decide if a tool should be used to answer the user's question, to provide a more accurate or personalized response.
If the user's query depends on realtime or runtime data (e.g., filesystem contents, current date/time, etc.), assume action is required, and that you will be able to complete it.
""");
        memory.GetContext().ForEach(c => working.AddContext(c.Reference, c.Chunk));
        working.AddUserMessage(input);
        var result = await Engine.PostChatAndParseTypeAsync(working, typeof(PlanObjective));
        if (result == null || result is not PlanObjective goal)
        {
            ctx.Failed("Failed to parse goal from response.", Error.EmptyResponse);
            return new PlanObjective { TakeAction = false, Goal = "No further action required" };
        }
        ctx.Succeeded();
        return (PlanObjective)result;
    });

    public async Task<(string result, Memory memory)> PostChatAsync(Memory memory) => await Log.MethodAsync(async ctx =>
    {
        // We're just getting started, reset results and actionsTaken for each session
        done = false;
        results = new List<string>(); 
        actionsTaken = new HashSet<string>();

        var objective = await GetObjective(memory);
        ctx.Append(Log.Data.Goal, objective != null ? objective.ToJson() : "<null>");

        // EARLY EXIT: If no action is needed, skip planning loop and just answer
        if (null == objective ||
            objective.TakeAction == false ||
            string.IsNullOrEmpty(objective.Goal.Trim() ?? string.Empty) ||
            objective.Goal.Equals("No further action required", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Construct a user-facing natural language reply using the original memory
            ctx.Append(Log.Data.Message, "No planning required, generating response directly.");
            memory.SetSystemMessage(Program.config.SystemPrompt);
            var finalResult = await Engine.Provider!.PostChatAsync(memory, Program.config.Temperature);
            ctx.Succeeded();
            return (finalResult, memory); // memory is unchanged
        }

        var input = memory.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        int stepsTaken = 0, maxAllowedSteps = Program.config.MaxSteps;
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"working on: {objective.Goal}");
        Console.ResetColor();

        ctx.Append(Log.Data.Message, "Taking steps to achieve the goal.");
        // Conversation implies action on the part of the planner.
        int duplicatesAllowed = 3;
        bool planningFailed = false;
        await foreach (var step in Steps(objective.Goal, memory, input, onPlanningFailure: reason => {
            planningFailed = true;
            ctx.Append(Log.Data.Reason, reason);
        }))
        {
            // Prevent infinite loops by limiting the number of steps taken (user configureable).
            if (stepsTaken++ > maxAllowedSteps)
            {
                ctx.Append(Log.Data.Message, $"Exceeded maximum allowed steps ({maxAllowedSteps}). Stopping planning.");
                break;
            }

            // Follow the DRY principle to avoid repeating steps...
            var key = $"{step.ToolName}:{step.ToolInput.ToJson() ?? ""}";
            if (actionsTaken.Contains(key))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"Skipping duplicate step: {step.ToolName ?? "<unknown>"} with already attempted input.");
                Console.ResetColor();

                if (0 == --duplicatesAllowed)
                {
                    ctx.Append(Log.Data.Message, $"Exceeded maximum allowed duplicates ({duplicatesAllowed}). Stopping planning.");
                    memory.SetSystemMessage(Program.config.SystemPrompt);
                    memory.AddUserMessage($"Planning failed for goal: {objective.Goal}. Please summarize the results of the steps taken so far.");
                    var finalResponse = await Engine.Provider!.PostChatAsync(memory, Program.config.Temperature);

                    ctx.Failed("Planning failed, summarizing results.", Error.PlanningFailed);
                    return (finalResponse, memory);
                }
                continue; // skip duplicate steps
            }
            actionsTaken.Add(key);

            ctx.Append(Log.Data.Count, stepsTaken);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Step {stepsTaken}: {step.ToolName}...");
            Console.ResetColor();

            var result = await ToolRegistry.InvokeInternalAsync(step!.ToolName!, step!.ToolInput!, memory, objective.Goal!);
            var status = result.Succeeded ? "✅" : "❌";
            var stepSummary = $"--- step {stepsTaken}: {step.ToolName}({step.ToolInput.ToJson()}) ---\n{result.Response}\n--- end step {stepsTaken} ---";

            if (result.Succeeded)
            {
                // update memory with the result of the tool invocation
                memory = result.Memory;
                memory.AddToolMessage(stepSummary);
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{status}\n{result.Response}");
            Console.ResetColor();

            // update working and memory with the step summary
            results.Add(stepSummary);

            // check progress towards the goal
            var progress = await GetPlanProgress(memory, objective.Goal, input);
            if (progress == null || progress is not PlanProgress)
            {
                throw new CsChatException($"Failed to get plan progress for goal '{objective.Goal}'.", Error.EmptyResponse);
            }
            
            done = progress.GoalAchieved;
        }

        // Handle the case where planning failed, and bail out early with a summary of the results taken so far.
        if (planningFailed)
        {
            memory.SetSystemMessage(Program.config.SystemPrompt);
            memory.AddUserMessage($"Planning failed for goal: {objective.Goal}. Please summarize the results of the steps taken so far.");
            var finalResponse = await Engine.Provider!.PostChatAsync(memory, Program.config.Temperature);
            ctx.Failed("Planning failed, summarizing results.", Error.PlanningFailed);
            return (finalResponse, memory);
        }

        // Final summarization with feedback loop
        var working = new Memory($"""
Below is a conversation leading up to and including the steps taken, and their results, to achieve that goal.
The implied goal before action was taken was: {objective.Goal}

The user stated: {input}.

Use the following context to inform your response to the user's statement.
""");
        // marshal memory context into working memory
        memory.GetContext().ForEach(c => working.AddContext(c.Reference, c.Chunk));
        results.ForEach(r => working.AddToolMessage(r));
        working.AddUserMessage($"Use the results of the steps taken to achieve the goal: '{objective.Goal}' to inform your response to the original statement: '{input}'");
        var final = await Engine.Provider!.PostChatAsync(working, Program.config.Temperature);

        ctx.Succeeded();
        return (final, memory);
    });

    private enum State { failed, success, noFurtherActionRequired };

    public async IAsyncEnumerable<PlanStep> Steps(string goal, Memory memory, string userInput, Action<string> onPlanningFailure = null!) 
    {
        while (!done)
        {
            // Get the next step based on the goal and current context
            string reason = string.Empty;            
            State state = State.success;
            PlanStep? step = null;
            try
            {
                step = await GetNextStep(goal, memory, userInput);
                if (step == null)
                {
                    state = State.failed;
                    reason = "ERROR: Planner failed to return a valid step; check logs for further details.";
                }
                else if (step.Done)
                {
                    state = State.noFurtherActionRequired;
                    reason = step.Reason;
                }
                else if (string.IsNullOrEmpty(step.ToolName))
                {
                    state = State.failed;
                    reason = "ERROR: Planner returned a step with an empty tool name; check logs for further details.";
                }
                else if (!ToolRegistry.IsToolRegistered(step.ToolName))
                {
                    state = State.failed;
                    reason = $"ERROR: Planner returned an invalid tool name: {step.ToolName}; check logs for further details.";
                }
                else if (step.ToolInput == null)
                {
                    state = State.failed;
                    reason = "ERROR: Planner returned a step with null tool input; check logs for further details.";
                }
            } 
            catch (Exception ex)
            {
                state = State.failed;
                reason = $"ERROR: Planner failed to return a valid step due to an exception: {ex.Message}";
            }

            if (State.failed == state)
            {
                onPlanningFailure?.Invoke(reason);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(reason);
                Console.ResetColor();
                yield break;
            }

            if (State.noFurtherActionRequired == state)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Done.");
                Console.ResetColor();
                yield break;
            }

            yield return step!;
        }
    }

    private async Task<PlanStep> GetNextStep(string goal, Memory memory, string userInput) => await Log.MethodAsync(
        retryCount: 2,
        shouldRetry: e => e is CsChatException cce && cce.ErrorCode == Error.EmptyResponse,
        func: async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Goal, goal);
        
        // Phase 1: Determine what tool to use
        var toolSelection = await GetToolSelection(goal, memory);
        if (done || toolSelection == null || string.IsNullOrEmpty(toolSelection.ToolName))
        {
            // No action required
            ctx.Succeeded();
            done = true;
            return PlanStep.Complete(toolSelection?.Reasoning ?? "No further action required.");
        }

        // Phase 2: Get typed input for the selected tool
        var tool = ToolRegistry.GetTool(toolSelection.ToolName);
        if (tool == null)
        {
            throw new CsChatException($"Tool '{toolSelection.ToolName}' not found.", Error.ToolNotAvailable);
        }

        var typedInput = await GetToolInput(toolSelection.ToolName, tool, goal);
        if (typedInput == null)
        {
            throw new CsChatException($"Failed to get input for tool '{toolSelection.ToolName}'.", Error.EmptyResponse);
        }

        ctx.Succeeded();
        return PlanStep.Run(toolSelection.ToolName, typedInput, toolSelection.Reasoning);
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
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

        stopwatch.Stop();
        var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
        Console.WriteLine($"{elapsedTime}ms required to process files in '{path}'.");

        ctx.Append(Log.Data.Count, files.Count);
        ctx.Succeeded();
    });

    public static async Task AddContentToVectorStore(string content, string reference = "content") => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
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
        ctx.OnlyEmitOnFailure();
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
        ctx.OnlyEmitOnFailure();
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
        ctx.OnlyEmitOnFailure();
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
        ctx.OnlyEmitOnFailure();
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

    public static async Task<(string result, Memory memory)> PostChatAsync(Memory history)
    {
        Provider.ThrowIfNull("Provider is not set.");
        var input = history.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        await MemoryContextManager.InvokeAsync(input, history);
        return await Planner.PostChatAsync(history);
    }

    public static async Task<object> PostChatAndParseTypeAsync(Memory memory, Type t) => await Log.MethodAsync(async ctx=>{
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.TypeToParse, t.Name);

        var exampleTextAttr = t.GetCustomAttribute<ExampleText>();
        if (exampleTextAttr != null)
        {
            memory.AddSystemMessage($"Example text for {t.Name}:\n{exampleTextAttr.Text}");
        }
        
        // Reflection to the rescue!
        var method = typeof(Engine).GetMethods()
            .FirstOrDefault(m => m.Name == nameof(PostChatAndParseAsync)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(Memory));

        if (method == null)
        {
            throw new InvalidOperationException($"Method {nameof(PostChatAndParseAsync)} not found.");
        }

        var genericMethod = method.MakeGenericMethod(t);
        if (genericMethod == null)
        {
            throw new InvalidOperationException($"Failed to create generic method for type {t.Name}.");
        }

        var task = genericMethod.Invoke(null, new object[] { memory }) as Task;
        if (task == null)
        {
            throw new InvalidOperationException($"Failed to invoke method {nameof(PostChatAndParseAsync)} with type {t.Name}.");
        }

        await task.ConfigureAwait(false); // await Task<T> and extract the result

        // Use reflection to get Task<T>.Result
        var resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty == null)
        {
            throw new InvalidOperationException($"Result property not found on task of type {task.GetType().Name}.");
        }

        var result = resultProperty.GetValue(task);
        if (result == null)
        {
            throw new InvalidOperationException($"Result is null for task of type {task.GetType().Name}.");
        }
        ctx.Append(Log.Data.Result, result.ToJson());
        ctx.Succeeded();        
        return result;
    });

    public static async Task<T> PostChatAndParseAsync<T>(Memory memory) where T : class
        => await Log.MethodAsync(
            shouldRetry: e => e is CsChatException cce && (cce.ErrorCode == Error.EmptyResponse || cce.ErrorCode == Error.FailedToParseResponse),
            retryCount: 2,
            func: async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var lastMessage = memory.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        ctx.Append(Log.Data.TypeToParse, typeof(T).Name);

        // Send memory to the provider's PostChatAsync method
        var response = await Provider!.PostChatAsync(memory, 0.05f);
        ctx.Append(Log.Data.Response, response);

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new CsChatException($"Received empty response from provider for: {lastMessage}.", Error.EmptyResponse);
        }

        if (response.TrimStart().StartsWith("```"))
        {
            // Remove code block markers if present
            response = Regex.Replace(response, @"^```[\w-]*\s*", "", RegexOptions.Multiline).Trim();
            response = Regex.Replace(response, @"\s*```$", "", RegexOptions.Multiline).Trim();
        }

        if (!response.TrimStart().StartsWith("{"))
        {
            throw new CsChatException($"LLM returned invalid JSON: hallucinated preamble or natural language detected. Response: {response}", Error.FailedToParseResponse);
        }

        if (!response.TrimEnd().EndsWith("}"))
        {
            throw new CsChatException($"LLM returned invalid JSON: hallucinated postamble or missing closing brace detected. Response: {response}", Error.FailedToParseResponse);
        }

        // Parse the response into the specified type
        var parsedObject = response.FromJson<T>();
        if (null == parsedObject)
        {
            ctx.Append(Log.Data.Response, response);
            throw new CsChatException($"Failed to parse response into type {typeof(T).Name}.", Error.FailedToParseResponse);
        }
        ctx.Append(Log.Data.Result, $"Successfully parsed {typeof(T).Name}");
        ctx.Succeeded();
        return parsedObject;
    });
}