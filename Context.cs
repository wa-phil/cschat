using System;
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

    public IEnumerable<ChatMessage> Messages
    {
        get
        {
            var result = new List<ChatMessage>();
            result.Add(GetSystemMessage());
            result.AddRange(_messages);
            return result;
        }
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
            result.Content += "\nWhat follows is content to help answer your next question.\n"+string.Join("\n", _context.Select(c => $"--- BEGIN CONTEXT: {c.Reference} ---\n{c.Chunk}\n--- END CONTEXT ---"));
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
    public static async Task InvokeAsync(string input, Context Context) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Context.ClearContext();

        // Try to add context to Context first
        var references = new List<string>();
        var results = await SearchVectorDB(input);
        if (results != null && results.Count > 0)
        {
            foreach (var result in results)
            {
                references.Add(result.Reference);
                Context.AddContext(result.Reference, result.Content);
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
        IEmbeddingProvider? embeddingProvider = Engine.Provider as IEmbeddingProvider;
        embeddingProvider.ThrowIfNull("Current configured provider does not support embeddings.");

        ctx.Append(Log.Data.Reference, reference);
        var embeddings = new List<(string Reference, string Chunk, float[] Embedding)>();
        var chunks = Engine.TextChunker!.ChunkText(reference, content);
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

    public static async Task<List<SearchResult>> SearchVectorDB(string userMessage)
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