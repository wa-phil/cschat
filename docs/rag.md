# RAG Pipeline

Retrieval-Augmented Generation (RAG) enriches LLM responses with content from local files. The pipeline runs at two times:

- **Ingest** — content is chunked, embedded, and stored in the vector store.
- **Query** — each user message is embedded, the store is searched, and the top results are injected into the system message before the LLM call.

## Overview

```
Ingest path:
  file/dir/zip
    → Engine.AddFile/Dir/Zip ToVectorStore
        → ReadFilesFromDirectory / ReadFilesFromZip
            → ShouldIncludeFile (RagFileType matching)
        → ContextManager.AddContent(content, reference)
            → ITextChunker.ChunkText → List<(Reference, Content)>
            → SHA-256 embedding cache lookup
            → IEmbeddingProvider.GetEmbeddingsAsync (batched)
            → IVectorStore.Add(entries)

Query path (per turn):
  user message
    → ContextManager.InvokeAsync(input, context)
        → ContextManager.SearchVectorDB(input)
            → IEmbeddingProvider.GetEmbeddingAsync(query)
            → IVectorStore.Search(queryEmbedding, topK)  [MMR optional]
            → filter below-average scores
        → Flatten overlapping chunks
        → Context.AddContext(reference, mergedContent)
```

## RagFileType

**File:** `RagFileType.cs`
**UserManaged name:** `"RAG File Type"`

Controls which file types are included during ingest and what line-level filters apply:

| Field | Description |
|-------|-------------|
| `Extension` | File extension including leading dot (e.g. `.cs`, `.md`) — the `[UserKey]` |
| `Enabled` | If `false`, files with this extension are skipped entirely |
| `Include` | Regex patterns; a file must match at least one (if any are present) |
| `Exclude` | Regex patterns; if any match, the file is skipped |
| `Description` | Optional human-readable note |

`Engine.RefreshSupportedFileTypesFromUserManaged()` syncs the fast `SupportedFileTypes` list from the enabled entries. It is called before each directory walk.

Migration: on first run, if no `RagFileType` entries exist, they are auto-populated from the legacy `RagSettings.SupportedFileTypes` list (which is now `[Obsolete]`).

## Text Chunkers

Located in `TextChunkers.cs`. All implement `ITextChunker`:

```csharp
public interface ITextChunker
{
    List<(Reference Reference, string Content)> ChunkText(string path, string text);
}
```

### SmartChunk (default)

**Registration name:** `"SmartChunk"`

Token-budget-based chunker. Accumulates lines until the approximate token count (characters ÷ 3) exceeds `config.RagSettings.MaxTokensPerChunk`. Applies line-level include/exclude regex filters from `RagFileType` entries. Lines longer than `MaxLineLength` characters are dropped. Returns a `Reference.Full(path)` when the entire file fits in one chunk; `Reference.Partial(path, start, end)` otherwise.

### LineChunk

**Registration name:** `"LineChunk"`

Groups lines in fixed-size windows of `ChunkSize` lines with `Overlap` overlap. Good for log files and other line-oriented content.

### BlockChunk

**Registration name:** `"BlockChunk"`

Fixed character-count windows of `ChunkSize` characters with `Overlap` overlap. Suitable for binary-like or non-line-oriented content.

## VectorStore

**File:** `Memory/VectorStore.cs` — `SimpleVectorStore` implementing `IVectorStore`

An in-memory store of `(Reference, Chunk, float[] Embedding)` tuples. Embeddings are L2-normalized on `Add`.

### Search

`Search(queryEmbedding, topK)` scores all entries by cosine similarity. If `UseMmr = true` (default), Maximal Marginal Relevance is applied:

1. Score all entries.
2. Build a candidate pool of size `topK × MmrPoolMultiplier` (at least `topK + MmrMinExtra`).
3. Greedily select `topK` items using `λ × relevance − (1−λ) × max_similarity_to_selected`.

The `MmrLambda` (default 0.55) controls relevance vs. diversity. Setting it to 1.0 degenerates to plain top-K.

`SearchReferences(reference)` returns all entries whose `Source` contains the given string (substring match, case-insensitive).

`GetEntries(filter)` returns all stored entries with an optional predicate.

## Graph Store

**File:** `Graph.cs` — `Entity`, `Relationship`, `GraphStore`, `GraphStoreManager`

An experimental knowledge graph for Graph-RAG. Content chunks are processed by the LLM to extract:
- **Entities** — named things with type, attributes, and source reference
- **Relationships** — typed, directed edges between entities with a description

Entities have bidirectional adjacency lists (`OutgoingRelationships`, `IncomingRelationships`).

`GraphStore` supports:
- `AddEntity / AddRelationship`
- `GetEntity(name)` — returns entity or null
- `GetRelationships(entityName)` — returns outgoing relationships
- `BfsEntities(startName, hops)` — breadth-first traversal returning entities within N hops

`GraphStoreManager` wraps an LLM call to extract entities and relationships from a text chunk and store them. It also provides community detection (Girvan-Newman-style) and cluster reporting.

## ContextManager Embedding Cache

`ContextManager` maintains a SHA-256 keyed `ConcurrentDictionary<string, float[]>` so the same chunk text is never re-embedded across multiple ingest runs in the same session. Cache is cleared by `ContextManager.ClearCaches()` (called on restart).

Batch embedding follows the `MaxEmbeddingConcurrency` setting. Cache hits are reported via progress callbacks as `"cached"`, misses as `"embedded n/m"`.

## Configuration Reference

All RAG settings live in `Config.RagSettings`:

| Setting | Default | Description |
|---------|---------|-------------|
| `ChunkingStrategy` | `"SmartChunk"` | Chunker to use |
| `ChunkSize` | `100` | Lines (LineChunk) or chars (BlockChunk) per chunk |
| `Overlap` | `5` | Overlap size |
| `TopK` | `3` | Search result count |
| `UseEmbeddings` | `true` | If false, stores empty vectors (keyword search only) |
| `EmbeddingModel` | `"nomic-embed-text"` | Model for embeddings |
| `MaxTokensPerChunk` | `8000` | SmartChunk token budget |
| `MaxEmbeddingConcurrency` | `8` | Parallel embedding calls |
| `MaxIngestConcurrency` | `10` | Parallel ingest tasks |
| `MaxLineLength` | `1600` | Lines exceeding this are dropped by SmartChunk |
| `UseMmr` | `true` | Enable MMR diversity reranking |
| `MmrLambda` | `0.55` | MMR relevance weight |
| `MmrPoolMultiplier` | `4` | Pool size multiplier for MMR candidates |
| `TopKForParsing` | `2` | Context entries used in Planner sub-conversations |
