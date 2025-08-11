using System;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Context
{
    protected ChatMessage _systemMessage = new ChatMessage { Role = Roles.System, Content = string.Empty };
    protected List<ChatMessage> _messages = new List<ChatMessage>();
    protected List<(string Reference, string Chunk)> _context = new List<(string Reference, string Chunk)>();
    private DateTime _conversationStartTime = DateTime.Now;

    public Context(string? systemPrompt = null)
    {
        _conversationStartTime = DateTime.Now;
        AddSystemMessage(systemPrompt ?? Program.config.SystemPrompt);
    }

    public Context(IEnumerable<ChatMessage> messages)
    {
        _conversationStartTime = DateTime.Now;
        foreach (var msg in messages)
        {
            if (msg.Role == Roles.System)
                AddSystemMessage(msg.Content);
            else
                _messages.Add(msg);
        }
    }

    public IEnumerable<ChatMessage> Messages(bool InluceSystemMessage = true)
    {
        var result = new List<ChatMessage>();
        if (InluceSystemMessage) { result.Add(GetSystemMessage()); }
        result.AddRange(_messages);
        return result;
    }

    public void Clear()
    {
        _systemMessage.Content = string.Empty;
        _context.Clear();
        _messages.Clear();
        _conversationStartTime = DateTime.Now;
    }

    public void AddContext(string reference, string chunk) => _context.Add((reference, chunk));
    public void ClearContext() => _context.Clear();
    public List<(string Reference, string Chunk)> GetContext() => new List<(string Reference, string Chunk)>(_context);

    public void AddUserMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.User, Content = content });
    public void AddAssistantMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.Assistant, Content = content });
    public void AddToolMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.Tool, Content = content });
    public void AddSystemMessage(string content)
    {
        _systemMessage.Content = _systemMessage.Content.Length > 0
            ? $"{_systemMessage.Content}\n{content}"
            : content;
        // Ensure system message has the conversation start time, not current time
        _systemMessage.CreatedAt = _conversationStartTime;
    }

    public void SetSystemMessage(string content)
    {
        _systemMessage = new ChatMessage
        {
            Role = Roles.System,
            Content = content,
            CreatedAt = _conversationStartTime
        };
    }

    public ChatMessage GetSystemMessage()
    {
        var result = new ChatMessage { Role = Roles.System, Content = _systemMessage.Content };
        if (_context.Count > 0)
        {
            result.Content += "\nWhat follows is content to help answer your next question.\n" + string.Join("\n", _context.Select(c => $"--- BEGIN CONTEXT: {c.Reference} ---\n{c.Chunk}\n--- END CONTEXT ---"));
            result.Content += "\nWhen referring to the provided context in your answer, explicitly state which content you are referencing in the form 'as per [reference], [your answer]'.";
        }
        return result;
    }

    public void Save(string filePath)
    {
        var data = new ContextData
        {
            SystemMessage = _systemMessage,
            Messages = _messages,
            Context = _context
        };

        var json = data.ToJson();
        System.IO.File.WriteAllText(filePath, json);
    }

    public void Load(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var json = System.IO.File.ReadAllText(filePath);
        var data = json.FromJson<ContextData>();

        if (data == null)
        {
            throw new InvalidOperationException("Failed to deserialize Context data.");
        }

        _systemMessage = data.SystemMessage ?? new ChatMessage { Role = Roles.System, Content = string.Empty };
        _messages = data.Messages ?? new List<ChatMessage>();
        _context = data.Context ?? new List<(string Reference, string Chunk)>();
    }

    public Context Clone()
    {
        return new Context
        {
            _systemMessage = new ChatMessage
            {
                Role = Roles.System,
                Content = $"{_systemMessage.Content}", // Ensure a deep copy of the content for system message -- SUPER IMPORTANT
                CreatedAt = _systemMessage.CreatedAt
            },
            _messages = new List<ChatMessage>(_messages),
            _context = new List<(string Reference, string Chunk)>(_context),
            _conversationStartTime = _conversationStartTime
        };
    }

    private class ContextData
    {
        public ChatMessage? SystemMessage { get; set; }
        public List<ChatMessage>? Messages { get; set; }
        public List<(string Reference, string Chunk)>? Context { get; set; }
    }
}

public class ContextManager
{
    public static GraphStore GraphStore { get; } = new GraphStore();
    
