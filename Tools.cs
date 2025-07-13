using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

// Can't use a record here as JSON parser doesn't support parsing records. :(
public class ToolSuggestion
{
    [DataMember(Name = "tool")]
    public string Tool { get; set;} = string.Empty;

    [DataMember(Name = "input")]
    public string Input { get; set;} = string.Empty;
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

    public static bool IsToolRegistered(string toolName) => _tools.Keys.Any(t => t.Equals(toolName, StringComparison.OrdinalIgnoreCase));

    private static bool All(string _) => true;

    public static List<(string Name, string Description, string Usage)> GetRegisteredTools(Func<string, bool> filter) => _tools
        .Where(t => filter(t.Key))
        .Select(t => (t.Key, t.Value.Description, t.Value.Usage))
        .ToList();

    public static List<(string Name, string Description, string Usage)> GetRegisteredTools() => GetRegisteredTools(All);

    public static List<string> GetToolDescriptions(Func<string, bool> filter) => _tools
        .Where(t => filter(t.Key))
        .Select(t => $"Name: {t.Key}\nDescription: {t.Value.Description}\nUsage: {t.Value.Usage}")
        .ToList();

    public static List<string> GetToolDescriptions() => GetToolDescriptions(All);

    internal static async Task<ToolResult> InvokeInternalAsync(string toolName, string toolInput, Memory memory, string userInput) =>
        await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Name, toolName);
        ctx.Append(Log.Data.ToolInput, toolInput);

        if (_tools == null || !_tools.ContainsKey(toolName))
        {
            ctx.Failed($"Tool '{toolName}' is not registered.", Error.ToolNotAvailable);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' is not registered.", memory);
        }

        var tool = _tools[toolName];
        if (tool == null)
        {
            ctx.Failed($"Tool '{toolName}' is not available.", Error.ToolNotAvailable);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' is not available.", memory);
        }
        var toolResult = await tool.InvokeAsync(toolInput, memory);
        ctx.Append(Log.Data.Result, $"Summarize:{toolResult.Summarize}, ResponseText:{toolResult.Response}");

        if (null == toolResult)
        {
            ctx.Failed($"Tool '{toolName}' returned null result.", Error.ToolFailed);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' returned null result.", memory);
        }

        if (!toolResult.Succeeded)
        {
            var error = toolResult.Error ?? "Unknown error";
            ctx.Append(Log.Data.Error, error);
            ctx.Failed($"Tool '{toolName}' failed: {error}", Error.ToolFailed);
            return ToolResult.Failure(error, memory);
        }

        Memory toolMemory = !toolResult.Summarize ? toolResult.Memory : new Memory(new[]
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

        var formattedResponse = await Engine.Provider!.PostChatAsync(toolMemory, Program.config.Temperature);
        ctx.Succeeded();
        return ToolResult.Success(formattedResponse, toolResult.Memory, toolResult.Summarize);
    });
    
    public static async Task<string> InvokeToolAsync(string toolName, string toolInput, Memory memory, string lastUserInput)
    {
        var result = await InvokeInternalAsync(toolName, toolInput, memory, lastUserInput);
        return result.Response;
    }
}

[IsConfigurable("Calculator")]
public class CalculatorTool : ITool
{
    public string Description => "Evaluates basic math expressions.";
    public string Usage => "Input: arithmetic expression as string (e.g. '2 + 3 * 4')";

    public Task<ToolResult> InvokeAsync(string input, Memory memory) => Log.Method(ctx =>
    {
        try
        {
            var result = new System.Data.DataTable().Compute(input, null);
            ctx.Succeeded();
            return Task.FromResult(ToolResult.Success(result?.ToString() ?? string.Empty, memory, true));
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error evaluating expression '{input}'", ex);
            return Task.FromResult(ToolResult.Failure($"Error evaluating expression '{input}'", memory));
        }
    });
}

[IsConfigurable("CurrentDateTime")]
public class CurrentDateTimeTool : ITool
{
    public string Description => "Returns the current local date and time in UTC format.";
    public string Usage => "Input: None.";

    public Task<ToolResult> InvokeAsync(string input, Memory memory) => Log.Method(ctx =>
    {
        var result = DateTime.Now.ToString("u");
        ctx.Succeeded();
        return Task.FromResult(ToolResult.Success(result, memory, true));
    });
}

[IsConfigurable("GetFileNames")]
public class GetFileNamesTool : ITool
{
    public string Description => "Gets the names of supported source/text files in the specified directory recursively.";
    public string Usage => "Input: optional path (defaults to current directory).";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        var path = string.IsNullOrWhiteSpace(input) ? Directory.GetCurrentDirectory() : input;
        ctx.Append(Log.Data.Path, path);

