# cschat

A simple, interactive C# console chat client for [Ollama](https://ollama.com/) and Azure OpenAI LLM servers.

## Documentation & Configuration

- **Ollama**
  - [Ollama Documentation](https://github.com/ollama/ollama/blob/main/docs/README.md)
  - [Ollama Quickstart](https://ollama.com/docs/)
  - To configure for Ollama, set `Provider` to `Ollama` and `Host` to your Ollama server URL (default: `http://localhost:11434`) in `config.json`.
  - Recommend you download the nomic-embed-text:latest model if you intend to use RAG and at least one of llama3.2, deepseek-r1, or gemma3 models for general use.

- **Azure OpenAI**
  - [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
  - [Azure OpenAI Quickstart](https://learn.microsoft.com/en-us/azure/ai-services/openai/quickstart)
  - To configure for AzureAI, set `Provider` to `AzureAI`, `Host` to your Azure OpenAI endpoint, and `Model` to your deployed model name in `config.json`.
  - Make sure to run the following after making certain that you've got the correct permissions to access the deployment in question
    - winget install Microsoft.AzureCLI
    - az login

## Features
- **Multi-Provider Support:** Connects to Ollama or Azure OpenAI servers and interacts with LLM models via chat.
- **Rich Input Handling:** Supports multi-line input (Shift+Enter for new line) and command history.
- **Runtime Configuration:** Easily switch models, providers, and hosts at runtime without restarting.
- **Extensible Architecture:** Provider and command abstraction for easy extension with new capabilities.
- **Memory Management:** Maintains chat history in memory with cloning and context management capabilities.
- **Auto-Configuration:** Configuration is loaded from or saved to `config.json` (auto-generated).
- **Retrieval-Augmented Generation (RAG):** Ingest, search, and use external documents or knowledge in chat context. RAG data can be added, listed, and cleared via commands.
- **Interactive Menu System:** Press `Escape` to open a keyboard-navigable menu for commands, model/provider selection, and more. Supports filtering and quick selection.
- **Advanced Tool System:** Register and invoke custom tools via the `tools` command or through model-suggested tool use. Tools can be used to extend the assistant with new capabilities (e.g., calculations, file operations, lookups, etc.).
- **MCP subsystem support:** Easily add MCP server definitions to expand and extend tool functionality. Note: MCP is implemented as a subsystem and is managed via the Subsystem Manager.
- **Autonomous Planning:** Multi-step task execution with intelligent planning system that can break down complex goals into actionable steps.
- **Comprehensive Logging:** Built-in logging system with retry logic, error handling, and detailed execution tracking for debugging and monitoring.

## Tool System

The tool system allows you to register and invoke custom tools from within the chat client. Tools are modular functions that can be called directly by the user or automatically by the model when appropriate.

- **Listing tools:** Use the `tools` command in the menu to see available tools and their descriptions.
- **Invoking tools:** Select a tool from the menu to use a tool. You will be prompted for any required input.
- **Model tool use:** The model may suggest and invoke tools automatically if it determines a tool is relevant to your query. The result will be used to inform the model's response.
- **Implementing tools:** To add a new tool, implement the `ITool` interface and mark it with the IsConfigurable attribute. Each tool provides a name, description, usage string, and an async invocation method.

## Planning System

The planning system enables autonomous multi-step task execution by breaking down complex user goals into actionable steps. When a user's query requires multiple operations or data gathering, the planner automatically coordinates tool usage to achieve the desired outcome.

**How Planning Works:**
1. **Goal Assessment:** The system first determines if the user's request requires action or can be answered with existing knowledge.
2. **Step Generation:** If action is needed, the planner generates a series of steps using available tools to achieve the goal.
3. **Step Execution:** Each step is executed in sequence, with results feeding into subsequent steps.
4. **Duplicate Prevention:** The system tracks executed actions to avoid repeating the same tool with identical inputs.
5. **Error Handling:** Failed steps are logged and the planner adapts to continue with alternative approaches.
6. **Result Synthesis:** Once all steps are complete, the system synthesizes the results into a coherent response.

**Key Features:**
- **Intelligent Step Planning:** Uses AI to determine the optimal sequence of tool invocations
- **Context Management:** Maintains memory of previous steps to inform future decisions
- **Configurable Limits:** Maximum step count (default: 25) prevents infinite loops
- **Retry Logic:** Built-in retry mechanisms for handling transient failures
- **Visual Progress:** Real-time console output shows step execution with colored status indicators

## MCP Subsystem
MCP (Model Context Protocol) is a protocol designed to facilitate communication between clients and servers for managing and interacting with AI models. cschat exposes MCP as a subsystem that can be enabled/disabled via the Subsystem Manager, and configured via the UserManageData component (Data>McpServer menu).  It provides a standardized way to define, query, and execute operations on models hosted on MCP-compatible servers.

### How MCP Works with cschat
- **Server Integration:** cschat can connect to MCP servers defined in the `mcp_servers` directory. Each server is described using a JSON configuration file (e.g., `gitmcp.json`).
- **Dynamic Model Management:** MCP allows dynamic discovery and interaction with models hosted on the server.
- **Command Extensions:** MCP commands are integrated into the `provider` and `rag` command categories, enabling users to leverage MCP-specific features.

### Using MCP with cschat
1. **Configure MCP Servers:**
   - Add your MCP server configuration files to the `mcp_servers` directory. Each file should define the server's endpoint, available models, and other relevant details.
   - Example configuration (`mcp_servers/gitmcp.json`):
     ```json
     {
       "name": "GitMCP",
       "endpoint": "http://localhost:5000",
       "models": ["model1", "model2"]
     }
     ```

2. **Select MCP as Provider:**
   - Use the `provider select` command to choose MCP as the provider.
   - Example:
     ```
     provider select MCP
     ```

3. **List Available Models:**
   - Use the `provider model` command to list and select models available on the MCP server.
   - Example:
     ```
     provider model
     ```

4. **Interact with Models:**
   - Once a model is selected, you can interact with it just like any other provider in cschat.

5. **RAG with MCP:**
   - Use the `rag` commands to ingest, search, and clear external documents for use in chat context with MCP models.

### Benefits of MCP Integration
- **Standardized Protocol:** Simplifies interaction with AI models across different servers.
- **Extensibility:** Easily add new MCP servers and models without modifying the core application.
- **Enhanced Features:** Leverage advanced capabilities provided by MCP servers, such as custom model operations and metadata management.

### Getting started with MCP
As most MCP servers have a dependency on NPX (node.js), 
On Windows run: 
```
  winget install OpenJS.NodeJS
```

On MacOS run:
```
  brew install node
```

For more details on MCP, refer to [MCP for beginners](https://github.com/microsoft/mcp-for-beginners)

## Logging System

The logging system provides comprehensive tracking of application behavior with built-in retry logic and detailed error handling. It's designed for both debugging and monitoring system reliability.

**Core Features:**
- **Method-Level Logging:** Automatically captures method entry, execution time, parameters, and results
- **Retry Logic:** Configurable retry attempts with custom retry conditions for handling transient failures
- **Context Tracking:** Rich contextual data including source file, line number, method name, and execution flow
- **Structured Data:** Log entries include typed data fields for easy parsing and analysis
- **Exception Handling:** Detailed exception capture with stack traces and retry attempt tracking

**Retry Mechanism:**
```csharp
// Example: Retry up to 2 times on specific exceptions
var result = await Log.MethodAsync(
    retryCount: 2,
    shouldRetry: e => e is CsChatException cce && cce.ErrorCode == Error.EmptyResponse,
    func: async ctx => {
        // Your code here
        return await SomeOperation();
    });
```

## Subsystem Manager and ADO Subsystem

### Subsystem Manager
The Subsystem Manager is designed to manage and enable various subsystems dynamically. It provides a centralized way to register, enable, and interact with subsystems, ensuring modularity and scalability. Key features include:

- **Dynamic Registration**: Subsystems can be registered dynamically using a dictionary of subsystem types.
- **Enable/Disable Functionality**: Subsystems can be enabled or disabled based on configuration settings.
- **Service Provider Integration**: The manager integrates with the service provider to fetch subsystem instances.
- **Configuration Persistence**: Changes to subsystem states are saved to the configuration file.

### ADO Subsystem
The ADO (Azure DevOps) Subsystem integrates Azure DevOps functionality into `cschat`. It exposes interactive commands for browsing queries, selecting saved queries, summarizing and triaging work items, and producing manager-facing briefings and action plans.

Files of interest:
- `Subsytems/ADO/ADOInsights.cs` — scoring and briefing helpers
- `Subsytems/ADO/ADOCommands.cs` — interactive ADO commands exposed to the menu/CLI
- `Subsytems/ADO/ADOConfigCommands.cs` — runtime configuration commands for ADO (including insights weights)
- `Subsytems/UserManagedData/UserSelectedQuery.cs` — the user-managed saved-query model

ADOInsights (what it does)
- Purpose: analyze a set of `WorkItemSummary` entries and surface "attention" signals so you can focus triage on the most important items.
- Key types:
  - `AdoInsightsConfig` — holds tunable parameters and weights (fresh/soon windows, per-signal weights, and a list of critical tags).
  - `ScoredItem` — pairs a `WorkItemSummary` with a numeric `Score` and a `Factors` dictionary describing which signals contributed to the score.
- How scoring works (high level):
  1. For each work item, `AdoInsights.Analyze` accumulates a score by adding configured weights for detected signals:
     - "fresh": item created within `FreshDays` → add `W_Fresh`
     - "recentChange": changed within `FreshDays` → add `W_RecentChange`
     - "unassigned": no assignee or explicitly unassigned → add `W_Unassigned`
     - "priorityHigh": priority numeric value ≤ 2 (P1/P2) → add `W_PriorityHigh`
     - "dueSoon": due date within `SoonDays` → add `W_DueSoon`
     - "criticalTag": any tag that matches an entry in `CriticalTags` (case-insensitive contains) → add `W_CriticalTag`
  2. The result is a `ScoredItem` per work item; the list is sorted by descending score, then by due date, then by priority.
  3. `Analyze` also returns simple aggregates: counts by state, tag and area (useful for snapshots and the manager briefing prompt).
- Prompts: `ADOInsights` also provides helpers that build LLM prompts:
  - `MakeManagerBriefingPrompt(...)` — creates a concise prompt to ask the model for a 30-second manager briefing based on aggregates and sample titles.
  - `MakeActionPlanPrompt(...)` — formats the top scored items into a prompt asking for a triage-style action plan (assign, next step, likely resolution, preventative work).

Configuration (how to tune scoring)
- All of the numeric windows and weights live on `Program.config.Ado.Insights` (an `AdoInsightsConfig` instance) and are editable at runtime via the ADO config commands found under the top-level config command group.
- The available config commands (see `Subsytems/ADO/ADOConfigCommands.cs`) include:
  - `Insights -> fresh days` (sets `FreshDays`)
  - `Insights -> soon days` (sets `SoonDays`)
  - `Insights -> freshness weight` (`W_Fresh`)
  - `Insights -> recent change weight` (`W_RecentChange`)
  - `Insights -> unassigned weight` (`W_Unassigned`)
  - `Insights -> high priority weight` (`W_PriorityHigh`)
  - `Insights -> critical tag weight` (`W_CriticalTag`)
  - `Insights -> Due soon weight` (`W_DueSoon`)
  - `Insights -> Critical Tags -> list/add/remove` (manage the `CriticalTags` list)
- When you change a value via those commands, the code calls `Config.Save(...)` so changes persist to `config.json`.

ADOCommands (what they do and how they work)
- Location: `Subsytems/ADO/ADOCommands.cs`.
- Top-level command: `ADO` with two primary groups: `workitem` and `queries`.

- `ADO workitem` subcommands:
  - `lookup by ID` — prompts for a numeric work item ID, calls `AdoClient.GetWorkItemSummaryById(id)` and prints the result.
  - `summarize item` — lets you pick one of your saved queries (from `UserSelectedQuery` stored in the UserManagedData component), lists the query results in an aligned interactive menu, then runs a concise LLM summarization of the chosen work item via `Engine.Provider.PostChatAsync(...)` (falls back to raw detail text on failure).
  - `top` — pick a saved query, fetch its work items, run `AdoInsights.Analyze(...)` with the current `Program.config.Ado.Insights`, and print the top-N items ranked by score (score-only table). Useful quick triage list.
  - `summarize query` — compute aggregates for a saved query (counts by state/tag/area), print a numeric snapshot then ask the model for a 30-second briefing (via `MakeManagerBriefingPrompt`).
  - `triage` — end-to-end triage flow: pick a saved query, compute `Analyze`, print a manager briefing, list the top items (with scores), and request an LLM-generated action plan for the top subset.

- `ADO queries browse`:
  - Opens an interactive folder-style picker for your ADO project's queries (My/Shared Queries). You can navigate folders with the menu and when you pick a query the code will add that query to the user's saved queries collection using:
    - `Program.userManagedData.AddItem(new UserSelectedQuery(...))`
  - After adding the saved query the command calls `Config.Save(Program.config, Program.ConfigFilePath)` so your selection persists to `config.json`.

Interaction with UserSelectedQuery and the menu system
- `UserSelectedQuery` is a small user-managed type (see `Subsytems/UserManagedData/UserSelectedQuery.cs`) that stores a query's GUID, name, project and path. It's discoverable and editable via the `Data` component menu (the UserManagedData component exposes per-type `list/add/update/delete` commands automatically).
- Many ADO commands obtain the user's saved queries via:
  - `var saved = Program.userManagedData.GetItems<UserSelectedQuery>()`
  - They then build a human-friendly list of choices like `$"{q.Name} ({q.Project}) - {q.Path}"` and call `Program.ui.RenderMenu(header, choices)` to let the user pick one.
- The same `Program.ui.RenderMenu(...)` infra is used throughout ADO commands to:
  - Present saved queries to pick which query to act on
  - Present work-item rows (formatted via `ToMenuRows()`/aligned columns) so you can pick a row to summarize
  - Navigate query folders when browsing
-- The `Data` menu (UserManagedData component) is how you manage stored `UserSelectedQuery` entries directly (edit, delete, list). `ADO queries browse` is a convenience that discovers queries in ADO and automatically adds them to that collection.

Practical notes / examples
- To add a query to your saved queries: `ADO queries browse` → navigate to the desired query → it will be added to your `User Selected Query` collection and saved to `config.json`.
- To run a quick attention list: `ADO workitem top` → choose one of your saved queries → see the top-N scored items (scores come from `AdoInsights` and are influenced by the weights in config).
- To tune what "top" means, open the config and adjust weights: from the top-level menu run the ADO configuration commands (group shown as `Azure Dev Ops (ADO)` in the configuration command group) and modify the `Insights` settings — changes persist immediately.

Implementation pointers
- Scoring and prompt construction live entirely in `Subsytems/ADO/ADOInsights.cs`.
- Runtime editing of weights and tags lives in `Subsytems/ADO/ADOConfigCommands.cs` and persists via `Config.Save(...)`.
- The interactive commands that fetch work items and call `AdoClient` are in `Subsytems/ADO/ADOCommands.cs` and rely on the `Program.SubsystemManager` to obtain `AdoClient` and `UserManagedData` instances.

This expanded documentation should help you understand where the scoring lives, how the signals are combined, how to tune the behavior at runtime, and how the ADO commands interact with the saved-query menu integration via `UserSelectedQuery`.

### UserManagedData component

The `UserManagedData` component lets developers register small, typed user data collections (for example: saved queries, bookmarks, snippets, or other user-maintained records) that the app stores in `config.json` and exposes via an interactive `Data` command group.

Note that by-default this component should be enabled and that it has no other configurable settings.

Key points:

- Mark a class with `[UserManagedAttribute("Display Name", "Description")]` to make it discoverable by the component.
- Decorate properties with `[UserField(required: true/false)]` and mark a logical key with `[UserKey]` to enable update/delete operations.
- The component, through reflection, at runtime, discovers annotated types and creates per-type commands grouped under the top-level `Data` command with `list`, `add`, `update`, and `delete` actions as default actions.
- Stored data lives in `Program.config.UserManagedData.TypedData` as simple dictionaries (serialized JSON). This makes the feature lightweight and durable across runs.
- Example: `UserSelectedQuery` (used by the ADO subsystem) demonstrates a user-managed type that stores a query name, project, path, and GUID. Once defined, it appears in the `Data` menu and can be managed interactively.
- Extending: to add a new user-managed collection, create a class with the appropriate attributes and a parameterless constructor. The component will discover it after the next start (or when the component is enabled) and expose the interactive commands.

This component is intended for small, developer-maintained collections and not for large data stores or sensitive secrets.

## How it works
- On startup, loads or creates a configuration file (`config.json`) for provider, host, model, and system prompt.
- Initializes providers, command system, and in-memory chat history.
- **Menu system:** Press `Escape` at any prompt to open an interactive menu. Navigate with arrow keys, filter options by typing, and select with Enter. Menu allows access to commands, model/provider selection, and RAG actions.
- **RAG workflow:** Use commands or menu to add documents to the RAG store, search or clear RAG data, and have the model use retrieved context in responses.
- **Tool workflow:** When a user query matches a tool's purpose, the model may suggest invoking a tool. The tool is run, and its result is used to answer the user's question. Tools can also be invoked directly via the menu's `tools` command.
- For normal input, sends the chat history (and optionally RAG context) and user message to the selected provider and displays the response.
- Multi-line input is supported by pressing Shift+Enter for a soft new line.
- Chat history is kept in memory and can be cleared with the `clear` command in the menu.

## Building

1. **Requirements:**
   - [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) or later
   - An [Ollama](https://ollama.com/) or Azure OpenAI server running locally or remotely

2. **Clone the repository:**
   ```sh
   git clone https://github.com/wa-phil/cschat
   cd cschat
   ```

3. **Restore dependencies and build:**
   ```sh
   dotnet build
   ```

## Running

1. **Start your Ollama or Azure OpenAI server** (see their respective docs for details).
2. **Run the chat client:**
   ```sh
   dotnet run
   ```
   Or run the built binary from `bin/Debug/net9.0/cschat`.

3. **Usage:**
   - Type your message and press Enter to send.
   - Use Shift+Enter for a new line within your message.
   - Press ESC to see available commands, type to reduce options to select betwen.  Press a number for fast selection, or use cursor keys + enter to make a selection.  Press ESC to exit or go back to the previous menu.
   - Example commands:
```
commands
> [1] chat - chat-related commands     
  [2] provider - Provider-related commands 
  [3] rag - Retrieval-Augmented Generation commands 
  [4] tools - Tool commands
  [5] system - System commands
  [6] exit - Quit the application

tools commands
> [1] Calculator - Evaluates basic math expressions.
  [2] CurrentDateTime - Returns the current date and time.
|> 

provider commands                     
> [1] select - Select the LLM Provider 
  [2] model - List and select available models 
  [3] host - Change Ollama host      
  [4] system - Change system prompt   
  [5] temp - Set response temperature       
  [6] max-tokens - Set maximum tokens for response
|>  

system commands                        
> [1] log - Logging commands           
  [2] exit - Quit the application           
|>   
```

## Project Structure
- `Commands.cs` — Command and CommandManager abstractions
- `Config.cs` — Configuration class and file handling
- `config.json` — Configuration file (auto-generated)
- `Engine.cs` - Core chat engine
- `Json/` — Minimal JSON parser and writer
- `Log.cs` — Logging utilities
- `Context.cs` - Context implementations
- `Program.cs` — Main application logic, chat loop, and command handling
- `Providers/` — Provider implementations (Ollama, AzureAI)
- `Tools.cs` - ToolRegistry and various simple tools
- `User.cs` — User input and menu utilities

## Extending
To add a new command, add a new `Command` to the array passed to `CommandManager` in `Program.cs`.
To add a new provider, implement `IChatProvider` and decorate with `[ProviderName("YourProvider")]`.
To add a new tool, implement the `ITool` interface and and mark it with the IsConfigurable attribute to register it with the `ToolRegistry`. Tools should provide a name, description, usage, and an async invocation method.

---

## Chat threads and chat commands

CSChat now has managed chat threads and chat-related commands to make conversations forkable, persistent, and easy to navigate.

What changed
- New user-managed type `ChatThread` (`Chat/ChatThread.cs`) stores simple metadata: `Name`, `Description`, and `LastUsedUtc` for sorting.
- Chat management logic added in `Chat/ChatManager.cs` to create, save, load, delete, and switch chat threads. Threads are persisted to disk under a configurable root (see settings below).
- New top-level `chat` command and subcommands implemented in `Chat/ChatCommands.cs`:
  - `chat new` — fork the current conversation into a new thread (auto-names the thread or uses an LLM suggestion) and switch to a fresh conversation.
  - `chat switch` — pick an existing thread to switch to; the active thread is saved before switching.
  - `chat show` — display the current chat history in a readable format.
  - `chat clear` — clear the in-memory chat history and re-instantiate the system prompt.
  - `chat default new chat name` — set the default name used when creating new threads.

Usage examples
- Create a new thread based on the current conversation:
  - Run the menu and choose `chat -> new` or type `chat new`.
  - The conversation is saved as a new thread; a fresh thread becomes active.
- Switch to a previous thread:
  - Run `chat switch` and select a thread from the menu. The previous active thread is saved automatically.
- Inspect or clear history:
  - `chat show` prints the messages in the current active context.
  - `chat clear` empties the current context and re-applies the system prompt.

Design notes and behavior
- Thread storage: threads are stored under `Program.config.ChatThreadSettings.RootDirectory` (by default a `.threads` folder next to the app base directory). Each thread has a `chat.json` file holding its messages.
- Active thread tracking: `Program.config.ChatThreadSettings.ActiveThreadName` records the currently active thread and is persisted via `Config.Save(...)`.
- Naming: When forking, the system attempts to produce a meaningful name using `ChatManager.NameAndDescribeThread` which can call the model to suggest a name and optional description; `ChatManager.EnsureUniqueThreadName` avoids collisions by appending ` (n)` when needed.
- Lifecycle: Deleting a thread removes its on-disk files; if the deleted thread was active, a replacement default-named thread is created and switched to.
- Safe exits: on application exit the code will attempt to save the active thread before terminating.


**Note:** This project is a simple example and does not persist RAG data between runs. For advanced features, consider extending the codebase.

## History

### v0.5 (September 17, 2025)
- **Chat threads & commands:** Introduced managed chat threads (`ChatThread`) and a `ChatManager` to create, save, load, switch, and delete threads. Added the `chat` command group with `new`, `switch`, `show`, `clear`, and `default new chat name` subcommands to make conversations forkable and persistent.
- **Config & startup integration:** Added `ChatThreadSettings` to `Config` (root directory, default new-thread name, active thread tracking) and wired thread initialization into startup so the last active thread is loaded automatically.
- **Thread persistence & storage:** Each thread is persisted on disk (per-thread `chat.json`) under a configurable `.threads` root. Thread lifecycle handles safe deletion along with automatic fallback to a default thread when the active thread is removed.
- **LLM-assisted naming:** Implemented `ChatManager.NameAndDescribeThread` which can ask the model to propose a concise thread name and optional description; `EnsureUniqueThreadName` avoids naming collisions.
- **UX & safety:** Active thread is saved on switches and on exit; `chat switch` saves the current active thread before loading another. Exit behavior saves the active thread where possible. `chat clear` preserves the system prompt after clearing history.
- **Commands refactor & minor improvements:** Organization and various command behaviors were adjusted (exit now attempts to save active thread, system commands gained chat/thread-related settings). Several small attribute and naming normalizations across user-managed types and config were applied.

### v.04 (July 28, 2025)
#### MCP Support and code refactoring
This version introduces a rather substantial refactoring to pave the way to add support for the Model Context Protocol (MCP), enabling seamless integration with MCP servers for enhanced functionality and extensibility.

### v0.3 (July 12, 2025)
- **Enhanced Planning & Reliability:** Major improvements to planning system reliability with better error handling and retry logic
- **Expanded Token Limits:** Increased default max token size to 32,000 for larger responses
- **Improved Tool Support:** Added support for tool messages in chat history and enhanced tool invocation reliability
- **Better Logging:** Updated time format to include milliseconds for better diagnosability
- **System Hardening:** Substantial improvements to PostChat & JSON parsing reliability
- **Bug Fixes:** Fixed issues in provider commands' max tokens command and general stability improvements

### v0.2 (July 12, 2025)
- **Advanced Planning System:** Introduced PlanStep and Planner classes for autonomous multi-step task execution
- **Enhanced File Tools:** Added ListFilesMatchingTool for regex-based file searching and improved FileMetadataTool
- **New Restart Command:** Added restart command to reset chat history, RAG state, logs, and console
- **Improved Tool Integration:** Enhanced tool role handling and error reporting in tool invocation
- **Memory Management:** Introduced memory cloning capabilities and improved context management
- **Better User Experience:** Enhanced console output with colored step summaries and completion messages
- **Command Structure:** Refactored commands into separate files (chat, provider, RAG, system) for better organization
- **Azure OpenAI Improvements:** Added embedding client initialization and retry logic for timeouts

### v0.1 and Earlier (July 2025)
- Added Azure OpenAI provider support alongside Ollama.
- Unified configuration file (`config.json`) for provider, host, model, and prompt.
- Improved provider abstraction and dynamic provider switching at runtime.
- Enhanced command system and tab-completion.
- Refactored project structure for extensibility and maintainability.
- Added minimal JSON parser/writer for config and API payloads.
- Improved error handling and logging.
- **Added Retrieval-Augmented Generation (RAG) support:** Users can ingest, search, and clear external documents for use in chat context.
- **Major menu system upgrade:** Interactive, keyboard-navigable menu (invoked with Escape) for commands, model/provider selection, and RAG actions. Supports filtering and quick selection.
- Improved user input handling and extensibility for new commands and providers.
- **Introduced tools:** Register and invoke custom tools via `tools` command or model-suggested tool use. Extend assistant capabilities with new tools.
