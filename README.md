# cschat

A simple, interactive C# console chat client for [Ollama](https://ollama.com/) and Azure OpenAI LLM servers.

## Documentation & Configuration

- **Ollama**
  - [Ollama Documentation](https://github.com/ollama/ollama/blob/main/docs/README.md)
  - [Ollama Quickstart](https://ollama.com/docs/)
  - To configure for Ollama, set `Provider` to `Ollama` and `Host` to your Ollama server URL (default: `http://localhost:11434`) in `config.json`.

- **Azure OpenAI**
  - [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
  - [Azure OpenAI Quickstart](https://learn.microsoft.com/en-us/azure/ai-services/openai/quickstart)
  - To configure for AzureAI, set `Provider` to `AzureAI`, `Host` to your Azure OpenAI endpoint, and `Model` to your deployed model name in `config.json`.

## Features
- Connects to Ollama or Azure OpenAI servers and interacts with LLM models via chat.
- Supports multi-line input (Shift+Enter for new line).
- Easily switch models, providers, and hosts at runtime.
- Extensible provider and command abstraction for easy extension.
- Maintains chat history in memory.
- Configuration is loaded from or saved to `config.json` (auto-generated).
- **Retrieval-Augmented Generation (RAG):** Ingest, search, and use external documents or knowledge in chat context. RAG data can be added, listed, and cleared via commands.
- **Interactive menu system:** Press `Escape` to open a keyboard-navigable menu for commands, model/provider selection, and more. Supports filtering and quick selection.

## How it works
- On startup, loads or creates a configuration file (`config.json`) for provider, host, model, and system prompt.
- Initializes providers, command system, and in-memory chat history.
- **Menu system:** Press `Escape` at any prompt to open an interactive menu. Navigate with arrow keys, filter options by typing, and select with Enter. Menu allows access to commands, model/provider selection, and RAG actions.
- **RAG workflow:** Use commands or menu to add documents to the RAG store, search or clear RAG data, and have the model use retrieved context in responses.
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
   - Type `/` to see available commands, and use Tab for completion.
   - Example commands:
     - `/model` — List and select available models
     - `/host` — Change server host
     - `/provider` — Switch between Ollama and AzureAI
     - `/clear` — Clear chat history
     - `/history` — Show chat history
     - `/exit` — Quit the application
     - `/?` or `/help` — Show help

## Project Structure
- `Program.cs` — Main application logic, chat loop, and command handling
- `Commands.cs` — Command and CommandManager abstractions
- `Providers/` — Provider implementations (Ollama, AzureAI)
- `Json/` — Minimal JSON parser and writer
- `Config.cs` — Configuration class and file handling
- `Engine.cs` - Core chat engine
- `Memory.cs` - Memory implementations
- `Log.cs` — Logging utilities
- `User.cs` — User input and menu utilities
- `config.json` — Configuration file (auto-generated)

## Extending
To add a new command, add a new `Command` to the array passed to `CommandManager` in `Program.cs`.
To add a new provider, implement `IChatProvider` and decorate with `[ProviderName("YourProvider")]`.

---

**Note:** This project is a simple example and does not persist chat history or RAG data between runs. For advanced features, consider extending the codebase.

## History

### July 2025
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
