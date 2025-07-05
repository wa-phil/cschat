using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Memory
{
    protected ChatMessage _systemMessage = new ChatMessage { Role = Roles.System, Content = string.Empty };
    protected List<ChatMessage> _messages = new List<ChatMessage>();
    protected List<(string Reference, string Chunk)> _context = new List<(string Reference, string Chunk)>();
    public Memory(string systemPrompt) => AddSystemMessage(systemPrompt);

    public Memory(IEnumerable<ChatMessage> messages)
    {
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
    }

    public void AddContext(string reference, string chunk) => _context.Add((reference, chunk));
    public void ClearContext() => _context.Clear();

    public void AddUserMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.User, Content = content });
    public void AddAssistantMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.Assistant, Content = content });
    public void AddSystemMessage(string content) =>
        _systemMessage.Content = _systemMessage.Content.Length > 0
            ? $"{_systemMessage.Content}\n{content}"
            : content;

    public void SetSystemMessage(string content) =>
        _systemMessage = new ChatMessage { Role = Roles.System, Content = content };

    public ChatMessage GetSystemMessage()
    {
        var result = new ChatMessage { Role = Roles.System, Content = _systemMessage.Content };
        if (_context.Count > 0)
        {
            result.Content += "\nWhat follows is content to help answer your next question.\n"+string.Join("\n", _context.Select(c => $"--- BEGIN CONTEXT: {c.Reference} ---\n{c.Chunk}\n--- END CONTEXT ---"));
        }
        result.Content += "\nWhen referring to the provided context in your answer, explicitly state which content you are referencing in the form 'as per [reference], [your answer]'.";
        return result;
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

    public List<SearchResult> Search(float[] queryEmbedding, int topK = 3, float threshold = 0.5f) =>
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
            .Where(e => e.Score >= threshold)
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