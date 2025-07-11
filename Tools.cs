using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;

// Can't use a record here as JSON parser doesn't support parsing records. :(
public class ToolSuggestion
{
    [DataMember(Name = "tool")]
    public string Tool { get; set;} = string.Empty;

    [DataMember(Name = "input")]
    public string Input { get; set;} = string.Empty;
}

public static class ToolRegistry
{
    private static Dictionary<string, ITool> _tools = new Dictionary<string, ITool>();
    public static void Initialize() => Log.Method(ctx =>
    {
        List<string> registered = new List<string>();
        Program.serviceProvider.ThrowIfNull("Service provider is not initialized.");
        foreach (var tool in Program.Tools)
        {
            if (Program.serviceProvider!.GetService(tool.Value) is ITool t)
            {
                _tools[tool.Key] = t;
                registered.Add(tool.Key);
            }

        }
        ctx.Append(Log.Data.Count, registered.Count);
        ctx.Append(Log.Data.Registered, registered.ToArray());
        ctx.Succeeded();
    });

    private static bool All(string _) => true;
    public static List<(string Name, string Description, string Usage)> GetRegisteredTools(Func<string, bool> func) => _tools
        .Where(t => func(t.Key))
        .Select(t => (t.Key, t.Value.Description, t.Value.Usage))
        .ToList();

    public static List<(string Name, string Description, string Usage)> GetRegisteredTools() => GetRegisteredTools(All);

    public static List<string> GetToolDescriptions() => _tools
        .Select(t => $"Name: {t.Key}\nDescription: {t.Value.Description}\nUsage: {t.Value.Usage}")
        .ToList();

    public static async Task<ToolSuggestion?> GetToolSuggestionAsync(string userMessage) => await Log.MethodAsync(async ctx =>
    {
        Engine.Provider.ThrowIfNull("Provider is not set.");

        // Keep in sync with ToolSuggestion definition.
        var expectedResponseFormat = "{\n   \"tool\":\"<tool_name>\",\n   \"input\":\"<input_string>\"\n}";

        var prompt = $"""
The following tools are available:
```{string.Join("\n", GetToolDescriptions())}
```

Given the following user request:

"{userMessage}"

If one of these tools is appropriate to use, respond in the following JSON format:

{expectedResponseFormat}

If no tool is appropriate, respond with: NO_TOOL
""";

        var memory = new Memory(new[]
        {
            new ChatMessage { Role = Roles.System, Content = "You are a tool router. Only suggest tools from the list above." },
            new ChatMessage { Role = Roles.User, Content = prompt }
        });

        var response = await Engine.Provider!.PostChatAsync(memory, 0.0f);
        response = response.Trim();
        ctx.Append(Log.Data.Query, userMessage);
        ctx.Append(Log.Data.Response, response);

        if (response.StartsWith("```"))
        {
            response = response.Substring(3).TrimEnd('`', '\n', ' ');
            ctx.Append(Log.Data.Result, "Stripping code block.");
        }

        if (response.StartsWith("json", StringComparison.OrdinalIgnoreCase))
        {
            response = response.Substring(4).TrimStart(':', ' ', '\n');
            ctx.Append(Log.Data.Result, "Stripping JSON prefix.");
        }

        if (response.StartsWith("NO_TOOL", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Append(Log.Data.Message, "No tool suggestion made by the model.");
            ctx.Succeeded();
            return null;
        }

        try
        {
            var parsed = response.FromJson<ToolSuggestion>();
            if (parsed != null && parsed.Tool != null && ToolRegistry.GetRegisteredTools().Any(t => t.Name.Equals(parsed.Tool, StringComparison.OrdinalIgnoreCase)))
            {
                ctx.Append(Log.Data.ToolName, parsed.Tool);
                ctx.Append(Log.Data.ToolInput, parsed.Input);
                ctx.Succeeded();
                return parsed;
            }
            ctx.Failed("Response does not match expected format or tool not registered.", Error.ToolNotAvailable);
        }
        catch (Exception ex)
        {
            ctx.Append(Log.Data.Exception, ex.Message);
            ctx.Failed("Failed to parse response.", Error.FailedToParseResponse);
        }

        return null;
    });

    internal static async Task<ToolResult> InvokeInternalAsync(string toolName, string toolInput, Memory memory, string userInput) =>
        await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Name, toolName);
        ctx.Append(Log.Data.ToolInput, toolInput);

