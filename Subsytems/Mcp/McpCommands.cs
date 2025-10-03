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
                        Action = async () => await Log.MethodAsync(async ctx =>
                        {
                            ctx.OnlyEmitOnFailure();
                            ctx.Append(Log.Data.Message, "Reloading MCP servers");

                            try
                            {
                                // Shutdown all current connections
                                await McpManager.Instance.ShutdownAllAsync();
                                
                                // Reload from configuration files
                                await McpManager.Instance.LoadAllServersAsync();

                                var connectedServers = McpManager.Instance.GetConnectedServers();
                                Program.ui.WriteLine($"Successfully reloaded {connectedServers.Count} MCP servers.");

                                if (connectedServers.Count > 0)
                                {
                                    var totalTools = connectedServers.Sum(cs => cs.Tools.Count);
                                    Program.ui.WriteLine($"Total tools available: {totalTools}");
                                }
                            }
                            catch (Exception ex)
                            {
                                ctx.Failed("Error reloading MCP servers", ex);
                                return Command.Result.Failed;
                            }
                            ctx.Succeeded();
                            return Command.Result.Success;
                        })
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
                                Program.ui.WriteLine("No MCP servers are currently connected.");
                                return Task.FromResult(Command.Result.Success);
                            }
                            Program.ui.WriteLine("Creating documentation for MCP servers and tools...");
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
                                Program.ui.WriteLine("No MCP servers are currently connected.");
                                return Task.FromResult(Command.Result.Success);
                            }

                            Program.ui.WriteLine($"Tools from connected MCP servers ({connectedServers.Count} servers):");
                            foreach (var (serverName, tools) in connectedServers)
                            {
                                Program.ui.WriteLine($"\n{serverName} ({tools.Count} tools):");
                                foreach (var tool in tools)
                                {
                                    Program.ui.ForegroundColor = ConsoleColor.Yellow;
                                    Program.ui.WriteLine($"{tool.ToolName}");
                                    Program.ui.ForegroundColor = ConsoleColor.Gray;
                                    Program.ui.WriteLine($"    Usage: {tool.Usage}");
                                    Program.ui.WriteLine($"    Description: {tool.Description}");
                                    Program.ui.ResetColor();
                                    var exmapleText = tool.InputType?.GetCustomAttribute<ExampleText>()?.Text ?? string.Empty;
                                    if (!string.IsNullOrEmpty(exmapleText))
                                    {
                                        exmapleText = exmapleText.Replace("ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.", string.Empty);
                                        Program.ui.ForegroundColor = ConsoleColor.DarkGray;
                                        Program.ui.WriteLine($"    Example input:\n{exmapleText}");
                                        Program.ui.ResetColor();
                                    }
                                    Program.ui.WriteLine("---------------------------------------------------------");
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
