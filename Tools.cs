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
    public static List<(string Name, string Description, string Usage)> GetRegisteredTools(Func<string, bool> filter) => _tools
        .Where(t => filter(t.Key))
        .Select(t => (t.Key, t.Value.Description, t.Value.Usage))
        .ToList();

    public static List<(string Name, string Description, string Usage)> GetRegisteredTools() => GetRegisteredTools(All);

    public static List<string> GetToolDescriptions(Func<string, bool> filter) => _tools
        .Where(t => filter(t.Key))
        .Select(t => $"Name: {t.Key}\nDescription: {t.Value.Description}\nUsage: {t.Value.Usage}")
        .ToList();

    public static List<string> GetToolDescriptions() => GetToolDescriptions(All);
    
    public static async Task<ToolSuggestion?> GetToolSuggestionAsync(string userMessage) => await Log.MethodAsync(async ctx =>
    {
        Engine.Provider.ThrowIfNull("Provider is not set.");

        // Keep in sync with ToolSuggestion definition.
        var expectedResponseFormat = "{\n   \"tool\":\"<tool_name>\",\n   \"input\":\"<input_string>\"\n}";
        var toolDescriptions = ToolRegistry.GetRegisteredTools()
            .Select(t => $"Name: {t.Name}\nDescription: {t.Description}\nUsage: {t.Usage}")
            .ToList();

        var prompt = $"""
The following tools are available:
```{string.Join("\n", toolDescriptions)}
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