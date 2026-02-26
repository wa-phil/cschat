# Core

Root-level source files that form the backbone of CSChat.

## Program.cs

Entry point. Responsibilities:

1. **`Startup()`** — loads `config.json`, initializes logging, creates `SubsystemManager`, runs DI discovery for providers / chunkers / tools / subsystems, creates `UserManagedData`.
2. **`InitProgramAsync()`** — creates `Context`, initializes `ToolRegistry`, runs RagFileType migration (legacy `SupportedFileTypes` → UserManaged entries), connects subsystems, initializes `ChatManager`, loads or creates the active chat thread, injects tool descriptions into the system context.
3. **`Main()`** — parses CLI flags (`-h`, `-m`, `-s`, `-p`, `-e`, `-u`), selects `Terminal` or `PhotinoUi`, runs `ui.RunAsync()` with the main chat loop.

Key global statics on `Program`:
- `config` — the loaded `Config` instance
- `Context` — current conversation state
- `commandManager` — root of the command tree
- `serviceProvider` — the DI container
- `SubsystemManager` — subsystem lifecycle manager
- `userManagedData` — attribute-driven CRUD store
- `ui` — current `IUi` implementation

## Engine.cs

Static orchestrator class. Key methods:

| Method | Purpose |
|--------|---------|
| `SetProvider(name)` | Resolves an `IChatProvider` by name from the DI container |
| `SetTextChunker(name)` | Resolves an `ITextChunker` by name |
| `AddFileToVectorStore(path)` | Reads a single file and ingests it into the vector store |
| `AddDirectoryToVectorStore(path)` | Recursively reads a directory, filters by `RagFileType`, ingests |
| `AddZipFileToVectorStore(zipPath)` | Reads files from a zip archive and ingests |
| `AddFileToGraphStore(path)` | Ingests a single file into the graph store |
| `AddDirectoryToGraphStore(path)` | Ingests a directory into the graph store |
| `PostChatAsync(context)` | Runs `ContextManager.InvokeAsync()` then `Planner.PostChatAsync()` |
| `SelectModelAsync()` | Fetches available models from the provider, renders a menu |
| `RefreshSupportedFileTypesFromUserManaged()` | Syncs `SupportedFileTypes` from enabled `RagFileType` entries |
| `BuildCommandTreeArt(commands, ...)` | Renders an ASCII command-tree representation |

Engine holds global singletons: `VectorStore`, `Provider`, `TextChunker`, `Planner`.

## Context.cs

### Context

Immutable-like container for a single conversation. Fields:
- `_systemMessage` — the system prompt `ChatMessage` (with appended RAG context at render time)
- `_messages` — ordered list of user / assistant / tool messages
- `_context` — list of `(Reference, Chunk)` pairs injected into the system message

Key methods:
- `GetSystemMessage()` — returns the system message with all RAG context chunks appended. If `IncludeCitationRule` is true, appends a citation instruction.
- `AddUserMessage / AddAssistantMessage / AddToolMessage`
- `AddContext / ClearContext` — manage RAG chunks for the current turn
- `Clone()` — deep copy (used by Planner for ephemeral sub-conversations)
- `Save(path) / Load(path)` — JSON serialization via the custom JSONParser

### ContextManager

Static class that handles the bridge between raw content and the vector store. Key methods:

| Method | Purpose |
|--------|---------|
| `AddContent(content, reference, ...)` | Chunks, embeds (with SHA-256 cache), and stores in `VectorStore` |
| `AddGraphContent(content, reference)` | Chunks and routes to `GraphStoreManager.ExtractAndStoreAsync()` |
| `InvokeAsync(input, context)` | Searches the vector store for the user message, populates `Context` |
| `SearchVectorDB(userMessage)` | Embeds the query, searches, filters below-average scores |
| `Flatten(entries)` | Merges overlapping line-ranged chunks from the same source |

The embedding cache is a `ConcurrentDictionary<string, float[]>` keyed by SHA-256 hash of the chunk text. `ClearCaches()` resets it (used on restart).

## Config.cs

`Config` is the top-level settings model. It is loaded from and saved to `config.json` next to the executable.

Key settings:

| Field | Default | Description |
|-------|---------|-------------|
| `UiMode` | `Terminal` | `Terminal` or `Gui` |
| `Provider` | `"Ollama"` | Active provider name |
| `Model` | `""` | Active model name |
| `Host` | `"http://localhost:11434"` | LLM server URL |
| `MaxTokens` | `32000` | Max tokens per response |
| `Temperature` | `0.7` | Sampling temperature |
| `SystemPrompt` | (default prompt) | System prompt text |
| `MaxSteps` | `25` | Max planning steps per turn |
| `RagSettings` | see below | All RAG-related settings |
| `ChatThreadSettings` | see below | Thread storage settings |
| `Subsystems` | dict | Per-subsystem enabled flags |
| `UserManagedData` | `UserManagedDataConfig` | Persisted user-managed items |

### RagSettings

