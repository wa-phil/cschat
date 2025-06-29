# cschat

A simple, interactive C# console chat client for [Ollama](https://ollama.com/) LLM server.

## Features
- Connects to an Ollama server and interacts with LLM models via chat.
- Supports multi-line input (Shift+Enter for new line).
- Tab-completion for commands (type `/` and press Tab).
- Easily switch models and hosts at runtime.
- Command abstraction for easy extension.
- Maintains chat history in memory.

## How it works
- On startup, loads or creates a configuration file (`ollama_config.json`) for host, model, and system prompt.
- Prompts the user for input. If the input starts with `/`, it is treated as a command (e.g., `/model`, `/host`, `/clear`, `/history`, `/exit`).
- Commands are managed via a `CommandManager` abstraction, making it easy to add or modify commands.
- For normal input, sends the chat history and user message to the Ollama server and displays the response.
- Chat history is kept in memory and can be cleared with `/clear`.
- Multi-line input is supported by pressing Shift+Enter for a soft new line.
- Tab-completion is available for commands: type `/` and press Tab to complete or see available commands.

## Building

1. **Requirements:**
   - [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) or later
   - An [Ollama](https://ollama.com/) server running locally or remotely

2. **Clone the repository:**
   ```sh
   git clone <your-repo-url>
   cd cschat
   ```

3. **Restore dependencies and build:**
   ```sh
   dotnet build
   ```

## Running

1. **Start your Ollama server** (see [Ollama docs](https://ollama.com/) for details).
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
     - `/host` — Change Ollama host
     - `/clear` — Clear chat history
     - `/history` — Show chat history
     - `/exit` — Quit the application
     - `/?` or `/help` — Show help

## Project Structure
- `Program.cs` — Main application logic, chat loop, and command handling
- `Commands.cs` — Command and CommandManager abstractions
- `ollama_config.json` — Configuration file (auto-generated)

## Extending
To add a new command, add a new `Command` to the array passed to `CommandManager` in `Program.cs`.

---

**Note:** This project is a simple example and does not persist chat history between runs. For advanced features, consider extending the codebase.
