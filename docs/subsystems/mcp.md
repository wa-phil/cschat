# MCP Subsystem

**Registration name:** `"Mcp"`
**Default enabled:** `true`
**Files:** `Subsytems/Mcp/`

## Overview

Implements the [Model Context Protocol](https://modelcontextprotocol.io) client side. Connects to external MCP servers running as child processes via stdio, discovers their tools, and registers them in `ToolRegistry` so the Planner and the user can invoke them.

## McpServerDefinition (UserManaged)

**File:** `Subsytems/Mcp/McpServerDefinition.cs`

`[UserManaged("McpServers", "MCP server definition")]`

| Field | Description |
|-------|-------------|
| `Name` | Unique identifier for the server |
| `Description` | Human-readable description |
| `Command` | Executable to launch (e.g. `node`, `python`, `npx`) |
| `Args` | Command-line arguments (list of strings) |
| `Environment` | Environment variable overrides |
| `WorkingDirectory` | Working directory for the server process |

Servers are managed via **data > McpServers** or the **mcp** command group.

## McpManager

**File:** `Subsytems/Mcp/McpManager.cs`

`[IsConfigurable("Mcp")]` — uses a static `Instance` property (`_instance ??= new McpManager()`) to act as a singleton. Implements `ISubsystem`.

On enable:
1. Loads all `McpServerDefinition` entries from `UserManagedData`.
2. Connects to each server via `McpClient.CreateAsync()`.
3. Lists each server's tools and registers them in `ToolRegistry`.
4. Subscribes to `UserManagedData` changes for `McpServerDefinition` (add/update/delete → auto-reconnect or disconnect).
5. Adds the `mcp` command group to the command manager.

On disable:
1. Calls `ShutdownAllAsync()` to terminate all server processes.
2. Unregisters MCP tools from `ToolRegistry`.
3. Removes the `mcp` command group.

### Tool Registration

Each MCP tool is wrapped in `McpTool` (which implements `ITool`) and registered under the tool's name. When the server disconnects, its tools are unregistered.

## McpClient

**File:** `Subsytems/Mcp/McpClient.cs`

Wraps the `ModelContextProtocol.Client.IMcpClient` from the MCP SDK. Communicates with the server process via `StdioClientTransport`. Key methods:

| Method | Description |
|--------|-------------|
| `CreateAsync(serverDef)` | Launches the server process and establishes the MCP connection |
| `ListToolsAsync()` | Returns the list of tools the server exposes |
| `CallToolAsync(name, args)` | Invokes a tool on the server and returns the result content |
| `Dispose()` | Terminates the server process |

## McpTool

**File:** `Subsytems/Mcp/McpTool.cs`

Adapts an MCP tool to the `ITool` interface. `InvokeAsync` serializes the input as a JSON arguments dictionary and calls `McpClient.CallToolAsync`. The result content (text parts) is concatenated and returned as the tool response.

## Commands

**File:** `Subsytems/Mcp/McpCommands.cs`

Registers under the `mcp` command group:

| Command | Description |
|---------|-------------|
| `MCP > reload` | Reload and reconnect to all configured MCP servers |
| `MCP > create documentation` | Write `mcp_documentation.md` with schemas and example inputs for all connected server tools |
| `MCP > list tools` | List tools from connected MCP servers with usage, description, and example input |

## Dependencies

None. MCP has no `[DependsOn]` attributes.