| Field | Default | Description |
|-------|---------|-------------|
| `ChunkingStrategy` | `"SmartChunk"` | Chunker name |
| `ChunkSize` | `100` | Lines per chunk (LineChunk / BlockChunk) |
| `Overlap` | `5` | Overlap lines/chars |
| `TopK` | `3` | Number of results to retrieve |
| `UseEmbeddings` | `true` | Enable/disable embedding |
| `EmbeddingModel` | `"nomic-embed-text"` | Embedding model name |
| `MaxTokensPerChunk` | `8000` | Token limit per SmartChunk chunk |
| `MaxEmbeddingConcurrency` | `8` | Parallel embedding requests |
| `UseMmr` | `true` | Enable Maximal Marginal Relevance |
| `MmrLambda` | `0.55` | Relevance vs. diversity trade-off |

## Commands.cs

### Command

A node in the command tree:
- `Name` — display label
- `Description` — `Func<string>` (dynamic text)
- `Action` — `Func<Task<Command.Result>>` (async)
- `SubCommands` — children; setting this also sets `Parent` back-pointers
- `GetFullPath()` — returns `"parent>child>..."` breadcrumb string

Default action: shows a filtered scrollable menu of sub-commands.

### CommandManager

Extends `Command`. Created via `CreateDefaultCommands()` which assembles:
- `chat` commands (`/Chat/ChatCommands.cs`)
- `rag` commands (`/Commands/RagCommands.cs`, `RagConfigCommands.cs`)
- `system` commands (`/Commands/SystemCommands.cs`)
- `restart` — resets all state and re-initializes
- `help` — calls `summarize_text` tool on the command tree
- `exit` — saves active thread and exits

Sub-command groups added dynamically by subsystems (ADO, Kusto, MCP, Mail, PRs, S360) when their `ISubsystem.IsEnabled` is set to `true`.

## Interfaces.cs

Defines all core interfaces and attributes:

| Symbol | Kind | Description |
|--------|------|-------------|
| `IChatProvider` | interface | `GetAvailableModelsAsync()`, `PostChatAsync(context, temp)` |
| `IEmbeddingProvider` | interface | `GetEmbeddingAsync(text)`, `GetEmbeddingsAsync(texts, ct)` |
| `IVectorStore` | interface | `Add`, `Clear`, `Search`, `SearchReferences`, `GetEntries` |
| `ITextChunker` | interface | `ChunkText(path, text) → List<(Reference, Content)>` |
| `ITool` | interface | `Description`, `Usage`, `InputType`, `InvokeAsync(input, ctx)` |
| `ISubsystem` | interface | `IsAvailable`, `IsEnabled` (get/set) |
| `IUi` | interface | Full UI contract — see [ux.md](ux.md) |
| `IUiField` | interface | Form field contract |
| `[IsConfigurable(name)]` | attribute | Marks a type for DI auto-registration |
| `[UserManaged(name, desc)]` | attribute | Marks a type for UserManagedData |
| `[UserField(...)]` | attribute | Marks a property as a form field |
| `[UserKey]` | attribute | Marks the identity property for updates |
| `[DependsOn(name)]` | attribute | Declares a subsystem dependency |
| `[ExampleText(text)]` | attribute | Provides a JSON example for TypeParser |
| `Roles` enum | enum | `System`, `User`, `Assistant`, `Tool`, `Progress` |
| `ChatMessage` | class | `Id`, `Role`, `Content`, `CreatedAt`, `State`, `Meta` |
| `Reference` | record | `Source`, `Start?`, `End?` — identifies a content chunk |
| `SearchResult` | record | `Score`, `Reference`, `Content` |
| `ToolResult` | record | `Succeeded`, `Response`, `context`, `Error?` |

## Log.cs

Structured logging with context-aware metadata. All log calls use the `Log.Method` / `Log.MethodAsync` pattern:

```csharp
await Log.MethodAsync(async ctx => {
    ctx.OnlyEmitOnFailure();         // only emit if something goes wrong
    ctx.Append(Log.Data.Path, path); // attach typed key-value pairs
    // ... work ...
    ctx.Succeeded();
});
```

- `ctx.Succeeded(bool)` — marks success and sets timestamp
- `ctx.Failed(message, errorCode)` — marks failure; elevates level to Error
- `ctx.Warn(message)` — marks as warning
- `ctx.OnlyEmitOnFailure()` — sets level to Verbose so the entry is suppressed on success

Log entries are buffered in-memory (`Log._buffer`). `Log.GenerateTable()` renders them as a structured table. `Log.ClearOutput()` clears the buffer.

`EventLogListener` bridges `System.Diagnostics.Tracing.EventSource` events (Azure SDK, .NET networking) into the same log buffer. Which event sources to capture is controlled by `Config.EventSources`.

### Log.Data enum

Typed keys for structured fields: `Method`, `Level`, `Timestamp`, `Success`, `Path`, `Provider`, `Model`, `ToolName`, `Goal`, `Step`, `Reference`, and many more.
