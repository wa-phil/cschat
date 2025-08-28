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
