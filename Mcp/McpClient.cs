using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

public class McpClient : IDisposable
{
    private readonly IMcpClient _client;
    private readonly IClientTransport _transport;
    private bool _disposed = false;

    private McpClient(IMcpClient client, IClientTransport transport)
    {
        _client = client;
        _transport = transport;
    }

    public static async Task<McpClient?> CreateAsync(McpServerDefinition serverDef) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.ServerName, serverDef.Name);
        try
        {
            // Use null logger factory for now - we can improve logging later
            var loggerFactory = NullLoggerFactory.Instance;

            // Configure the transport to start the MCP server process
            var transportOptions = new StdioClientTransportOptions
            {
                Command = serverDef.Command,
                Arguments = serverDef.Args,
                WorkingDirectory = serverDef.WorkingDirectory,
                EnvironmentVariables = serverDef.Environment.ToDictionary(kv => kv.Key, kv => (string?)kv.Value)
            };

            // Create transport - this handles process management internally
            var transport = new StdioClientTransport(transportOptions, loggerFactory);

            // Create client options
            var clientOptions = new McpClientOptions();

            // Create the MCP client and connect
            var client = await McpClientFactory.CreateAsync(transport, clientOptions, loggerFactory);
            var result = new McpClient(client, transport);
            ctx.Succeeded(null != result);
            return result;
        }
        catch (Exception ex)
        {
            ctx.Failed("Failed to create MCP client", ex);
            return null;
        }
    });

    public async Task<List<McpClientTool>> ListToolsAsync() => await Log.MethodAsync(async ctx =>
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(McpClient));

        try
        {
            var tools = await _client.ListToolsAsync();
            var list = tools.ToList();
            ctx.Append(Log.Data.Count, list.Count);
            ctx.Append(Log.Data.Names, list.Select(t => t.Name).ToArray());
            ctx.Succeeded();
            return list;
        }
        catch (Exception ex)
        {
            ctx.Failed("Failed to list tools", ex);
            return new List<McpClientTool>();
        }
    });

    public async Task<CallToolResult> CallToolAsync(string toolName, Dictionary<string, object?> arguments) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        if (_disposed)
            throw new ObjectDisposedException(nameof(McpClient));

        var tools = await _client.ListToolsAsync();
        var tool = tools.FirstOrDefault(t => t.Name == toolName);

        if (tool != null)
        {
            var result = await tool.CallAsync(arguments, null, null);
            ctx.Succeeded();
            return result;
        }

        throw new InvalidOperationException($"Tool '{toolName}' not found");
    });

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            (_transport as IAsyncDisposable)?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore disposal errors
        }

        _disposed = true;
    }
}