        if (_tools == null || !_tools.ContainsKey(toolName))
        {
            ctx.Failed($"Tool '{toolName}' is not registered.", Error.ToolNotAvailable);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' is not registered.", memory);
        }

        var tool = _tools[toolName];
        if (tool == null)
        {
            ctx.Failed($"Tool '{toolName}' is not available.", Error.ToolNotAvailable);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' is not available.", memory);
        }
        var toolResult = await tool.InvokeAsync(toolInput, memory);
        ctx.Append(Log.Data.Result, $"Summarize:{toolResult.Summarize}, ResponseText:{toolResult.Response}");

        if (null == toolResult)
        {
            ctx.Failed($"Tool '{toolName}' returned null result.", Error.ToolFailed);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' returned null result.", memory);
        }

        if (!toolResult.Succeeded)
        {
            var error = toolResult.Error ?? "Unknown error";
            ctx.Append(Log.Data.Error, error);
            ctx.Failed($"Tool '{toolName}' failed: {error}", Error.ToolFailed);
            return ToolResult.Failure(error, memory);
        }

        Memory toolMemory = !toolResult.Summarize ? toolResult.Memory : new Memory(new[]
        {
            new ChatMessage { Role = Roles.System, Content= "Use the result of the invoked tool to answer the user's original question in natural language."},
            new ChatMessage { Role = Roles.User,
            Content = $"""
                The user asked a question that required invoking a tool to help answer it.

                {userInput}

                A tool, {toolName}, was invoked to help answer the user's question, the result of which was: {toolResult.Response}.

                Explain the answer succinctly.  You do not need to reference the tool or its output directly, just use it's result to inform your response.
            """}
        });

        var formattedResponse = await Engine.Provider!.PostChatAsync(toolMemory, Program.config.Temperature);
        ctx.Succeeded();
        return ToolResult.Success(formattedResponse, toolResult.Memory, toolResult.Summarize);
    });
    
    public static async Task<string> InvokeToolAsync(string toolName, string toolInput, Memory memory, string lastUserInput)
    {
        var result = await InvokeInternalAsync(toolName, toolInput, memory, lastUserInput);
        return result.Response;
    }
}

[IsConfigurable("Calculator")]
public class CalculatorTool : ITool
{
    public string Description => "Evaluates basic math expressions.";
    public string Usage => "Input: arithmetic expression as string (e.g. '2 + 3 * 4')";

    public Task<ToolResult> InvokeAsync(string input, Memory memory) => Log.Method(ctx =>
    {
        try
        {
            var result = new System.Data.DataTable().Compute(input, null);
            ctx.Succeeded();
            return Task.FromResult(ToolResult.Success(result?.ToString() ?? string.Empty, memory, true));
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error evaluating expression '{input}'", ex);
            return Task.FromResult(ToolResult.Failure($"Error evaluating expression '{input}'", memory));
        }
    });
}

[IsConfigurable("CurrentDateTime")]
public class CurrentDateTimeTool : ITool
{
    public string Description => "Returns the current local date and time in UTC format.";
    public string Usage => "Input: None.";

    public Task<ToolResult> InvokeAsync(string input, Memory memory) => Log.Method(ctx =>
    {
        var result = DateTime.Now.ToString("u");
        ctx.Succeeded();
        return Task.FromResult(ToolResult.Success(result, memory, true));
    });
}

[IsConfigurable("SearchKnowledgeBase")]
public class RagSearchTool : ITool
{
    public string Description => "Searches the knowledge base for relevant information and adds it as context.";
    public string Usage => "Input: search query or terms, and any other relevant context to use to retrieve information, all as a comma delimited string.";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        // Try to add context to memory first
        var references = new List<string>();
        var results = await SearchVectorDB(input);
        if (results != null && results.Count > 0)
        {
            memory.ClearContext();
            foreach (var result in results)
            {
                references.Add(result.Reference);
                memory.AddContext(result.Reference, result.Content);
            }
            // Context was added, no summary response required, returning modified memory back to caller.
            ctx.Append(Log.Data.Result, references.ToArray());
            ctx.Succeeded();
            return ToolResult.Success(string.Empty, memory, false);
        }