        if (!Directory.Exists(path))
        {
            ctx.Failed($"Directory not found: {path}", Error.PathNotFound);
            return ToolResult.Failure($"ERROR: Directory not found: {path}", memory);
        }

        var supportedTypes = Engine.supportedFileTypes;
        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var list = string.Join("\n", files.Select(f => Path.GetRelativePath(path, f)));
        memory.AddContext("file_list", list);
        await Engine.AddContentToVectorStore(list, "file_list");
        ctx.Succeeded();
        return ToolResult.Success($"Found {files.Count} files:\n{list}", memory, true);
    });
}

[IsConfigurable("FileMetadata")]
public class FileMetadataTool : ITool
{
    public string Description => "Extracts key metrics (lines, words, size) and modification details for a given file. Useful for identifying complexity or recent changes.";
    public string Usage => "Input: path to a file.";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            return ToolResult.Failure($"ERROR: File not found: {input}", memory);
        }

        var fileInfo = new FileInfo(input);
        var size = fileInfo.Length;
        var modified = fileInfo.LastWriteTime;

        string[] lines = await File.ReadAllLinesAsync(input);
        int lineCount = lines.Length;
        int wordCount = lines.Sum(line => line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries).Length);
        int charCount = lines.Sum(line => line.Length);

        var sb = new StringBuilder();
        sb.AppendLine($"File: {input}");
        sb.AppendLine($"Size: {size:N0} bytes");
        sb.AppendLine($"Lines: {lineCount:N0}");
        sb.AppendLine($"Words: {wordCount:N0}");
        sb.AppendLine($"Characters: {charCount:N0}");
        sb.AppendLine($"Last Modified: {modified:u}");

        memory.AddContext("file_metadata", sb.ToString());
        await Engine.AddContentToVectorStore(sb.ToString(), "file_metadata");
        ctx.Succeeded();
        return ToolResult.Success(sb.ToString(), memory, true);
    });
}

[IsConfigurable("SummarizeFile")]
public class SummarizeFileTool : ITool
{
    public static readonly int MaxContentLength = 16000; // Maximum length of content to read
    public string Description => "Reads and summarizes the contents in a specified file. Ideal for analyzing or explaining supported files like text (.txt, .log, etc), source code, project, or markdown (.md) files.";
    public string Usage => "Input: path to a file.";

    public async Task<ToolResult> InvokeAsync(string input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            ctx.Failed($"File not found: {input}", Error.PathNotFound);
            return ToolResult.Failure($"ERROR: File not found: {input}", memory);
        }

        await Engine.AddFileToVectorStore(input);
        var content = File.ReadAllText(input);
        if (content.Length > MaxContentLength)
        {
            ctx.Append(Log.Data.Message, $"Content truncated to {MaxContentLength} characters.");
            content = content.Substring(0, MaxContentLength) + "\n... [truncated]";
        }

        memory.AddContext($"file_summary: {input}", content);
        await Engine.AddContentToVectorStore(content, $"file_summary: {input}");
        ctx.Succeeded();
        return ToolResult.Success($"Contents of {input}:\n{content}", memory, true);
    });
}

[IsConfigurable("ListFilesMatching")]
public class ListFilesMatchingTool : ITool
{
    public string Description => "Lists files in the current project whose full path matches a provided regular expression.";
    public string Usage => @"Input: .NET-style regex pattern to match file paths (e.g., '^Commands/.*\\.cs$', 'README', '.*Test.*')";

    public Task<ToolResult> InvokeAsync(string input, Memory memory) => Log.Method(ctx =>
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            ctx.Failed("Empty regex input.", Error.InvalidInput);
            return Task.FromResult(ToolResult.Failure("Please provide a regex pattern to match file paths.", memory));
        }

        Regex pattern;
        try
        {
            pattern = new Regex(input, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (Exception ex)
        {
            ctx.Failed("Invalid regex.", ex);
            return Task.FromResult(ToolResult.Failure($"Invalid regex: {ex.Message}", memory));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var baseDir = Directory.GetCurrentDirectory();
        var supportedExtensions = Engine.supportedFileTypes;

        var matching = Directory.GetFiles(baseDir, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(baseDir, f))
            .Where(f => pattern.IsMatch(f))
            .OrderBy(f => f)
            .ToList();

        stopwatch.Stop();
        var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
        Console.WriteLine($"{elapsedTime}ms required to find files that match '{input}'.");


        if (matching.Count == 0)
        {
            ctx.Failed("No files matched.", Error.ToolFailed);
            return Task.FromResult(ToolResult.Failure($"No files matched regex: `{input}`", memory));
        }

        var output = string.Join("\n", matching);
        memory.AddContext($"matched_files: {input}", output);

        ctx.Append(Log.Data.Count, matching.Count);
        ctx.Succeeded();
        return Task.FromResult(ToolResult.Success($"Matched {matching.Count} files:\n{output}", memory));
    });
}
