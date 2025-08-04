using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

[IsConfigurable("explain_program_to_user")]
public class HelpTool : ITool
{
    public string Description => "Get information about the program, what it can do, its commands, and how to use it.";
    public string Usage => "Use this tool to help the user understand the program, its commands, and how to use it effectively.";
    public Type InputType => typeof(NoInput);
    public string InputSchema => "NoInput";
    public async Task<ToolResult> InvokeAsync(object input, Context Context) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        try
        {
            ctx.Append(Log.Data.Input, input?.ToString() ?? "<null>");
            var helpText = Engine.BuildCommandTreeArt(Program.commandManager.SubCommands, "", false, false);
            var working = new Context("""
Your job is to confidently summarize, and explain the cschat program to a user.
CSChat is a console-based chat application that allows users to interact with a chat provider using various commands.
Be sure to tell the user for more information, visit: https://github.com/wa-phil/cschat
Do not provide any code, or technical details.
Do not make up or guess any details, only use the information provided in the menu structure and layout.
Provide a brief overview of the program, how to use it, and what it can do.
Explain that there are commands available via the menu, and provide a brief overview of the menu.
Be sure to also call out that the user can press the ESC key to open the menu.
What follows is the menu structure and layout, followed by the current config, use these contents to provide a summary of the program and its commands.
Be sure to detail values that are configurable, and how they might alter the behavior of the program.
""");
            working.AddUserMessage($"{helpText}\n\nCurrent Config:\n{Program.config.ToJson()}");

            var summary = await Engine.Provider!.PostChatAsync(working, 0.2f);
            ctx.Append(Log.Data.Result, summary);
            ctx.Succeeded();
            return ToolResult.Success(summary, Context);
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error displaying help", ex);
            return ToolResult.Failure($"Error displaying help", Context);
        }
    });
}

[IsConfigurable("summarize_text")]
public class TextSummary : ITool
{
    public string Description => "Summarizes provided text, optionally using a prompt to guide the summary.";
    public string Usage => "Provide text to summarize, and an optional prompt to guide the summary.";
    public Type InputType => typeof(SummarizeText);
    public string InputSchema => "SummarizeText";
    public async Task<ToolResult> InvokeAsync(object input, Context Context) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        try
        {
            ctx.Append(Log.Data.Input, input?.ToString() ?? "<null>");
            var stringInput = input as SummarizeText ?? throw new ArgumentException("Expected SummarizeText as input");
            var working = new Context(stringInput.Prompt ?? "Summarize the provided text");
            working.AddUserMessage(stringInput.Text);
            var summary = await Engine.Provider!.PostChatAsync(working, 0.2f);
            ctx.Append(Log.Data.Result, summary);
            ctx.Succeeded();
            return ToolResult.Success(summary, Context);
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error summarizing text", ex);
            return ToolResult.Failure($"Error summarizing text", Context);
        }
    });
}

[IsConfigurable("search_knowledge_base")]
public class SearchVectorStore : ITool
{
    public string Description => "Searches the local knowledge base for relevant information.";
    public string Usage => "Provide a semantic query to search the knowledge base.";
    public Type InputType => typeof(string);
    public string InputSchema => "string";

    public async Task<ToolResult> InvokeAsync(object input, Context Context) => await Log.MethodAsync(async ctx =>
    {
        try
        {
            var stringInput = input as string ?? throw new ArgumentException("Expected string as input");
            ctx.Append(Log.Data.Input, stringInput);
            var results = await ContextManager.SearchVectorDB(stringInput);
            var resultText = results != null && results.Count > 0
                ? string.Join("\n", results.Select(r => $"---begin {r.Reference}---\n{r.Content}\n---end {r.Reference}---"))
                : "No relevant information found.";
            ctx.Succeeded();
            return ToolResult.Success(resultText, Context);
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error searching knowledge base", ex);
            return ToolResult.Failure($"Error searching knowledge base", Context);
        }
    });
}

[IsConfigurable("Calculator")]
public class CalculatorTool : ITool
{
    public string Description => "Evaluates basic math expressions.";
    public string Usage => "Provide a mathematical expression to evaluate. Supports basic arithmetic operations, such as addition (+), subtraction (-), multiplication (*), division (/), and parentheses (()).";
    public Type InputType => typeof(string);
    public string InputSchema => "string";

    public Task<ToolResult> InvokeAsync(object input, Context Context) => Log.Method(ctx =>
    {
        try
        {
            var stringInput = input as string ?? throw new ArgumentException("Expected string as input");
            var result = new System.Data.DataTable().Compute(stringInput, null);
            ctx.Succeeded();
            return Task.FromResult(ToolResult.Success(result?.ToString() ?? string.Empty, Context));
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
    public string InputSchema => "NoInput";

    public Task<ToolResult> InvokeAsync(object input, Context Context) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var result = DateTime.Now.ToString("u");
        ctx.Succeeded();
        return Task.FromResult(ToolResult.Success(result, Context));
    });
}