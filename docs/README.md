# CSChat Documentation

CSChat is an interactive C# console/GUI chat client for LLM servers (Ollama and Azure OpenAI). It supports RAG, autonomous planning, a tool system, MCP (Model Context Protocol), and optional subsystems for Azure DevOps, Kusto, and email integration.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                     UX Layer                            │
│         Terminal (console)  │  Photino (web GUI)        │
└─────────────────────┬───────────────────────────────────┘
                      │  IUi
┌─────────────────────▼───────────────────────────────────┐
│                  Command Layer                           │
│    CommandManager → RagCommands, SystemCommands, ...    │
└─────────────────────┬───────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────┐
│               Chat Engine (Engine.cs)                   │
│         Context management · RAG augmentation           │
│         Planner (agentic tool loop)                     │
└────────┬────────────┬────────────────────────┬──────────┘
         │            │                        │
┌────────▼───┐  ┌─────▼──────┐  ┌─────────────▼────────┐
│  Providers │  │  RAG Store  │  │     Subsystems        │
│  Ollama    │  │ VectorStore │  │ ADO · Kusto · MCP     │
│  AzureAI   │  │ GraphStore  │  │ Mail · PRs · S360     │
└────────────┘  └────────────┘  └──────────────────────┘
```

## Documentation Index

| File | Description |
|------|-------------|
| [architecture.md](architecture.md) | Layered architecture, key patterns, and main data flows |
| [core.md](core.md) | Root-level files: Program, Engine, Context, Config, Commands, Log |
| [providers.md](providers.md) | IChatProvider/IEmbeddingProvider, Ollama, AzureAI |
| [rag.md](rag.md) | Full RAG pipeline: chunkers, vector store, graph store, file types |
| [tools.md](tools.md) | ITool interface, ToolRegistry, built-in tools, Planner |
| [ux.md](ux.md) | IUi abstraction, Terminal, Photino, Progress, UiForm |
| [chat.md](chat.md) | Chat thread management: ChatThread, ChatManager, ChatCommands |
| [commands.md](commands.md) | CommandManager tree, all command group files |
| [json.md](json.md) | Custom JSON parser/writer (no external dependency) |
| [user-managed-data.md](user-managed-data.md) | Attribute-driven CRUD persistence, TypeParser |
| [subsystems/README.md](subsystems/README.md) | ISubsystem, SubsystemManager, enable/disable, adding a new subsystem |
| [subsystems/ado.md](subsystems/ado.md) | Azure DevOps integration |
| [subsystems/kusto.md](subsystems/kusto.md) | Kusto / Azure Data Explorer integration |
| [subsystems/mcp.md](subsystems/mcp.md) | Model Context Protocol server management |
| [subsystems/mail.md](subsystems/mail.md) | MAPI / Outlook email integration |
| [subsystems/prs.md](subsystems/prs.md) | Pull Request analytics |
| [subsystems/s360.md](subsystems/s360.md) | Service 360 health integration |

## Quick Start

```bash
# Build
dotnet build

# Run with Ollama (default)
dotnet run

# Run with Azure OpenAI
dotnet run -- -p azureai -u gui

# Show all flags
dotnet run -- --help
```

**Command-line flags:**

| Flag | Description |
|------|-------------|
| `-h` / `--host` | LLM server host (default: `http://localhost:11434`) |
| `-m` / `--model` | Model name |
| `-s` / `--system` | System prompt |
| `-p` / `--provider` | Provider: `ollama` or `azureai` |
| `-e` / `--embedding_model` | Embedding model (default: `nomic-embed-text`) |
| `-u` / `--ui` | UI mode: `terminal` or `gui` |
