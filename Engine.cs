using System;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

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

        // Aggregate the list of file names into a new line delimited string and add it to the vector store.
        var knownFiles = files.Aggregate(new StringBuilder(), (sb, f) => sb.AppendLine(Path.GetFileName(f))).ToString().TrimEnd();
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

    public static async Task AddFileToVectorStore(string path) => await Log.MethodAsync(async ctx =>
    {
        // start a timer to measure the time taken to add the file
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        ctx.Append(Log.Data.FilePath, path);
        TextChunker.ThrowIfNull("Text chunker is not set. Please configure a text chunker before adding files to the vector store.");
        IEmbeddingProvider? embeddingProvider = Engine.Provider as IEmbeddingProvider;
        embeddingProvider.ThrowIfNull("Current configured provider does not support embeddings.");

        var embeddings = new List<(string Reference, string Chunk, float[] Embedding)>();
        var chunks = TextChunker!.ChunkText(path, File.ReadAllText(path));
        ctx.Append(Log.Data.Count, chunks.Count);

        await Task.WhenAll(chunks.Select(async chunk =>
            embeddings.Add((
                Reference: chunk.Reference,
                Chunk: chunk.Content,
                Embedding: await embeddingProvider!.GetEmbeddingAsync(chunk.Content)
            ))
        ));

        Engine.VectorStore.Add(embeddings);
        stopwatch.Stop();
        var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
        Console.WriteLine($"{elapsedTime}ms required to add {embeddings.Count} chunks from file '{path}' to vector store.");
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

    public static async Task<string> GetRagQueryAsync(string userMessage) => await Log.MethodAsync(async ctx =>
    {
        Provider.ThrowIfNull("Provider is not set. Please configure a provider before making requests.");
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

        var response = await Provider!.PostChatAsync(query);
        ctx.Append(Log.Data.Result, response);

        string cleaned = response.Trim();

        if (cleaned.StartsWith("NO_RAG", StringComparison.OrdinalIgnoreCase))
        {
            // If the model says NO_RAG *and nothing else*, honor it.
            if (cleaned.Equals("NO_RAG", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            // Otherwise, remove the NO_RAG prefix and extract the rest
            var withoutTag = cleaned.Substring("NO_RAG".Length).TrimStart('-', '\n', ':', ' ');
            return withoutTag.Length > 0 ? withoutTag : string.Empty;
        }
        return cleaned;
    });    
    
    public static async Task<List<SearchResult>> SearchVectorDB(string userMessage)
    {
        var empty = new List<SearchResult>();
        if (string.IsNullOrEmpty(userMessage) || null == VectorStore || VectorStore.IsEmpty) { return empty; }

        var embeddingProvider = Provider as IEmbeddingProvider;
        if (embeddingProvider == null) { return empty; }

        var query = await GetRagQueryAsync(userMessage);
        if (string.IsNullOrEmpty(query)) { return empty; }
        
        float[]? queryEmbedding = await embeddingProvider!.GetEmbeddingAsync(query);
        if (queryEmbedding == null) { return empty; }
        
        return VectorStore.Search(queryEmbedding, Program.config.RagSettings.TopK, Program.config.RagSettings.EmbeddingThreshold);            
    }

    public static async Task<ToolSuggestion?> GetToolSuggestionAsync(string userMessage) => await Log.MethodAsync(async ctx =>
    {
        Provider.ThrowIfNull("Provider is not set.");
        
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

        var response = await Provider!.PostChatAsync(memory);
        response = response.Trim();
        ctx.Append(Log.Data.Query, userMessage);
        ctx.Append(Log.Data.Response, response);

        if (response.StartsWith("```"))
        {
            response = response.Substring(3).TrimEnd('`', '\n', ' ');
            ctx.Append(Log.Data.Message, "Stripping code block.");
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

    public static async Task<string> PostChatAsync(Memory history)
    {
        Provider.ThrowIfNull("Provider is not set.");
        
        var lastUserInput = history.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";

        // === Check if tool is applicable ===
        var suggestion = await GetToolSuggestionAsync(lastUserInput);
        if (null != suggestion)
        {
            var toolOutput = await ToolRegistry.InvokeToolAsync(suggestion.Tool, suggestion.Input ?? string.Empty);
            if (!string.IsNullOrEmpty(toolOutput))
            {
                var toolMemory = new Memory(new[]
                {
                    new ChatMessage { Role = Roles.System, Content= "Use the result of the invoked tool to answer the user's original question in natural language."},
                    new ChatMessage { Role = Roles.User,
                    Content = $"""
                    The user asked a question that required invoking a tool to help answer it.

                    {lastUserInput}

                    A tool, {suggestion.Tool}, was invoked to help answer the user's question, the result of which was: {toolOutput}.

                    Explain the answer succinctly.  You do not need to reference the tool or its output directly, just use it's result to inform your response.
                """}
                });

                return await Provider!.PostChatAsync(toolMemory);
            }
        }

        // === Continue with RAG ===
        var SearchResults = await SearchVectorDB(lastUserInput);
        if (SearchResults != null && SearchResults.Count > 0)
        {
            history.ClearContext();
            foreach (var result in SearchResults)
            {
                history.AddContext(result.Reference, result.Content);
            }
        }

        return await Provider!.PostChatAsync(history);
    }
}