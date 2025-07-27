using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

public static class ToolRegistry
{
    private static Dictionary<string, ITool> _tools = new Dictionary<string, ITool>();
    
    public static void Initialize() => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        List<string> registered = new List<string>();
        Program.serviceProvider.ThrowIfNull("Service provider is not initialized.");
        foreach (var tool in Program.Tools)
        {
            if (Program.serviceProvider!.GetService(tool.Value) is ITool t)
            {
                _tools[tool.Key] = t;
                registered.Add(tool.Key);
            }

        }
        ctx.Append(Log.Data.Count, registered.Count);
        ctx.Append(Log.Data.Registered, registered.ToArray());
        ctx.Succeeded();
    });

    public static void RegisterMcpTool(string toolName, ITool tool)
    {
        _tools[toolName] = tool;
    }

    public static void UnregisterMcpTool(string toolName)
    {
        _tools.Remove(toolName);
    }

    public static bool IsToolRegistered(string toolName) => _tools.Keys.Any(t => t.Equals(toolName, StringComparison.OrdinalIgnoreCase));

    private static bool All(string _) => true;

    public static List<(string Name, string Description, string Usage)> GetRegisteredTools(Func<string, bool> filter) => _tools
        .Where(t => filter(t.Key))
        .Select(t => (t.Key, t.Value.Description, t.Value.Usage))
        .ToList();

    public static List<(string Name, string Description, string Usage)> GetRegisteredTools() => GetRegisteredTools(All);

    public static List<string> GetToolDescriptions(Func<string, bool> filter) => _tools
        .Where(t => filter(t.Key))
        .Select(t => $"--- Name: {t.Key} ---\nInputType: {t.Value.InputType.Name}\nDescription: {t.Value.Description}\nUsage: {t.Value.Usage}\n--- end {t.Key} ---")
        .ToList();

    public static List<string> GetToolDescriptions() => GetToolDescriptions(All);

    public static ITool? GetTool(string toolName) => _tools.TryGetValue(toolName, out var tool) ? tool : null;

    internal static async Task<ToolResult> InvokeInternalAsync(string toolName, object toolInput, Context Context, string userInput) =>
        await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Name, toolName);
        ctx.Append(Log.Data.ToolInput, toolInput?.ToString() ?? "<null>");

        if (_tools == null || string.IsNullOrEmpty(toolName) || !_tools.ContainsKey(toolName))
        {
            ctx.Failed($"Tool '{toolName}' is not registered.", Error.ToolNotAvailable);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' is not registered.", Context);
        }

        if (null == toolInput)
        {
            ctx.Failed($"Tool '{toolName}' requires input, but received null.", Error.InvalidInput);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' requires input, but received null.", Context);
        }

        var tool = _tools[toolName];
        if (tool == null)
        {
            ctx.Failed($"Tool '{toolName}' is not available.", Error.ToolNotAvailable);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' is not available.", Context);
        }
        var toolResult = await tool.InvokeAsync(toolInput, Context);
        ctx.Append(Log.Data.Result, $"Summarize:{toolResult.Summarize}, ResponseText:{toolResult.Response}");

        if (null == toolResult)
        {
            ctx.Failed($"Tool '{toolName}' returned null result.", Error.ToolFailed);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' returned null result.", Context);
        }

        if (!toolResult.Succeeded)
        {
            var error = toolResult.Error ?? "Unknown error";
            ctx.Append(Log.Data.Error, error);
            ctx.Failed($"Tool '{toolName}' failed: {error}", Error.ToolFailed);
            return ToolResult.Failure(error, Context);
        }

        Context toolContext = !toolResult.Summarize ? toolResult.Context : new Context(new[]
        {
            new ChatMessage { Role = Roles.System, Content= "Use the result of the invoked tool to answer the user's original question in natural language."},
            new ChatMessage { Role = Roles.User,
            Content = $"""
                The user asked a question that required invoking a tool to help answer it.

                {userInput}

                A tool, {toolName}, was invoked to help answer the user's question, the result of which was: {toolResult.Response}.

                Explain the answer succinctly.  You do not need to reference the tool or its output directly, just use it's result to inform your response.
            """}
        });

        var formattedResponse = await Engine.Provider!.PostChatAsync(toolContext, Program.config.Temperature);
        ctx.Succeeded();
        return ToolResult.Success(formattedResponse, toolResult.Context, toolResult.Summarize);
    });
    
    public static async Task<string> InvokeToolAsync(string toolName, object toolInput, Context Context, string lastUserInput)
    {
        var result = await InvokeInternalAsync(toolName, toolInput, Context, lastUserInput);
        return result.Response;
    }
}