using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

/// <summary>
/// Naive in-Context vector store implementation.
/// This is a simple implementation for demonstration purposes and should not be used in production systems.
/// It stores vectors in Context and allows for basic search functionality using cosine similarity.
/// It does not handle persistence, scaling, or advanced search features, or ANN (Approximate Nearest Neighbor) search.
/// </summary>
public class SimpleVectorStore : IVectorStore
{
    private readonly List<(string Reference, string Chunk, float[] Embedding)> _entries = new();

    public void Add(List<(string Reference, string Chunk, float[] Embedding)> entries) =>
        _entries.AddRange(entries.Select(e => (e.Reference, e.Chunk, Normalize(e.Embedding))));

    public void Clear() => _entries.Clear();

    public bool IsEmpty => _entries.Count == 0;
    public int Count => _entries.Count;

    public List<SearchResult> SearchReferences(string reference) => _entries
        .Where(e => e.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase))
        .Select(e => new SearchResult
        {
            Score = 1.0f, // Exact match, so score is 1.0
            Reference = e.Reference,
            Content = e.Chunk
        })
        .ToList();

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

    public List<(string Reference, string Content)> GetEntries(Func<string, string, bool>? filter = null) => _entries
        .Where(e => filter == null || filter(e.Reference, e.Chunk))
        .Select(e => (e.Reference, e.Chunk))
        .ToList();
}