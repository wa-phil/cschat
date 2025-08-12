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
    private readonly List<(Reference Reference, string Chunk, float[] Embedding)> _entries = new();

    public void Add(List<(Reference Reference, string Chunk, float[] Embedding)> entries) =>
        _entries.AddRange(entries.Select(e => (e.Reference, e.Chunk, Normalize(e.Embedding))));

    public void Clear() => _entries.Clear();

    public bool IsEmpty => _entries.Count == 0;
    public int Count => _entries.Count;

    public List<SearchResult> SearchReferences(string reference) => _entries
        .Where(e => e.Reference.Source.Contains(reference, StringComparison.OrdinalIgnoreCase))
        .Select(e => new SearchResult
        {
            Score = 1.0f, // Exact match, so score is 1.0
            Reference = e.Reference,
            Content = e.Chunk
        })
        .ToList();

    // Greedy MMR selection: argmax λ·rel − (1−λ)·max sim(d, S)
    private List<int> MmrSelect(
        List<int> candidateIdxs,
        List<float> candidateRelScores, // cosine(query, doc) for each candidate
        int k,
        double lambda)
    {
        var selected = new List<int>(k);
        var remaining = new HashSet<int>(candidateIdxs);

        // quick map idx -> rel score
        var rel = new Dictionary<int, double>(candidateIdxs.Count);
        for (int t = 0; t < candidateIdxs.Count; t++)
            rel[candidateIdxs[t]] = candidateRelScores[t];

        while (selected.Count < k && remaining.Count > 0)
        {
            int best = -1;
            double bestScore = double.NegativeInfinity;

            foreach (var i in remaining)
            {
                double diversity = 0.0;
                if (selected.Count > 0)
                    diversity = selected.Max(j => CosineSimilarity(_entries[i].Embedding, _entries[j].Embedding));

                var mmr = lambda * rel[i] - (1.0 - lambda) * diversity;
                if (mmr > bestScore) { bestScore = mmr; best = i; }
            }

            selected.Add(best);
            remaining.Remove(best);
        }

        return selected;
    }

    public List<SearchResult> Search(float[] queryEmbedding, int topK = 3) =>
        Log.Method(ctx =>
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0 || _entries.Count == 0)
            return new();

        var q = Normalize(queryEmbedding); // entries are normalized on Add

        // Score all entries once
        var scored = _entries.Select((e, idx) => new { idx, score = CosineSimilarity(q, e.Embedding) })
            .OrderByDescending(x => x.score)
            .ToList();

        // Fallback: plain TopK
        bool doMmr = Program.config.RagSettings.UseMmr && topK > 1 && scored.Count > topK;
        if (!doMmr)
        {
            var resultsPlain = scored
                .Take(topK)
                .Select(x => new SearchResult
                {
                    Score = x.score,
                    Reference = _entries[x.idx].Reference,
                    Content = _entries[x.idx].Chunk ?? string.Empty
                })
                .ToList();

            ctx.Append(Log.Data.Scores, resultsPlain.Select(r => r.Score).ToList());
            ctx.Append(Log.Data.Count, resultsPlain.Count);
            ctx.Succeeded();
            return resultsPlain;
        }

        // MMR path: take a larger pool, then select topK with diversity
        int pool = (int)Math.Min(scored.Count, Math.Max(topK * Program.config.RagSettings.MmrPoolMultiplier, topK + Program.config.RagSettings.MmrMinExtra));
        var poolIdxs = scored.Take(pool).Select(x => x.idx).ToList();
        var poolScores = scored.Take(pool).Select(x => x.score).ToList();

        var selectedIdxs = MmrSelect(poolIdxs, poolScores, topK, Program.config.RagSettings.MmrLambda);

        var results = selectedIdxs
            .Select(i => new SearchResult
            {
                Score = CosineSimilarity(q, _entries[i].Embedding),
                Reference = _entries[i].Reference,
                Content = _entries[i].Chunk ?? string.Empty
            })
            .OrderByDescending(r => r.Score)
            .ToList();

        ctx.Append(Log.Data.Scores, results.Select(r => r.Score).ToList());
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
        if (a.Length != b.Length) { return 0f; } // all vectors must be of the same length. Those that aren't get a score of 0.

        float dot = 0, normA = 0, normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB) + 1e-8));
    }

    public List<(Reference Reference, string Content)> GetEntries(Func<Reference, string, bool>? filter = null) => _entries
        .Where(e => filter == null || filter(e.Reference, e.Chunk))
        .Select(e => (e.Reference, e.Chunk))
        .ToList();
}