        // If no results found, return a message
        ctx.Append(Log.Data.Message, "No relevant information found in the knowledge base.");
        ctx.Succeeded();
        return ToolResult.Failure("No relevant information found in the knowledge base.", memory);
    });

    public async Task<List<SearchResult>> SearchVectorDB(string userMessage)
    {
        var empty = new List<SearchResult>();
        if (string.IsNullOrEmpty(userMessage) || null == Engine.VectorStore || Engine.VectorStore.IsEmpty) { return empty; }

        var embeddingProvider = Engine.Provider as IEmbeddingProvider;
        if (embeddingProvider == null) { return empty; }

        float[]? query = await embeddingProvider!.GetEmbeddingAsync(userMessage);
        if (query == null) { return empty; }

        var items = Engine.VectorStore.Search(query, Program.config.RagSettings.TopK);
        // filter out below average results
        var average = items.Average(i => i.Score);
        return items.Where(i => i.Score >= average).ToList();
    }
}

[IsConfigurable("GetFileNames")]
public class GetFileNamesTool : ITool
{
    public string Description => "Gets the names of supported source/text files in the specified directory recursively.";
    public string Usage => "Input: optional path (defaults to current directory).";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        var path = string.IsNullOrWhiteSpace(input) ? Directory.GetCurrentDirectory() : input;
        ctx.Append(Log.Data.Path, path);

        if (!Directory.Exists(path))
        {
            ctx.Failed($"Directory not found: {path}", Error.PathNotFound);
            return ToolResult.Failure($"ERROR: Directory not found: {path}", memory);
        }

        var supportedTypes = Engine.supportedFileTypes;
        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var list = string.Join("\n", files.Select(f => Path.GetRelativePath(path, f)));
        memory.ClearContext();
        memory.AddContext("file_list", list);
        await Engine.AddContentToVectorStore(list, "file_list");
        ctx.Succeeded();
        return ToolResult.Success($"Found {files.Count} files:\n{list}", memory, true);
    });
}

[IsConfigurable("FileMetadata")]
public class FileMetadataTool : ITool
{
    public string Description => "Provides metadata about a file including size, line count, and last modified date.";
    public string Usage => "Input: path to a file.";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            return ToolResult.Failure($"ERROR: File not found: {input}", memory);
        }

        var fileInfo = new FileInfo(input);
        var size = fileInfo.Length;
        var modified = fileInfo.LastWriteTime;

        string[] lines = await File.ReadAllLinesAsync(input);
        int lineCount = lines.Length;
        int wordCount = lines.Sum(line => line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries).Length);
        int charCount = lines.Sum(line => line.Length);

        var sb = new StringBuilder();
        sb.AppendLine($"File: {input}");
        sb.AppendLine($"Size: {size:N0} bytes");
        sb.AppendLine($"Lines: {lineCount:N0}");
        sb.AppendLine($"Words: {wordCount:N0}");
        sb.AppendLine($"Characters: {charCount:N0}");
        sb.AppendLine($"Last Modified: {modified:u}");
        memory.ClearContext();
        memory.AddContext("file_metadata", sb.ToString());
        await Engine.AddContentToVectorStore(sb.ToString(), "file_metadata");
        ctx.Succeeded();
        return ToolResult.Success(sb.ToString(), memory, true);
    });
}

[IsConfigurable("SummarizeFile")]
public class SummarizeFileTool : ITool
{
    public static readonly int MaxContentLength = 16000; // Maximum length of content to read
    public string Description => "Reads and summarizes the contents of a source/text file.";
    public string Usage => "Input: path to a file.";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            ctx.Failed($"File not found: {input}", Error.PathNotFound);
            return ToolResult.Failure($"ERROR: File not found: {input}", memory);
        }

        await Engine.AddFileToVectorStore(input);
        var content = File.ReadAllText(input);
        if (content.Length > MaxContentLength)
        {
            ctx.Append(Log.Data.Message, $"Content truncated to {MaxContentLength} characters.");
            content = content.Substring(0, MaxContentLength) + "\n... [truncated]";
        }

        ctx.Succeeded();
        return ToolResult.Success($"Contents of {input}:\n{content}", memory, true);
    });
}

