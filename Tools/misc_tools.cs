using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

[IsConfigurable("Calculator")]
public class CalculatorTool : ITool
{
    public string Description => "Evaluates basic math expressions.";
    public string Usage => "Provide a mathematical expression to evaluate. Supports basic arithmetic operations, such as addition (+), subtraction (-), multiplication (*), division (/), and parentheses (()).";
    public Type InputType => typeof(string);

    public Task<ToolResult> InvokeAsync(object input, Context Context) => Log.Method(ctx =>
    {
        try
        {
            var stringInput = input as string ?? throw new ArgumentException("Expected string as input");
            var result = new System.Data.DataTable().Compute(stringInput, null);
            ctx.Succeeded();
            return Task.FromResult(ToolResult.Success(result?.ToString() ?? string.Empty, Context, true));
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error evaluating expression", ex);
            return Task.FromResult(ToolResult.Failure($"Error evaluating expression", Context));
        }
    });
}

[IsConfigurable("datetime_current")]
public class datetime_current : ITool
{
    public string Description => "Returns the current local date and time in UTC format.";
    public string Usage => "No input required. Simply returns the current date and time.";
    public Type InputType => typeof(NoInput);

    public Task<ToolResult> InvokeAsync(object input, Context Context) => Log.Method(ctx =>
    {
        var result = DateTime.Now.ToString("u");
        ctx.Succeeded();
        return Task.FromResult(ToolResult.Success(result, Context, true));
    });
}