using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Memory
{
    protected ChatMessage _systemMessage = new ChatMessage { Role = Roles.System, Content = string.Empty };
    protected List<ChatMessage> _messages = new List<ChatMessage>();
    protected List<(string Reference, string Chunk)> _context = new List<(string Reference, string Chunk)>();
    private DateTime _conversationStartTime = DateTime.Now;
    
    public Memory(string? systemPrompt = null) 
    {
        _conversationStartTime = DateTime.Now;
        AddSystemMessage(systemPrompt ?? Program.config.SystemPrompt);
    }

    public Memory(IEnumerable<ChatMessage> messages)
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
        var data = new MemoryData
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
        var data = json.FromJson<MemoryData>();

        if (data == null)
        {
            throw new InvalidOperationException("Failed to deserialize memory data.");
        }

        _systemMessage = data.SystemMessage ?? new ChatMessage { Role = Roles.System, Content = string.Empty };
        _messages = data.Messages ?? new List<ChatMessage>();
        _context = data.Context ?? new List<(string Reference, string Chunk)>();
    }

    public Memory Clone()
    {
        return new Memory
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

    private class MemoryData
    {
        public ChatMessage? SystemMessage { get; set; }
        public List<ChatMessage>? Messages { get; set; }
        public List<(string Reference, string Chunk)>? Context { get; set; }
    }
}

/// <summary>
/// Naive in-memory vector store implementation.
/// This is a simple implementation for demonstration purposes and should not be used in production systems.
/// It stores vectors in memory and allows for basic search functionality using cosine similarity.
/// It does not handle persistence, scaling, or advanced search features, or ANN (Approximate Nearest Neighbor) search.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly List<(string Reference, string Chunk, float[] Embedding)> _entries = new();

    public void Add(List<(string Reference, string Chunk, float[] Embedding)> entries) =>
        _entries.AddRange(entries.Select(e => (e.Reference, e.Chunk, Normalize(e.Embedding))));

    public void Clear() => _entries.Clear();

    public bool IsEmpty => _entries.Count == 0;
    public int Count => _entries.Count;

    public List<SearchResult> Search(float[] queryEmbedding, int topK = 3) =>
        Log.Method(ctx =>
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
        {
            return new();
        }

        var normalizedQuery = Normalize(queryEmbedding);

        var results = _entries
            .Select(entry => new SearchResult
            {
                Score = CosineSimilarity(normalizedQuery, entry.Embedding),
                Reference = entry.Reference ?? string.Empty,
                Content = entry.Chunk ?? string.Empty
            })
            .OrderByDescending(e => e.Score)
            .Take(topK)
            .ToList();

        var topScores = results.Select(item => item.Score).ToList();
        ctx.Append(Log.Data.Scores, topScores);
        ctx.Append(Log.Data.Count, results.Count);
        ctx.Succeeded();
        return results;
    });

    private static float[] Normalize(float[] vector)
    {
        float norm = (float)Math.Sqrt(vector.Sum(v => v * v));
        if (norm < 1e-8) return vector; // avoid divide-by-zero
        return vector.Select(v => v / norm).ToArray();
    }    

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vector dimensions do not match.");

        float dot = 0, normA = 0, normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB) + 1e-8));
    }
}

public class MemoryContextManager
{
    public async Task InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        memory.ClearContext();

        // Try to add context to memory first
        var references = new List<string>();
        var results = await SearchVectorDB(input);
        if (results != null && results.Count > 0)
        {
            foreach (var result in results)
            {
                references.Add(result.Reference);
                memory.AddContext(result.Reference, result.Content);
            }
            // Context was added, no summary response required, returning modified memory back to caller.
            ctx.Append(Log.Data.Result, references.ToArray());
            ctx.Succeeded();
            return;
        }

        // If no results found, return a message
        memory.AddContext("Memory", "No special or relevant information about current context.");
        ctx.Append(Log.Data.Message, "Nothing relevant in the knowledge base.");
        ctx.Succeeded();
        return;
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