public class PlanStep
{
    public string Description { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; }
}

[IsConfigurable("Planner")]
public class PlannerTool : ITool
{
    public string Description => "Dynamically plans and executes multi-step tasks using available tools.";
    public string Usage => "Input: A natural language goal, e.g., 'Summarize the repo contents'.";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        var results = new List<string>();
        var usedSteps = new HashSet<string>();
        var stepFeedback = new List<string>();
        int stepCounter = 0;
        int maxSteps = 10;

        while (stepCounter < maxSteps)
        {
            var step = await GetNextStep(input, memory, results);
            if (step == null || string.IsNullOrEmpty(step.ToolName) || step.ToolName.StartsWith("No further action required", StringComparison.OrdinalIgnoreCase))
                break;

            var key = $"{step.ToolName}:{step.ToolInput}";
            if (usedSteps.Contains(key))
            {
                Console.WriteLine($"Skipping repeated step: {key}");
                break;
            }
            usedSteps.Add(key);

            Console.Write($"Step {stepCounter + 1}: {step.Description} using tool {step.ToolName} with {step.ToolInput ?? "no input"}... ");

            stepCounter++;
            var result = await ToolRegistry.InvokeInternalAsync(step.ToolName, step.ToolInput ?? string.Empty, memory, input);
            var status = result.Succeeded ? "✅" : "❌";
            Console.WriteLine($" - {status}\n---\n{result.Response}\n---");

            var stepSummary = $"Step {stepCounter}: {step.Description}\n{result.Response}";
            results.Add(stepSummary);
            memory.AddAssistantMessage(stepSummary);

            // Capture feedback
            var feedback = $"{status} Step {stepCounter}" + (result.Succeeded
                ? $" succeeded using {step.ToolName}."
                : $" failed using {step.ToolName}. Reason: {result.Error ?? "Unknown error"}");
            stepFeedback.Add(feedback);
        }

        // Final summarization with feedback loop
        var summary = new Memory(new[]
        {
            new ChatMessage { Role = Roles.System, Content = "Summarize the results of the steps below for the user. Include insights about which steps worked well, which did not, and what could improve future attempts." },
            new ChatMessage { Role = Roles.User, Content = input }
        });
        results.ForEach(r => summary.AddAssistantMessage(r));
        stepFeedback.ForEach(fb => summary.AddAssistantMessage(fb));

        var final = await Engine.Provider!.PostChatAsync(summary, Program.config.Temperature);
        ctx.Succeeded();
        return ToolResult.Success(final, memory);
    });

    private async Task<PlanStep?> GetNextStep(string goal, Memory memory, List<string> results) => await Log.MethodAsync(async ctx =>
    {
        var thisToolName = typeof(PlannerTool).GetCustomAttribute<IsConfigurable>()?.Name ?? "PlannerTool";
        var noActionRequired = "{\"Description\": \"No further action required.\", \"ToolName\": null, \"ToolInput\": null}";
        var takeFollowingAction = "{\"Description\": \"...\", \"ToolName\": \"ToolName\", \"ToolInput\": \"Tool input\"}";
        var context = string.Join("\n", results.TakeLast(5));

        var tools = ToolRegistry.GetRegisteredTools()
            .Where(t => t.Name != thisToolName)
            .Select(t => $"Name: {t.Name}\nDescription: {t.Description}\nUsage: {t.Usage}")
            .ToList();

        var prompt = $"""
    The user has a goal: "{goal}"

    You have the following knowledge so far:
    {context}

    Determine the next best step to help reach the user's goal. If a step previously failed, consider what other information or actions might be required to succeed before retrying it. Reflect on which steps have worked well so far and which have not, and adapt your plan accordingly.

    If no further action is needed, respond with:
    {noActionRequired}

    Otherwise, respond with:
    {takeFollowingAction}

    Only use tools from this list exactly as named:
    {string.Join("\n", tools)}
    """;

        var planMemory = new Memory(new[]
        {
            new ChatMessage { Role = Roles.System, Content = "You are a stepwise planner. Respond only with a single next step." },
            new ChatMessage { Role = Roles.User, Content = prompt }
        });
        results.ForEach(r => planMemory.AddAssistantMessage(r));

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
