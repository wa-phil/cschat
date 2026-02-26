# Architecture

## Layered Diagram

```
┌──────────────────────────────────────────────────────────────┐
│  Entry Point: Program.cs                                      │
│  • CLI flag parsing (Mono.Options)                            │
│  • DI container setup (Microsoft.Extensions.DependencyInjection)│
│  • Selects UI mode (Terminal or PhotinoUi)                    │
│  • Runs main chat loop via ui.RunAsync()                      │
└───────────────────────────┬──────────────────────────────────┘
                            │
        ┌───────────────────▼──────────────────────┐
        │           UX Layer  (IUi)                 │
        │  Terminal.cs  ──  PhotinoUi.cs            │
        │  Progress.cs, Table.cs, Report.cs, etc.   │
        └───────────────────┬──────────────────────┘
                            │
        ┌───────────────────▼──────────────────────┐
        │       Command Layer (CommandManager)      │
        │  RagCommands  SystemCommands  ChatCmds    │
        │  ProviderCommands  SubsystemCommands      │
        │  DataCommands  (UserManagedData)          │
        └───────────────────┬──────────────────────┘
                            │
        ┌───────────────────▼──────────────────────┐
        │         Engine.cs  (orchestrator)         │
        │  • SetProvider / SetTextChunker           │
        │  • AddFile/Dir/Zip → VectorStore          │
        │  • PostChatAsync → ContextManager → Planner│
        └────────┬──────────┬──────────────────────┘
                 │          │
    ┌────────────▼──┐  ┌────▼──────────────────────┐
    │  Providers/   │  │   RAG Pipeline             │
    │  Ollama.cs    │  │  TextChunkers.cs           │
    │  AzureAI.cs   │  │  Memory/VectorStore.cs     │
    │  (IChatProvider│  │  Graph.cs (GraphStore)    │
    │   IEmbedding) │  │  ContextManager.cs         │
    └───────────────┘  └───────────────────────────┘
                 │
    ┌────────────▼──────────────────────────────────┐
    │   Subsystems (ISubsystem, SubsystemManager)   │
    │   ADO  Kusto  MCP  Mail  PRs  S360            │
    └───────────────────────────────────────────────┘
```

## Key Patterns

### Dependency Injection

`Program.Startup()` uses `Microsoft.Extensions.DependencyInjection` to register all providers, chunkers, tools, and subsystems discovered via reflection. Types marked with `[IsConfigurable("name")]` are auto-registered under their logical name. Subsystems that expose a static `Instance` property are registered as singletons using that property.

```csharp
// Discovery and registration happens in DictionaryOfTypesToNamesForInterface<T>()
Providers = DictionaryOfTypesToNamesForInterface<IChatProvider>(serviceCollection, types);
Tools     = DictionaryOfTypesToNamesForInterface<ITool>(serviceCollection, types);
```

### Command Pattern

All interactive menu items implement `Command`. Commands form a tree rooted at `CommandManager`. Each `Command` has:
- `Name` — menu label
- `Description` — delegate so text can be dynamic
- `Action` — async task invoked when the item is selected
- `SubCommands` — nested child commands

The user presses **ESC** in Terminal mode to open the command menu. The menu supports arrow-key navigation, number shortcuts, and incremental text filtering.

### Subsystem Plugin Architecture

`ISubsystem` implementations are discovered via reflection on `[IsConfigurable]`. `SubsystemManager`:
- Registers all discovered subsystems at startup
- Calls `SetEnabled(name, bool)` to connect/disconnect them based on `config.Subsystems`
- Respects `[DependsOn("OtherSubsystem")]` attributes (auto-enables dependencies, cascades disables)
- Persists enabled state to `config.json`

### UserManagedData

Classes decorated with `[UserManaged("name", "desc")]` and `[UserField]` / `[UserKey]` properties are automatically:
- Discovered and registered via reflection
- Stored in `config.json` under `UserManagedData.TypedData[TypeName]`
- Exposed through CRUD commands under the **data** menu
- Notifiable via pub/sub: `userManagedData.Subscribe<T>(handler)`

### Logging Pattern

The `Log` class provides structured, context-aware logging with optional retry support:

```csharp
await Log.MethodAsync(async ctx => {
    ctx.OnlyEmitOnFailure();   // suppress verbose-level on success
    // ... work ...
    ctx.Succeeded();
});
```

Key methods: `Log.Method(...)`, `Log.MethodAsync(...)` — both accept `retryCount` and `shouldRetry` predicates. Each invocation creates a `Log.Context` that emits to an in-memory buffer on dispose. The buffer can be rendered as a structured table via `Log.GenerateTable()`.

## Main Data Flows

### Chat Flow

```
User types message
  → Context.AddUserMessage()
  → Engine.PostChatAsync(context)
      → ContextManager.InvokeAsync()      // RAG lookup
          → SearchVectorDB(userMessage)   // embed query, cosine search
          → Context.AddContext(chunks)    // inject retrieved chunks
      → Planner.PostChatAsync(context)
          → GetObjective()                // LLM: does this need tool use?
          → if TakeAction:
              → Steps() loop
                  → GetToolSelection()   // LLM picks tool
                  → GetToolInput()       // LLM generates typed input
                  → ToolRegistry.InvokeInternalAsync()
                  → GetPlanProgress()    // LLM: goal achieved?
          → Provider.PostChatAsync()     // final LLM call
  → Context.AddAssistantMessage(response)
  → ui.RenderChatMessage(response)
```

### RAG Ingestion Flow

```
Engine.AddFileToVectorStore(path)
  → ReadFilesFromDirectory() / ReadFilesFromZip()
      → ShouldIncludeFile() using RagFileType entries
  → ContextManager.AddContent(content, reference)
      → TextChunker.ChunkText()          // SmartChunk / LineChunk / BlockChunk
      → IEmbeddingProvider.GetEmbeddingsAsync()  // batched, with cache
      → VectorStore.Add(entries)         // normalized cosine vectors
```

### Form / UiForm Protocol

Commands collect input by building a `UiForm` with typed `IUiField` instances and calling `Program.ui.ShowFormAsync(form)`. The UiForm model is cloned at creation so the original is not mutated until the user confirms. Terminal renders field-by-field with text prompts; Photino serializes the form definition to JSON and sends it to the web frontend via the `ShowForm` message type.
