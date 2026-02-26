# Commands

Located in `/Commands/` (and partially in root `Commands.cs`). All interactive menu commands implement `Command` and are registered in the `CommandManager` tree.

## CommandManager Tree (default)

```
Menu
├── chat          (Chat/ChatCommands.cs)
│   ├── new           Create a new chat thread
│   ├── switch        Switch to an existing thread (menu)
│   └── delete        Delete a thread
├── rag           (Commands/RagCommands.cs + RagConfigCommands.cs)
│   ├── add file      Add a single file to the vector store
│   ├── add directory Add a directory recursively to the vector store
│   ├── add zip       Add a zip archive to the vector store
│   ├── search        Semantic search query against the vector store
│   ├── export        Export vector store contents
│   ├── clear         Clear the vector store
│   ├── Graph         Knowledge-graph sub-menu
│   │   ├── add file
│   │   ├── add directory
│   │   ├── search
│   │   ├── walk
│   │   └── community info
│   └── config        (RagConfigCommands.cs)
│       ├── chunker       Select chunking strategy
│       ├── file types    Manage RagFileType entries
│       └── settings      Adjust chunk size, overlap, topK, MMR, etc.
├── system        (Commands/SystemCommands.cs)
│   ├── logs          View structured log table
│   ├── config        View/edit current config JSON
│   └── provider      (Commands/ProviderCommands.cs)
│       ├── switch    Switch active provider
│       └── model     Change active model
├── tools         (Commands.cs — dynamic, built from ToolRegistry)
│   └── <tool-name>   Invoke any registered tool interactively
├── data          (UserManagedData/DataCommands.cs — auto-generated)
│   └── <type-name>   CRUD for each UserManaged type
│       ├── add
│       ├── edit
│       ├── delete
│       └── list
├── restart       Reset all state and re-initialize
├── help          Summarize the program via the summarize_text tool
└── exit          Save active thread and quit
```

Subsystems add their own command groups when enabled (see [subsystems/README.md](subsystems/README.md)).

## Commands.cs (root)

Defines `Command`, `CommandManager`, and `CreateDefaultCommands()`. Also contains:

- `CreateToolsCommands()` — builds the `tools` sub-menu dynamically from `ToolRegistry` at menu-open time so newly registered MCP tools appear without a restart.
- `GetInputFormatGuidance(tool)` — inspects `InputType` and `[ExampleText]` attributes to generate user-facing guidance shown in the tool input form.
- `ParseToolInput(tool, input)` — deserializes raw text input to the tool's `InputType` using `JSONParser`.

## ProviderCommands (`Commands/ProviderCommands.cs`)

| Command | Action |
|---------|--------|
| `switch provider` | Renders a menu of available providers; calls `Engine.SetProvider(name)` |
| `switch model` | Fetches models from the active provider; calls `Engine.SelectModelAsync()` |

## RagCommands (`Commands/RagCommands.cs`)

All RAG ingest and retrieval commands. Uses `UiForm` for path input and `AsyncProgress` for showing progress during batch ingest.

## RagConfigCommands (`Commands/RagConfigCommands.cs`)

Configuration commands for the RAG pipeline: chunker selection, `RagFileType` CRUD (via UserManagedData), and individual settings like `ChunkSize`, `TopK`, `UseMmr`.

## SystemCommands (`Commands/SystemCommands.cs`)

| Command | Action |
|---------|--------|
| `logs` | Calls `Log.GenerateTable()` to render the structured log buffer |
| `config` | Displays the current `config.json` in a realtime writer output |

## SubsystemCommands (`Commands/SubsystemCommands.cs`)

Auto-generates enable/disable toggle commands for every registered subsystem. Each command reads the current enabled state and flips it via `SubsystemManager.SetEnabled(name, !current)`.

## DataCommands (`UserManagedData/DataCommands.cs`)

Auto-discovers all types registered in `UserManagedData` and creates a CRUD command group for each. The `add` and `edit` commands use `UiForm` built from the type's `[UserField]` annotations. `[UserKey]` identifies the field used to find items for edit/delete.
