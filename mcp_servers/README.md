# MCP Server Integration

This directory contains MCP server definition files. Each `.json` file represents a configured MCP server with a friendly name.

## Example MCP Server Configuration File

Create a JSON file with your MCP server configuration:

```json
{
  "name": "filesystem",
  "description": "File system operations MCP server",
  "command": "npx",
  "args": ["@modelcontextprotocol/server-filesystem", "/tmp"],
  "environment": {},
  "workingDirectory": "",
  "enabled": true
}
```

## Usage

Use the `mcp` commands in the application to manage servers:

- `mcp add` - Add a new server by providing a path to a JSON configuration file and a friendly name
- `mcp list` - Show configured servers with their friendly names and status
- `mcp remove` - Remove a server using a menu selection
- `mcp reload` - Reload and reconnect to all configured servers  
- `mcp tools` - Show tools from connected servers

## Workflow

1. Create a JSON configuration file for your MCP server
2. Use `mcp add` and provide the path to your configuration file
3. Give it a friendly name when prompted
4. The server will be automatically connected and tools registered
5. Use `mcp tools` to see available tools
6. MCP tools will automatically appear in the main `tools` menu

## Example Configuration Files

### Filesystem Server
```json
{
  "name": "filesystem",
  "description": "File system operations",
  "command": "npx",
  "args": ["@modelcontextprotocol/server-filesystem", "/path/to/workspace"],
  "environment": {},
  "workingDirectory": ""
}
```

### Git Server
```json
{
  "name": "git",
  "description": "Git repository operations",
  "command": "npx",
  "args": ["@modelcontextprotocol/server-git"],
  "environment": {},
  "workingDirectory": "/path/to/repo"
}
```
