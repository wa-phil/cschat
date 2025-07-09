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
            
            var formattedResponse = await Engine.Provider!.PostChatAsync(toolMemory);
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

    public Task<ToolResult> InvokeAsync(string input, Memory memory)
    {
        try
        {
            var result = new System.Data.DataTable().Compute(input, null);
            return Task.FromResult(new ToolResult (Summarize : true, ResponseText : result?.ToString() ?? string.Empty, new Memory() ));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolResult (Summarize : false, ResponseText : $"ERROR: {ex.Message}", new Memory() ));
        }
    }
}

[IsConfigurable("CurrentDateTime")]
public class CurrentDateTimeTool : ITool
{
    public string Description => "Returns the current local date and time in UTC format.";
    public string Usage => "Input: None.";

    public Task<ToolResult> InvokeAsync(string input, Memory memory)
    {
        return Task.FromResult(new ToolResult (Summarize : true, ResponseText : DateTime.Now.ToString("u"), new Memory()));
    }
}

[IsConfigurable("SearchKnowledgeBase")]
public class RagSearchTool : ITool
{
    public string Description => "Searches the knowledge base for relevant information and adds it as context.";
    public string Usage => "Input: user message, search query or terms, and/or relevant context to use to retrieve information.";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory)
    {
        // Try to add context to memory first
        var results = await SearchVectorDB(input);
        if (results != null && results.Count > 0)
        {
            memory.ClearContext();
            foreach (var result in results)
            {
                memory.AddContext(result.Reference, result.Content);
            }
            // Return empty string to indicate context was added, no direct response needed
            return new ToolResult (Summarize : false, ResponseText : string.Empty, memory);
        }

        // If no results found, return a message
        return new ToolResult(Summarize: false, ResponseText: "No relevant information found in the knowledge base.", memory);
    }

    public async Task<List<SearchResult>> SearchVectorDB(string userMessage)
    {
        var empty = new List<SearchResult>();
        if (string.IsNullOrEmpty(userMessage) || null == Engine.VectorStore || Engine.VectorStore.IsEmpty) { return empty; }

        var embeddingProvider = Engine.Provider as IEmbeddingProvider;
        if (embeddingProvider == null) { return empty; }

        var query = await GetRagQueryAsync(userMessage);
        if (string.IsNullOrEmpty(query)) { return empty; }

        float[]? queryEmbedding = await embeddingProvider!.GetEmbeddingAsync(query);
        if (queryEmbedding == null) { return empty; }

        var items = Engine.VectorStore.Search(queryEmbedding, Program.config.RagSettings.TopK);
        // filter out below average results
        var average = items.Average(i => i.Score);
        return items.Where(i => i.Score >= average).ToList();
    }

    // Expose the original GetRagQueryAsync functionality
    public async Task<string> GetRagQueryAsync(string userMessage) => await Log.MethodAsync(async ctx =>
    {
        Engine.Provider.ThrowIfNull("Provider is not set. Please configure a provider before making requests.");
        ctx.Append(Log.Data.Query, userMessage);
        var prompt =
$"""
Based on this user message:
"{userMessage}"

Extract a concise list of keywords that would help retrieve relevant information from a knowledge base. 
Avoid answering the question. Instead, focus on what terms are most useful for searching. 

If no external information is needed, respond only with: NO_RAG
""";

        var query = new Memory(new[] {
                    new ChatMessage { Role = Roles.System, Content = "You are a query generator for knowledge retrieval." },
                    new ChatMessage { Role = Roles.User, Content = prompt }
        });

        var response = await Engine.Provider!.PostChatAsync(query);
        ctx.Append(Log.Data.Result, response);

        string cleaned = response.Trim();

        if (cleaned.StartsWith("NO_RAG", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Append(Log.Data.Message, "NO_RAG detected, processing accordingly.");
            // If the model says NO_RAG *and nothing else*, honor it.
            if (cleaned.Equals("NO_RAG", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Succeeded();
                return string.Empty;
            }

            // Otherwise, remove the NO_RAG prefix and extract the rest
            var withoutTag = cleaned.Substring("NO_RAG".Length).TrimStart('-', '\n', ':', ' ');
            ctx.Append(Log.Data.Message, $"Dirty result, extracted without NO_RAG tag: '{withoutTag}'");
            ctx.Succeeded();
            return withoutTag.Length > 0 ? withoutTag : string.Empty;
        }
        ctx.Append(Log.Data.Message, "Returning cleaned response.");
        ctx.Succeeded();
        return cleaned;
    });
}

