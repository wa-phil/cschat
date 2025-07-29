using System;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using System.Reflection.Emit;
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
        _client = client;
        _toolInfo = toolInfo;
        _serverName = serverName;
        _dynamicInputType = GenerateDynamicInputType();
    }

    public string Description => $"{_toolInfo.Description}";

    public string Usage => GenerateUsage();

    public Type InputType => _dynamicInputType;

    public string ServerName => _serverName;
    public string ToolName => _toolInfo.Name;

    private Type GenerateDynamicInputType() => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.ServerName, _serverName);
        ctx.Append(Log.Data.Name, _toolInfo.Name);
        var schemaText = _toolInfo.InputSchema?.ToString();
        ctx.Append(Log.Data.Schema, schemaText ?? "<null>");

        if (string.IsNullOrWhiteSpace(schemaText))
        {
            ctx.Append(Log.Data.Input, "NoInput");
            ctx.Succeeded();
            return typeof(NoInput);
        }

        var schema = schemaText.FromJson<Dictionary<string, object>>() ?? new Dictionary<string, object>();

        if (!schema.TryGetValue("properties", out var propsObj) || propsObj is not Dictionary<string, object> props || 0 == props.Keys.Count)
        {
            ctx.Append(Log.Data.Input, "NoInput");
            ctx.Succeeded();
            return typeof(NoInput);
        }

        var required = (schema.TryGetValue("required", out var reqObj) && reqObj is List<object> reqList)
            ? reqList.Select(r => r.ToString() ?? "").ToHashSet()
            : new HashSet<string>();

        var exampleLines = new List<string>();
        var explainedProps = new List<string>();
        var propMap = new Dictionary<string, Type>();

        foreach (var kvp in props)
        {
            var name = kvp.Key;
            var desc = "";
            var type = "string";

            if (kvp.Value is Dictionary<string, object> propDict)
            {
                if (propDict.TryGetValue("type", out var typeVal)) type = typeVal?.ToString() ?? "string";
                if (propDict.TryGetValue("description", out var descVal)) desc = descVal?.ToString() ?? "";
            }

            propMap[name] = type switch
            {
                "string" => typeof(string),
                "number" => typeof(double),
                "integer" => typeof(int),
                "boolean" => typeof(bool),
                _ => typeof(object)
            };

            exampleLines.Add($"  \"{name}\": \"<{name}>\"");

            if (!string.IsNullOrEmpty(desc))
            {
                // If there's a description for the property, then the type is not self-explanatory.
                explainedProps.Add($"   <{name}> {(required.Contains(name) ? "" : "is optional, and ")}is {desc}.");
            }
        }

        var exampleText = $"{{\n{string.Join(",\n", exampleLines)}\n}}";
        if (explainedProps.Count != 0)
        {
            exampleText += $"\nWhere:\n{string.Join("\n", explainedProps)}";
        }
        exampleText += "\nONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.";
        ctx.Append(Log.Data.ExampleText, exampleText);

        // Create the dynamic type
        var typeName = $"{_serverName}_{_toolInfo.Name}_{Guid.NewGuid():N}";
        var asmName = new AssemblyName("DynamicMcpTypes");
        var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
        var moduleBuilder = asmBuilder.DefineDynamicModule("MainModule");
        var typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class);

        // Add the [ExampleText] attribute
        var attrCtor = typeof(ExampleText).GetConstructor(new[] { typeof(string) });
        var attrBuilder = new CustomAttributeBuilder(attrCtor!, new object[] { exampleText });
        typeBuilder.SetCustomAttribute(attrBuilder);

        // Add properties
        foreach (var (propName, propType) in propMap)
        {
            var field = typeBuilder.DefineField($"_{propName}", propType, FieldAttributes.Private);
            var propBuilder = typeBuilder.DefineProperty(propName, PropertyAttributes.HasDefault, propType, null);

            var getter = typeBuilder.DefineMethod($"get_{propName}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                propType, Type.EmptyTypes);

            var getterIL = getter.GetILGenerator();
            getterIL.Emit(OpCodes.Ldarg_0);
            getterIL.Emit(OpCodes.Ldfld, field);
            getterIL.Emit(OpCodes.Ret);

            var setter = typeBuilder.DefineMethod($"set_{propName}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                null, new[] { propType });

            var setterIL = setter.GetILGenerator();
            setterIL.Emit(OpCodes.Ldarg_0);
            setterIL.Emit(OpCodes.Ldarg_1);
            setterIL.Emit(OpCodes.Stfld, field);
            setterIL.Emit(OpCodes.Ret);

            propBuilder.SetGetMethod(getter);
            propBuilder.SetSetMethod(setter);
        }

        var dynamicType = typeBuilder.CreateTypeInfo()!;
        ctx.Append(Log.Data.Input, $"Generated type: {dynamicType.Name}");
        ctx.Succeeded();
        return dynamicType;
    });

    private string GenerateUsage() => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Name, _toolInfo.Name);
        if (_toolInfo.InputSchema == null)
        {
            ctx.Succeeded();
            return $"{_toolInfo.Name}() - No parameters required";
        }

        try
        {
            ctx.Append(Log.Data.Schema, _toolInfo.InputSchema.ToString() ?? "<null>");
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

                ctx.Succeeded();
                if (parameters.Count > 0)
                {
                    return $"{_toolInfo.Name} - Parameters: {string.Join(", ", parameters)}";
                }
                else
                {
                    ctx.Append(Log.Data.Message, "No parameters found in input schema.");
                    return $"{_toolInfo.Name}() - No parameters required";
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
            return ToolResult.Success(responseText, context, false);
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error calling MCP tool: {ex.Message}", Error.ToolFailed);
            return ToolResult.Failure($"Error calling MCP tool: {ex.Message}", context);
        }
    });
}