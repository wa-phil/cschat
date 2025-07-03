using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Memory
{
    protected ChatMessage _systemMessage = new ChatMessage { Role = Roles.System, Content = string.Empty };
    protected List<ChatMessage> _messages = new List<ChatMessage>();
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
            if (_systemMessage.Content.Length > 0)
            {
                result.Add(_systemMessage);
            }
            result.AddRange(_messages);
            return result;
        }
    }

    public void Clear()
    {
        _systemMessage.Content = string.Empty;
        _messages.Clear();
    }

    public void AddUserMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.User, Content = content });
    public void AddAssistantMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.Assistant, Content = content });
    public void AddSystemMessage(string content) =>
        _systemMessage.Content = _systemMessage.Content.Length > 0
            ? $"{_systemMessage.Content}\n{content}"
            : content;
            
    public void SetSystemMessage(string content) => 
        _systemMessage = new ChatMessage { Role = Roles.System, Content = content };
}

/// <summary>
/// Naive in-memory vector store implementation.
/// This is a simple implementation for demonstration purposes and should not be used in production systems.
/// It stores vectors in memory and allows for basic search functionality using cosine similarity.
/// It does not handle persistence, scaling, or advanced search features, or ANN (Approximate Nearest Neighbor) search.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly List<(string FilePath, int Offset, string Chunk, float[] Embedding)> _entries = new();

    public void Add(string filePath, List<(int Offset, string Chunk, float[] Embedding)> entries) =>
        entries.ForEach(e => _entries.Add((filePath, e.Offset, e.Chunk, e.Embedding)));

    public void Clear() => _entries.Clear();

    public bool IsEmpty => _entries.Count == 0;

    public List<(string FilePath, int Offset, string Content)> Search(float[] queryEmbedding, int topK = 3) =>
        Log.Method(ctx =>
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
        {
            return new();
        }

        var results = _entries
            .Select(entry => new
            {
                entry.FilePath,
                entry.Offset,
                entry.Chunk,
                Score = CosineSimilarity(queryEmbedding, entry.Embedding)
            })
            .OrderByDescending(e => e.Score)
            .Take(topK)
            .Select(e => (e.FilePath, e.Offset, e.Chunk))
            .ToList();
        ctx.Append(Log.Data.Count, results.Count);
        ctx.Succeeded();
        return results;
    });

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