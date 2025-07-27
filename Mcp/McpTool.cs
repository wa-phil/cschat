using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using ModelContextProtocol.Protocol;

// Basic MCP protocol types
public class ToolInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement? InputSchema { get; set; }
}

public class McpTool : ITool
{
    private readonly McpClient _client;
    internal readonly ToolInfo _toolInfo;
    private readonly string _serverName;
    private readonly Type _dynamicInputType;

    public McpTool(McpClient client, ToolInfo toolInfo, string serverName)
    {
        using var ctx = new Log.Context(Log.Level.Information);
        ctx.Append(Log.Data.Method, nameof(McpTool));
        ctx.Append(Log.Data.Name, toolInfo.Name);
        ctx.Append(Log.Data.ServerName, serverName);
        ctx.Append(Log.Data.ToolInput, toolInfo.InputSchema?.ToString() ?? "No input schema");
        _client = client;
        _toolInfo = toolInfo;
        _serverName = serverName;
        _dynamicInputType = GenerateDynamicInputType();
        ctx.Succeeded();
    }

    public string Description => $"[MCP:{_serverName}] {_toolInfo.Description ?? _toolInfo.Name}";

    public string Usage => GenerateUsage();

    public Type InputType => _dynamicInputType;

    public string ServerName => _serverName;
    public string ToolName => _toolInfo.Name;

    private Type GenerateDynamicInputType() => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.ServerName, _serverName);
        ctx.Append(Log.Data.Name, _toolInfo.Name);
        if (_toolInfo.InputSchema == null)
        {
            // No input schema, return a simple object type
            ctx.Append(Log.Data.Input, "NoInput");
            ctx.Succeeded();
            return typeof(NoInput);
        }

        // Currently returns Dictionary<string, object> since TinyJson handles this perfectly
        // and it matches what the MCP protocol expects for tool arguments.
        // 
        // Future enhancement: We could generate a dynamic type or anonymous type
        // based on the InputSchema properties for better type safety:
        // - Use System.Reflection.Emit to create a runtime type
        // - Use ExpandoObject for dynamic property access  
        // - Use anonymous types with proper property names
        // 
        // For now, Dictionary<string, object> provides the right balance of
        // flexibility and simplicity.
        ctx.Succeeded();
        return typeof(Dictionary<string, object>);
    });

    private string GenerateUsage() => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.Name, _toolInfo.Name);
        if (_toolInfo.InputSchema == null)
        {
            ctx.Succeeded();
            return $"{_toolInfo.Name}() - No parameters required";
        }

        try
        {
            var schema = _toolInfo.InputSchema.Value;

            if (schema.TryGetProperty("properties", out var properties))
            {
                var parameters = new List<string>();
                var requiredParams = new HashSet<string>();

                // Collect required parameters
                if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
                {
                    foreach (var req in required.EnumerateArray())
                    {
                        if (req.GetString() is string reqParam)
                        {
                            requiredParams.Add(reqParam);
                        }
                    }
                }

                foreach (var prop in properties.EnumerateObject())
                {
                    var paramInfo = GenerateParameterInfo(prop, requiredParams.Contains(prop.Name));
                    parameters.Add(paramInfo);
                }

                if (parameters.Count > 0)
                {
                    ctx.Succeeded();
                    return $"{_toolInfo.Name} - Parameters: {string.Join(", ", parameters)}";
                }
            }
        }
        catch (Exception ex)
        {
            ctx.Failed("Failed to parse tool input schema", ex);
            return $"{_toolInfo.Name}() - Error parsing parameters: {ex.Message}";
        }

        ctx.Warn("Either failed to parse tool input schema, or there is no input schema.");
        return $"{_toolInfo.Name}() - No parameters required";
    });

    private string GenerateParameterInfo(JsonProperty prop, bool isRequired)
    {
        var name = prop.Name;
        var typeInfo = "any";
        
        try
        {
            if (prop.Value.TryGetProperty("type", out var typeElement))
            {
                typeInfo = typeElement.GetString() ?? "any";
            }
            
            if (prop.Value.TryGetProperty("description", out var descElement))
            {
                var desc = descElement.GetString();
                if (!string.IsNullOrEmpty(desc))
                {
                    typeInfo += $" ({desc})";
                }
            }
        }
        catch
        {
            // Use default if parsing fails
        }

        return isRequired ? $"{name}: {typeInfo}" : $"[{name}]: {typeInfo}";
    }

    public async Task<ToolResult> InvokeAsync(object input, Context context) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Name, _toolInfo.Name);
        ctx.Append(Log.Data.Input, (null == input ? "<null>" : input.ToJson()));
        ctx.Append(Log.Data.ServerName, _serverName);

        try
        {
            // Convert input to the expected Dictionary<string, object> format
            Dictionary<string, object> arguments;

            if (input is Dictionary<string, object> directDict)
            {
                arguments = directDict;
            }
            else
            {
                // Convert any object to Dictionary<string, object>
                var json = input?.ToJson() ?? "{}";
                arguments = json.FromJson<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            }
            ctx.Append(Log.Data.ToolInput, arguments.ToJson());

            // Convert Dictionary<string, object> to Dictionary<string, object?> for MCP client, we need to do this 
            // because CallToolAsync expects nullable values, because the MCP protocol allows for missing fields.
            var response = await _client.CallToolAsync(_toolInfo.Name, arguments.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value));

            if (response?.Content == null || response.Content.Count == 0)
            {
                ctx.Failed("No response content from MCP tool", Error.EmptyResponse);
                return ToolResult.Failure("No response content from MCP tool", context);
            }

            // for now, only handle text content blocks
            if (response.Content.All(c => c is not TextContentBlock))
            {
                ctx.Failed("MCP tool returned non-text content", Error.ToolFailed);
                return ToolResult.Failure("MCP tool returned non-text content", context);
            }

            var responseText = response.Content.Select(c => (c as TextContentBlock)?.Text ?? string.Empty)
                .Where(text => !string.IsNullOrEmpty(text))
                .Aggregate((current, next) => $"{current}\n{next}");

            if (response.IsError == true)
            {
                ctx.Failed($"MCP tool returned error: {responseText}", Error.ToolFailed);
                return ToolResult.Failure($"MCP tool returned error: {responseText}", context);
            }

            // Combine all content into a simple string representation
            ctx.Append(Log.Data.Response, responseText);
            ctx.Succeeded();
            return ToolResult.Success(responseText, context, true);
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error calling MCP tool: {ex.Message}", Error.ToolFailed);
            return ToolResult.Failure($"Error calling MCP tool: {ex.Message}", context);
        }
    });
}