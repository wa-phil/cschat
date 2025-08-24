using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Subsytems.Mcp
{
    public partial class CommandManager : Command
    {
        public static Command CreateMcpSubsystemCommands()
        {
            return new Command
            {
                Name = "MCP", Description = () => "(Model Context Protocol) server management",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "add",
                    Description = () => "Add a new MCP server (enter details or paste JSON)",
                    Action = async () =>
                    {
                        Console.WriteLine("Adding new MCP server configuration...");
                        Console.WriteLine();

                        Console.WriteLine("You can either paste a JSON server definition now (single line), or press Enter to enter fields interactively.");
                        Console.Write("Paste JSON or press Enter: ");
                        var input = User.ReadLineWithHistory();

                        McpServerDefinition serverDef = null!;
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            try
                            {
                                var parsed = input.FromJson<McpServerDefinition>();
                                if (parsed == null)
                                {
                                    Console.WriteLine("Failed to parse server definition JSON.");
                                    return Command.Result.Failed;
                                }
                                serverDef = parsed;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid JSON provided: {ex.Message}");
                                return Command.Result.Failed;
                            }
                        }
                        else
                        {
                            // Interactive entry
                            serverDef = new McpServerDefinition();
                            Console.Write("Server name: ");
                            var nameInput = User.ReadLineWithHistory();
                            if (string.IsNullOrWhiteSpace(nameInput)) return Command.Result.Cancelled;
                            serverDef.Name = nameInput!;
                            Console.Write("Command to start server (e.g. python server.py): ");
                            var cmdInput = User.ReadLineWithHistory();
                            serverDef.Command = string.IsNullOrWhiteSpace(cmdInput) ? string.Empty : cmdInput!;
                            Console.Write("Optional args (space-separated): ");
                            var argsLine = User.ReadLineWithHistory();
                            serverDef.Args = string.IsNullOrWhiteSpace(argsLine) ? new List<string>() : argsLine!.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                            Console.Write("Description (optional): ");
                            var descInput = User.ReadLineWithHistory();
                            serverDef.Description = descInput ?? string.Empty;
                        }

                        // Validate name uniqueness
                        var existingServers = McpManager.Instance.GetServerDefinitions();
                        if (existingServers.Any(s => s.Name.Equals(serverDef.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            Console.WriteLine($"A server with the name '{serverDef.Name}' already exists.");
                            return Command.Result.Failed;
                        }

                        var success = await McpManager.Instance.AddServerAsync(serverDef);
                        if (success)
                        {
                            Console.WriteLine($"Successfully added and connected to MCP server '{serverDef.Name}'.");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to add MCP server '{serverDef.Name}'.");
                            return Command.Result.Failed;
                        }

                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "remove",
                    Description = () => "Remove an MCP server configuration",
                    Action = async () =>
                    {
                        var servers = McpManager.Instance.GetServerDefinitions();
                        if (servers.Count == 0)
                        {
                            Console.WriteLine("No MCP servers configured.");
                            return Command.Result.Success;
                        }

                        var serverNames = servers.Select(s => $"{s.Name} - {s.Description}").ToList();
                        var selected = User.RenderMenu("Select MCP server to remove", serverNames);
                        
                        if (string.IsNullOrEmpty(selected))
                        {
                            return Command.Result.Cancelled;
                        }

                        // Extract the server name (everything before the first " - ")
                        var serverName = selected.Split(" - ")[0].Trim();
                        
                        Console.WriteLine($"Are you sure you want to remove MCP server '{serverName}'? (y/N): ");
                        var confirmation = Console.ReadLine();
                        
                        if (!string.Equals(confirmation?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Operation cancelled.");
                            return Command.Result.Cancelled;
                        }

                        try
                        {
                            var success = await McpManager.Instance.RemoveServerAsync(serverName);
                            if (success)
                            {
                                Console.WriteLine($"Successfully removed MCP server '{serverName}'.");
                            }
                            else
                            {
                                Console.WriteLine($"Failed to remove MCP server '{serverName}'. Check if the server exists.");
                                return Command.Result.Failed;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error removing MCP server: {ex.Message}");
                            return Command.Result.Failed;
                        }

                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "reload",
                    Description = () => "Reload and reconnect to all configured MCP servers",
                    Action = async () =>
                    {
                        Console.WriteLine("Reloading MCP servers...");
                        
                        try
                        {
                            // Shutdown all current connections
                            await McpManager.Instance.ShutdownAllAsync();
                            
                            // Reload from configuration files
                            await McpManager.Instance.LoadAllServersAsync();
                            
                            var connectedServers = McpManager.Instance.GetConnectedServers();
                            Console.WriteLine($"Successfully reloaded {connectedServers.Count} MCP servers.");
                            
                            if (connectedServers.Count > 0)
                            {
                                var totalTools = connectedServers.Sum(cs => cs.Tools.Count);
                                Console.WriteLine($"Total tools available: {totalTools}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reloading MCP servers: {ex.Message}");
                            return Command.Result.Failed;
                        }

                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "create documentation",
                    Description = () => "Create documentation for all MCP servers and tools",
                    Action = () =>
                    {
                        var connectedServers = McpManager.Instance.GetConnectedServers();
                        if (connectedServers.Count == 0)
                        {
                            Console.WriteLine("No MCP servers are currently connected.");
                            return Task.FromResult(Command.Result.Success);
                        }
                        Console.WriteLine("Creating documentation for MCP servers and tools...");
                        var docPath = Path.Combine(Directory.GetCurrentDirectory(), "mcp_documentation.md");
                        using (var writer = new StreamWriter(docPath, false))
                        {
                            writer.WriteLine("# MCP Servers Documentation");
                            writer.WriteLine();
                            foreach (var (name, tools) in connectedServers)
                            {
                                writer.WriteLine($"## {name}");
                                writer.WriteLine();
                                foreach (var tool in tools)
                                {
                                    writer.WriteLine($"### {tool.ToolName}");
                                    var exmapleText = tool.InputType?.GetCustomAttribute<ExampleText>()?.Text ?? string.Empty;
                                    if (!string.IsNullOrEmpty(exmapleText))
                                    {
                                        exmapleText = exmapleText.Replace("ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.", string.Empty);
                                        writer.WriteLine($"- **Description**: {tool.Description}");
                                        writer.WriteLine($"- **Input Schema**: {tool.InputSchema}");
                                        writer.WriteLine("- **Example Input**:");
                                        writer.WriteLine("```json");
                                        writer.WriteLine(exmapleText);
                                        writer.WriteLine("``` ");
                                    }
                                }
                                writer.WriteLine();
                            }
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "list tools",
                    Description = () => "List tools from connected MCP servers",
                    Action = () =>
                    {
                        var connectedServers = McpManager.Instance.GetConnectedServers();
                        if (connectedServers.Count == 0)
                        {
                            Console.WriteLine("No MCP servers are currently connected.");
                            return Task.FromResult(Command.Result.Success);
                        }

                        Console.WriteLine($"Tools from connected MCP servers ({connectedServers.Count} servers):");
                        foreach (var (serverName, tools) in connectedServers)
                        {
                            Console.WriteLine($"\n{serverName} ({tools.Count} tools):");
                            foreach (var tool in tools)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"{tool.ToolName}");
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.WriteLine($"    Usage: {tool.Usage}");
                                Console.WriteLine($"    Description: {tool.Description}");
                                Console.ResetColor();
                                var exmapleText = tool.InputType?.GetCustomAttribute<ExampleText>()?.Text ?? string.Empty;
                                if (!string.IsNullOrEmpty(exmapleText))
                                {
                                    exmapleText = exmapleText.Replace("ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.", string.Empty);
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine($"    Example input:\n{exmapleText}");
                                    Console.ResetColor();
                                }
                                Console.WriteLine("---------------------------------------------------------");
                            }
                        }

                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}

}
