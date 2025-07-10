using System;
using System.Text;
using System.Linq;
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

    public static List<(string Name, string Description, string Usage)> GetRegisteredTools() => 
        _tools.Select(t => (t.Key, t.Value.Description, t.Value.Usage)).ToList();

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

    public static async Task<string> InvokeToolAsync(string toolName, string toolInput, Memory memory, string lastUserInput) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Name, toolName);
        ctx.Append(Log.Data.ToolInput, toolInput);
        if (_tools == null || !_tools.ContainsKey(toolName))
        {
            ctx.Failed($"Tool '{toolName}' is not registered.", Error.ToolNotFound);
            return string.Empty;
        }

        var tool = _tools[toolName];
        if (tool == null)
        {
            ctx.Failed($"Tool '{toolName}' is not available.", Error.ToolNotAvailable);
            return string.Empty;
        }

        try
        {
            var result = await tool.InvokeAsync(toolInput, memory);
            ctx.Append(Log.Data.Result, $"Summarize:{result.Summarize}, ResponseText:{result.ResponseText}");

            Memory toolMemory = !result.Summarize ? result.Memory : new Memory(new[]
            {
                new ChatMessage { Role = Roles.System, Content= "Use the result of the invoked tool to answer the user's original question in natural language."},
                new ChatMessage { Role = Roles.User,
                Content = $"""
                    The user asked a question that required invoking a tool to help answer it.

                    {lastUserInput}

                    A tool, {toolName}, was invoked to help answer the user's question, the result of which was: {result.ResponseText}.

                    Explain the answer succinctly.  You do not need to reference the tool or its output directly, just use it's result to inform your response.
                """}
            });

            var formattedResponse = await Engine.Provider!.PostChatAsync(toolMemory, Program.config.Temperature);
            ctx.Succeeded();
            return formattedResponse;
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error invoking tool '{toolName}': {ex.Message}", ex);
            return string.Empty;
        }
    });
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
            return Task.FromResult(new ToolResult(Summarize: true, ResponseText: result?.ToString() ?? string.Empty, new Memory()));
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error evaluating expression '{input}'", ex);
            return Task.FromResult(new ToolResult(Summarize: false, ResponseText: string.Empty, new Memory()));
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
        ctx.Succeeded();
        return Task.FromResult(new ToolResult(Summarize: true, ResponseText: DateTime.Now.ToString("u"), new Memory()));
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
            return new ToolResult(Summarize: false, ResponseText: string.Empty, memory);
        }

        // If no results found, return a message
        ctx.Append(Log.Data.Message, "No relevant information found in the knowledge base.");
        ctx.Succeeded();
        return new ToolResult(Summarize: false, ResponseText: string.Empty, memory);
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