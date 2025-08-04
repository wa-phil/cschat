using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;

public class McpManager
{
    private static McpManager? _instance;
    public static McpManager Instance => _instance ??= new McpManager();

    private readonly ConcurrentDictionary<string, McpClient> _clients = new();
    private readonly ConcurrentDictionary<string, List<McpTool>> _serverTools = new();
    private readonly string _serverDefinitionsPath;

    private McpManager()
    {
        _serverDefinitionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Program.config.McpServerDirectory);
        Directory.CreateDirectory(_serverDefinitionsPath);
    }

    public async Task<bool> AddServerAsync(McpServerDefinition serverDef) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Name, serverDef.Name);
        
        try
        {
            // Save the server definition first
            var filePath = Path.Combine(_serverDefinitionsPath, $"{serverDef.Name}.json");
            var json = serverDef.ToJson();
            await File.WriteAllTextAsync(filePath, json);
            Console.WriteLine($"Saved server definition to: {filePath}");

            // Connect to the server and register tools
            var success = await ConnectToServerAsync(serverDef);
            if (success)
            {
                ctx.Succeeded();
            }
            else
            {
                ctx.Failed("Failed to connect to MCP server", Error.ConnectionFailed);
                // Note: We keep the config file even if connection failed
                // so user can troubleshoot and use 'mcp reload' later
            }
            return success;
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error adding MCP server: {ex.Message}", Error.ToolFailed);
            return false;
        }
    });

    public async Task<bool> RemoveServerAsync(string serverName) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Name, serverName);
        
        try
        {
            // Disconnect from server and remove tools
            await DisconnectFromServerAsync(serverName);

            // Remove the server definition file
            var filePath = Path.Combine(_serverDefinitionsPath, $"{serverName}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            ctx.Succeeded();
            return true;
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error removing MCP server: {ex.Message}", Error.ToolFailed);
            return false;
        }
    });

    public async Task LoadAllServersAsync() => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        
        var serverFiles = Directory.GetFiles(_serverDefinitionsPath, "*.json");
        var loadedCount = 0;
        
        foreach (var file in serverFiles)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"Loading MCP server from {Path.GetFileName(file)}...");
                Console.ResetColor();
                var json = await File.ReadAllTextAsync(file);
                var serverDef = json.FromJson<McpServerDefinition>();

                if (serverDef != null && serverDef.Enabled)
                {
                    ctx.Append(Log.Data.Name, serverDef.Name);
                    ctx.Append(Log.Data.Command, serverDef.Command);
                    var success = await ConnectToServerAsync(serverDef);
                    if (success)
                    {
                        loadedCount++;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Successfully connected to MCP server: {serverDef.Name}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Failed to connect to MCP server: {serverDef.Name}");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Warning: Failed to load MCP server from {Path.GetFileName(file)}: {ex.Message}.  See system logs for additional details.");
                Console.ResetColor();
                return;
            }
        }
        
        ctx.Append(Log.Data.Count, loadedCount);
        ctx.Succeeded();
    });

    private async Task<bool> ConnectToServerAsync(McpServerDefinition serverDef) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Name, serverDef.Name);
        ctx.Append(Log.Data.Command, serverDef.Command);
        
        try
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write($"Connecting client to MCP server: {serverDef.Name}...");
            Console.ResetColor();

            // Create the MCP client using the simplified API
            var client = await McpClient.CreateAsync(serverDef);
            if (client == null)
            {
                ctx.Failed("Failed to create MCP client", Error.InitializationFailed);
                return false;
            }

            // List tools from the MCP server
            var clientTools = await client.ListToolsAsync();
            
            // Convert MCP client tools to our McpTool wrapper
            var tools = clientTools.Select(mcpTool => {
                // Extract the input schema using the JsonSchema property
                JsonElement? inputSchema = null;
                try 
                {
                    var schemaProperty = mcpTool.GetType().GetProperty("JsonSchema");
                    if (schemaProperty != null)
                    {
                        var schemaValue = schemaProperty.GetValue(mcpTool);
                        if (schemaValue is JsonElement jsonElement)
                        {
                            inputSchema = jsonElement;
                        }
                    }
                }
                catch
                {
                    // Ignore schema extraction errors and continue with null schema
                }
                
                return new McpTool(client, new ToolInfo 
                { 
                    Name = mcpTool.Name, 
                    Description = mcpTool.Description,
                    InputSchema = inputSchema
                }, serverDef.Name);
            }).ToList();
        
            
            // Register tools in the global tool registry
            foreach (var tool in tools)
            {
                var toolName = $"{serverDef.Name}_{tool.ToolName}";
                ToolRegistry.RegisterMcpTool(toolName, tool);
            }
            
            // Store the client and tools
            _clients[serverDef.Name] = client;
            _serverTools[serverDef.Name] = tools;
            
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"✅ {clientTools.Count} tools added.");
            Console.ResetColor();
            ctx.Append(Log.Data.Count, tools.Count);
            ctx.Succeeded();
            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($" failed: {ex.Message}");
            Console.ResetColor();
            ctx.Failed($"Error connecting to MCP server: {ex.Message}", ex);
            return false;
        }
    });

    private Task DisconnectFromServerAsync(string serverName) => Log.MethodAsync(ctx =>
    {
        ctx.Append(Log.Data.Name, serverName);
        
        try
        {
            // Remove tools from registry
            if (_serverTools.TryRemove(serverName, out var tools))
            {
                foreach (var tool in tools)
                {
                    ToolRegistry.UnregisterMcpTool($"{serverName}_{tool.ToolName}");
                }
            }

            // Disconnect client
            if (_clients.TryRemove(serverName, out var client))
            {
                try
                {
                    // Just dispose the client, no shutdown call needed
                    client.Dispose();
                }
                catch
                {
                    // Ignore shutdown errors
                }
            }

            ctx.Succeeded();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error disconnecting from MCP server: {ex.Message}", Error.ToolFailed);
            return Task.CompletedTask;
        }
    });

    public List<McpServerDefinition> GetServerDefinitions()
    {
        var servers = new List<McpServerDefinition>();
        
        try
        {
            var serverFiles = Directory.GetFiles(_serverDefinitionsPath, "*.json");
            foreach (var file in serverFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);  
                    var serverDef = json.FromJson<McpServerDefinition>();
                    if (serverDef != null)
                    {
                        servers.Add(serverDef);
                    }
                }
                catch
                {
                    // Ignore invalid server definition files
                }
            }
        }
        catch
        {
            // Ignore directory access errors
        }

        return servers;
    }

    public List<(string ServerName, List<McpTool> Tools)> GetConnectedServers() => _serverTools.Select(kvp => (kvp.Key, kvp.Value)).ToList();

    public async Task ShutdownAllAsync()
    {
        var tasks = _clients.Values.Select(client =>
        {
            try
            {
                client.Dispose();
                return Task.CompletedTask;
            }
            catch
            {
                // Ignore shutdown errors
                return Task.CompletedTask;
            }
        });

        await Task.WhenAll(tasks);
        
        _clients.Clear();
        _serverTools.Clear();
    }
}