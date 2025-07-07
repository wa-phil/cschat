using System;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;

// Can't use a record here as JSON parser doesn't support parsing records. :(
public class ToolSuggestion
{
    [DataMember(Name = "tool")]
    public string Tool { get; set;}

    [DataMember(Name = "input")]
    public string Input { get; set;}
}

public static class ToolRegistry
{
    private static Dictionary<string, ITool> _tools = new Dictionary<string, ITool>();
    public static void Initialize() => Log.Method(ctx =>
    {
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

    public static List<(string Name, string Description, string Usage)> GetRegisteredTools() => 
        _tools.Select(t => (t.Key, t.Value.Description, t.Value.Usage)).ToList();

    public static async Task<string> InvokeToolAsync(string toolName, string input) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Name, toolName);
        ctx.Append(Log.Data.ToolInput, input);
        if (_tools == null || !_tools.ContainsKey(toolName))
        {
            ctx.Failed($"Tool '{toolName}' is not registered.", Error.ToolNotFound);
            return string.Empty;
        }

        var tool = _tools[toolName];
        if (tool == null)
        {
            ctx.Failed($"Tool '{toolName}' is not available.", Error.ToolNotAvailable);
            return string.Empty;
        }

        try
        {
            var result = await tool.InvokeAsync(input);
            ctx.Append(Log.Data.Result, result);
            ctx.Succeeded();
            return result;
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error invoking tool '{toolName}': {ex.Message}", ex);
            return string.Empty;
        }
    }); 
}

[IsConfigurable("Calculator")]
public class CalculatorTool : ITool
{
    public string Description => "Evaluates basic math expressions.";
    public string Usage => "Input: arithmetic expression as string (e.g. '2 + 3 * 4')";

    public Task<string> InvokeAsync(string input)
    {
        try
        {
            var result = new System.Data.DataTable().Compute(input, null);
            return Task.FromResult(result?.ToString() ?? string.Empty);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"ERROR: {ex.Message}");
        }
    }
}

[IsConfigurable("CurrentDateTime")]
public class CurrentDateTimeTool : ITool
{
    public string Description => "Returns the current date and time.";
    public string Usage => "Input: None.";

    public Task<string> InvokeAsync(string _)
    {
        return Task.FromResult(DateTime.Now.ToString("u"));
    }
}

