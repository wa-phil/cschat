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

**Note:** This project is a simple example and does not persist chat history or RAG data between runs. For advanced features, consider extending the codebase.

## History

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
