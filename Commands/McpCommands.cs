using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager : Command
{
    public static Command CreateMcpCommands()
    {
        return new Command
        {
            Name = "mcp",
            Description = "MCP (Model Context Protocol) server management",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "mcp directory",
                    Description = "Set the directory for MCP server configurations",
                    Action = () =>
                    {
                        Console.Write("Enter the path to the MCP server directory (default: ./mcp_servers): ");
                        var input = Console.ReadLine()?.Trim();
                        if (string.IsNullOrWhiteSpace(input))
                        {
                            input = "./mcp_servers";
                        }
                        if (!Directory.Exists(input))
                        {
                            Console.WriteLine($"Directory does not exist: {input}");
                            return Task.FromResult(Command.Result.Failed);
                        }
                        Program.config.McpServerDirectory = input;
                        Console.WriteLine($"MCP server directory set to: {input}");
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "list",
                    Description = "List configured MCP servers",
                    Action = () =>
                    {
                        var servers = McpManager.Instance.GetServerDefinitions();
                        if (servers.Count == 0)
                        {
                            Console.WriteLine("No MCP servers configured.");
                            return Task.FromResult(Command.Result.Success);
                        }

                        Console.WriteLine($"Configured MCP servers ({servers.Count}):");
                        foreach (var server in servers)
                        {
                            var status = McpManager.Instance.GetConnectedServers()
                                .Any(cs => cs.ServerName == server.Name) ? "Connected" : "Not Connected";

                            Console.WriteLine($"  {server.Name} [{status}]");
                            Console.WriteLine($"    Description: {server.Description}");
                            Console.WriteLine($"    Config file: {server.Name}.json");
                            Console.WriteLine($"    Command: {server.Command}");
                            if (server.Args.Count > 0)
                            {
                                Console.WriteLine($"    Args: {string.Join(" ", server.Args)}");
                            }
                            Console.WriteLine($"    Enabled: {server.Enabled}");
                            Console.WriteLine($"    Created: {server.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                            Console.WriteLine();
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "add",
                    Description = "Add a new MCP server from a configuration file",
                    Action = async () =>
                    {
                        Console.WriteLine("Adding new MCP server configuration...");
                        Console.WriteLine();
                        Console.Write("Path to MCP server configuration file: ");
                        
                        var filePath = User.ReadLineWithHistory();
                        if (string.IsNullOrWhiteSpace(filePath))
                        {
                            Console.WriteLine("No file path provided.");
                            return Command.Result.Cancelled;
                        }

                        // Expand ~ to home directory if needed
                        if (filePath.StartsWith("~/"))
                        {
                            filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), filePath.Substring(2));
                        }

                        if (!File.Exists(filePath))
                        {
                            Console.WriteLine($"File not found: {filePath}");
                            return Command.Result.Failed;
                        }

                        try
                        {
                            var configJson = await File.ReadAllTextAsync(filePath);
                            Console.WriteLine($"Raw JSON content: {configJson}");
                            
                            var serverDef = configJson.FromJson<McpServerDefinition>();
                            
                            if (serverDef == null)
                            {
                                Console.WriteLine("Failed to parse MCP server configuration from file.");
                                return Command.Result.Failed;
                            }

                            Console.WriteLine();
                            Console.WriteLine("Parsed server configuration:");
                            Console.WriteLine($"  Name: '{serverDef.Name}'");
                            Console.WriteLine($"  Command: '{serverDef.Command}'");
                            Console.WriteLine($"  Description: '{serverDef.Description}'");
                            Console.WriteLine($"  Args: [{string.Join(", ", serverDef.Args.Select(a => $"'{a}'"))}]");
                            Console.WriteLine();

                            // Ask for a friendly name
                            Console.Write($"Enter a friendly name for this server (default: {serverDef.Name ?? "mcp-server"}): ");
                            var friendlyName = User.ReadLineWithHistory();
                            
                            if (string.IsNullOrWhiteSpace(friendlyName))
                            {
                                friendlyName = serverDef.Name ?? "mcp-server";
                            }

                            // Check if name already exists
                            var existingServers = McpManager.Instance.GetServerDefinitions();
                            if (existingServers.Any(s => s.Name.Equals(friendlyName, StringComparison.OrdinalIgnoreCase)))
                            {
                                Console.WriteLine($"A server with the name '{friendlyName}' already exists.");
                                return Command.Result.Failed;
                            }

                            // Update the server definition with the friendly name
                            serverDef.Name = friendlyName;
                            serverDef.Enabled = true;
                            serverDef.CreatedAt = DateTime.UtcNow;
                            serverDef.UpdatedAt = DateTime.UtcNow;

                            var success = await McpManager.Instance.AddServerAsync(serverDef);
                            if (success)
                            {
                                Console.WriteLine($"Successfully added and connected to MCP server '{friendlyName}'.");
                                
                                // Show available tools
                                var connectedServers = McpManager.Instance.GetConnectedServers();
                                var connectedServer = connectedServers.FirstOrDefault(cs => cs.ServerName == friendlyName);
                                if (connectedServer.Tools?.Count > 0)
                                {
                                    Console.WriteLine($"Available tools from {friendlyName}:");
                                    foreach (var tool in connectedServer.Tools)
                                    {
                                        Console.WriteLine($"  - {tool.ToolName}: {tool.Description}");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Failed to add MCP server '{friendlyName}'. Check the server configuration and ensure the server is accessible.");
                                return Command.Result.Failed;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error adding MCP server: {ex.Message}");
                            return Command.Result.Failed;
                        }

                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "remove",
                    Description = "Remove an MCP server configuration",
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
                    Description = "Reload and reconnect to all configured MCP servers",
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
                    Name = "tools",
                    Description = "List tools from connected MCP servers",
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
                                Console.WriteLine($"  - {tool.ToolName}: {tool.Description}");
                            }
                        }
                        
                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}