    public static List<(Reference Reference, string MergedContent)> Flatten(List<(Reference Reference, string Content)> entries)
    {
        var grouped = entries                           // grouped is Dictionary: source -> chunks, where chunks: List<(Start, End, Lines)>
            .GroupBy(entry => entry.Reference.Source)   // Group chunks by source file
            .ToDictionary(                              // use GroupBy + ToDictionary to correctly handle multiple source references
                g => g.Key,                             // key: source
                g => g.Select(entry => (
                    Start: entry.Reference.Start,       // nullable start line
                    End: entry.Reference.End,           // nullable end line
                    Lines: entry.Content.Split('\n')    // List<string>
                )).ToList()                             // Change from Enumerable -> List to avoid multiple reevaluations later
            );

        return grouped.Select(kvp => // flatten Dictionary: source -> chunks to List<(Reference, Content)>
        {
            var source = kvp.Key;
            var chunks = kvp.Value;

            var lineMap = chunks                                    // for files that have chunks
                .Where(c => c.Start.HasValue && c.End.HasValue)     // only consider line-ranged chunks
                .SelectMany(c => c.Lines                            // calculate line numbers for every line
                    .Select((line, idx) => (LineNumber: c.Start!.Value + idx, Content: line)))
                .GroupBy(x => x.LineNumber)                         // group by line number to deduplicate
                .ToDictionary(g => g.Key, g => g.First().Content);  // keep first occurrence of each line

            var fullChunks = chunks                                 // for whole files, or content w/ only one reference.
                .Where(c => !c.Start.HasValue || !c.End.HasValue)   // whole-content chunks (no line info)
                .SelectMany(c => c.Lines)                           // just extract their lines
                .ToList();

            var merged = lineMap                                    // put it all together...
                .OrderBy(kv => kv.Key)                              // order lines by line number
                .Select(kv => kv.Value)                             // get the content
                .Concat(fullChunks)                                 // append whole-content chunks at the end
                .ToList();                                          // convert to List to avoid multiple reevaluations

            var content = string.Join("\n", merged);                // join into final output
            var minLine = lineMap.Keys.DefaultIfEmpty().Min();      // min line number for ref
            var maxLine = lineMap.Keys.DefaultIfEmpty().Max();      // max line number for ref

            var reference = lineMap.Count > 0
                ? Reference.Partial(source, minLine, maxLine)       // ranged ref if we had any line-based chunks
                : Reference.Full(source);                           // otherwise fallback to whole-content

            return (reference, content);                            // return the combined entry
        }).ToList(); // gather all merged entries
    }

    public static async Task InvokeAsync(string input, Context Context) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Context.ClearContext();

        // Try to add context to Context first
        var references = new List<string>();
        var content = await SearchVectorDB(input);
        if (content != null && content.Count > 0)
        {
            var results = Flatten(content.Select(r => (r.Reference, r.Content)).ToList());
            foreach (var result in results)
            {
                references.Add(result.Reference.ToString());
                Context.AddContext(result.Reference.ToString(), result.MergedContent);
            }
            // Context was added, no summary response required, returning modified Context back to caller.
            ctx.Append(Log.Data.Result, references.ToArray());
            ctx.Succeeded();
            return;
        }

        // If no results found, return a message
        Context.AddContext("Context", "No special or relevant information about current context.");
        ctx.Append(Log.Data.Message, "Nothing relevant in the knowledge base.");
        ctx.Succeeded();
        return;
    });

    public static async Task AddContent(string content, string reference = "content") => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Engine.TextChunker.ThrowIfNull("Text chunker is not set. Please configure a text chunker before adding files to the vector store.");        
        IEmbeddingProvider embeddingProvider = Engine.Provider as IEmbeddingProvider ?? throw new InvalidOperationException("Current configured provider does not support embeddings.");

        var GetEmbeddingAsync = Program.config.RagSettings.UseEmbeddings
            ? embeddingProvider!.GetEmbeddingAsync
            : (Func<string, Task<float[]>>) (text => Task.FromResult(new float[0])); // Return empty embedding if embeddings are not used

        ctx.Append(Log.Data.Reference, reference.Substring(0, Math.Min(reference.Length, 50)));
        var embeddings = new List<(Reference Reference, string Chunk, float[] Embedding)>();
        var chunks = Engine.TextChunker!.ChunkText(reference, content);
        ctx.Append(Log.Data.Count, chunks.Count);

        await Task.WhenAll(chunks.Select(async chunk =>
            embeddings.Add((
                Reference: chunk.Reference,
                Chunk: chunk.Content,
                Embedding: await GetEmbeddingAsync(chunk.Content)
            ))
        ));

        Engine.VectorStore.Add(embeddings);
        ctx.Succeeded(embeddings.Count > 0);
    });

    public static async Task AddGraphContent(string content, string reference = "content") => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Engine.TextChunker.ThrowIfNull("Text chunker is not set. Please configure a text chunker before adding files to the vector store.");
        IGraphProvider? graphProvider = Engine.Provider as IGraphProvider;
        graphProvider.ThrowIfNull("Current configured provider does not support graph operations.");

        ctx.Append(Log.Data.Reference, reference);
        var chunks = Engine.TextChunker!.ChunkText(reference, content);
        ctx.Append(Log.Data.Count, chunks.Count);

        var chunksProcessed = 0;
        
        foreach (var chunk in chunks)
        {
            await graphProvider!.GetEntitiesAndRelationshipsAsync(chunk.Content, chunk.Reference.ToString());
            
            chunksProcessed++;
        }
        
        ctx.Append(Log.Data.Result, $"Processed {chunksProcessed} chunks and stored graph data");
        ctx.Succeeded(chunksProcessed > 0);
    });    

    public static async Task<List<SearchResult>> SearchReferences(string reference) => await Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var results = new List<SearchResult>();
        if (string.IsNullOrEmpty(reference) || null == Engine.VectorStore || Engine.VectorStore.IsEmpty)
        {
            return Task.FromResult(results);
        }

        results = Engine.VectorStore.SearchReferences(reference);
        ctx.Append(Log.Data.Result, results.Select(r => r.Reference).ToArray());
        ctx.Succeeded();
        return Task.FromResult(results);
    });

    public static async Task<List<SearchResult>> SearchVectorDB(string userMessage) => await Log.MethodAsync(async ctx =>
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

        var results = items.Where(i => i.Score >= average).ToList();
        if (results.Count == 0)
        {
            ctx.Append(Log.Data.Message, "No relevant results found in the vector store.");
        }
        else
        {
            ctx.Append(Log.Data.Result, results.Select(r => r.Reference).ToArray());
        }
        ctx.Succeeded();
        return results;
    });
}