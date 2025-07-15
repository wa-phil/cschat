using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

// Input classes for tools
[ExampleText("""
{ "Path": "<path_to_file_or_directory>" }

Where <path_to_file_or_directory> is either a full, or a relative path to a file on the local filesystem.
If the path is empty, the current working directory will be used.
""")]
public class PathInput
{
    public string Path { get; set; } = string.Empty;
}

[ExampleText("""
{ "Pattern": "<regex_pattern>" }

Where <regex_pattern> is a valid .NET regular expression pattern.
""")]
public class RegexInput  
{
    public string Pattern { get; set; } = string.Empty;
}

[ExampleText("""
{ "Path": "<path_to_file_or_directory>", "Pattern": "<regex_pattern>" }

Where 
<path_to_file_or_directory> is either a full, or a relative path to a file on the local filesystem.  If the path is empty, the current working directory will be used.
<regex_pattern> is a valid .NET regular expression pattern.
""")]
class PathAndRegexInput
{
    public string Path { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
}

[ExampleText("{ null }")]
public class NoInput
{
    // Empty class for tools that don't require input
}

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

    internal static async Task<ToolResult> InvokeInternalAsync(string toolName, object toolInput, Memory memory, string userInput) =>
        await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Name, toolName);
        ctx.Append(Log.Data.ToolInput, toolInput?.ToString() ?? "<null>");

        if (_tools == null || string.IsNullOrEmpty(toolName) || !_tools.ContainsKey(toolName))
        {
            ctx.Failed($"Tool '{toolName}' is not registered.", Error.ToolNotAvailable);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' is not registered.", memory);
        }

        if (null == toolInput)
        {
            ctx.Failed($"Tool '{toolName}' requires input, but received null.", Error.InvalidInput);
            return ToolResult.Failure($"ERROR: Tool '{toolName}' requires input, but received null.", memory);
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
    
    public static async Task<string> InvokeToolAsync(string toolName, object toolInput, Memory memory, string lastUserInput)
    {
        var result = await InvokeInternalAsync(toolName, toolInput, memory, lastUserInput);
        return result.Response;
    }
}

[IsConfigurable("Calculator")]
public class CalculatorTool : ITool
{
    public string Description => "Evaluates basic math expressions.";
    public string Usage => "Provide a mathematical expression to evaluate. Supports basic arithmetic operations, such as addition (+), subtraction (-), multiplication (*), division (/), and parentheses (()).";
    public Type InputType => typeof(string);

    public Task<ToolResult> InvokeAsync(object input, Memory memory) => Log.Method(ctx =>
    {
        try
        {
            var stringInput = input as string ?? throw new ArgumentException("Expected string as input");
            var result = new System.Data.DataTable().Compute(stringInput, null);
            ctx.Succeeded();
            return Task.FromResult(ToolResult.Success(result?.ToString() ?? string.Empty, memory, true));
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error evaluating expression", ex);
            return Task.FromResult(ToolResult.Failure($"Error evaluating expression", memory));
        }
    });
}

[IsConfigurable("datetime_current")]
public class datetime_current : ITool
{
    public string Description => "Returns the current local date and time in UTC format.";
    public string Usage => "No input required. Simply returns the current date and time.";
    public Type InputType => typeof(NoInput);

    public Task<ToolResult> InvokeAsync(object input, Memory memory) => Log.Method(ctx =>
    {
        var result = DateTime.Now.ToString("u");
        ctx.Succeeded();
        return Task.FromResult(ToolResult.Success(result, memory, true));
    });
}

[IsConfigurable("file_list")]
public class file_list : ITool
{
    public string Description => "Gets the names of supported source/text files in the specified directory recursively.";
    public string Usage => "Provide a directory path to list files from, or leave empty to use current directory. Only supported file types will be listed.";
    public Type InputType => typeof(PathInput);

    public async Task<ToolResult> InvokeAsync(object input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        var pathInput = input as PathInput ?? new PathInput();
        var path = string.IsNullOrWhiteSpace(pathInput.Path) ? Directory.GetCurrentDirectory() : pathInput.Path;
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
        return ToolResult.Success($"Found {files.Count} files:\n{list}", memory, false);
    });
}

[IsConfigurable("file_metadata")]
public class file_metadata : ITool
{
    public string Description => "Extracts key metrics (lines, words, size) and modification details for a given file. Useful for identifying complexity or recent changes.";
    public string Usage => "Provide the full or relative path to a file to analyze its metadata including size, line count, word count, and modification time.";
    public Type InputType => typeof(PathInput);

    public async Task<ToolResult> InvokeAsync(object input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        var pathInput = input as PathInput ?? throw new ArgumentException("Expected PathInput");
        if (string.IsNullOrWhiteSpace(pathInput.Path) || !File.Exists(pathInput.Path))
        {
            return ToolResult.Failure($"ERROR: File not found: {pathInput.Path}", memory);
        }

        var fileInfo = new FileInfo(pathInput.Path);
        var size = fileInfo.Length;
        var modified = fileInfo.LastWriteTime;

        string[] lines = await File.ReadAllLinesAsync(pathInput.Path);
        int lineCount = lines.Length;
        int wordCount = lines.Sum(line => line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries).Length);
        int charCount = lines.Sum(line => line.Length);

        var sb = new StringBuilder();
        sb.AppendLine($"File: {pathInput.Path}");
        sb.AppendLine($"Size: {size:N0} bytes");
        sb.AppendLine($"Lines: {lineCount:N0}");
        sb.AppendLine($"Words: {wordCount:N0}");
        sb.AppendLine($"Characters: {charCount:N0}");
        sb.AppendLine($"Last Modified: {modified:u}");

        memory.AddContext("file_metadata", sb.ToString());
        await Engine.AddContentToVectorStore(sb.ToString(), "file_metadata");
        ctx.Succeeded();
        return ToolResult.Success(sb.ToString(), memory, false);
    });
}

[IsConfigurable("summarize_file")]
public class summarize_file : ITool
{
    public static readonly int MaxContentLength = 16000; // Maximum length of content to read
    public string Description => "Reads and summarizes the contents in a specified file. Ideal for analyzing or explaining supported files like text (.txt, .log, etc), source code, project, or markdown (.md) files.";
    public string Usage => "Provide the path to a file to read and summarize. Large files will be truncated for processing.";
    public Type InputType => typeof(PathInput);

    public async Task<ToolResult> InvokeAsync(object input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var pathInput = input as PathInput ?? throw new ArgumentException("Expected PathInput");
        if (string.IsNullOrWhiteSpace(pathInput.Path) || !File.Exists(pathInput.Path))
        {
            ctx.Failed($"File not found: {pathInput.Path}", Error.PathNotFound);
            return ToolResult.Failure($"ERROR: File not found: {pathInput.Path}", memory);
        }

        await Engine.AddFileToVectorStore(pathInput.Path);
        var content = File.ReadAllText(pathInput.Path);
        if (content.Length > MaxContentLength)
        {
            ctx.Append(Log.Data.Message, $"Content truncated to {MaxContentLength} characters.");
            content = content.Substring(0, MaxContentLength) + "\n... [truncated]";
        }

        memory.AddContext($"file_summary: {pathInput.Path}", content);
        await Engine.AddContentToVectorStore(content, $"file_summary: {pathInput.Path}");
        ctx.Succeeded();
        return ToolResult.Success($"Contents of {pathInput.Path}:\n{content}", memory, true);
    });
}

[IsConfigurable("grep_files")]
public class grep_files : ITool
{
    public string Description => "Searches for a text pattern in all files in the current directory.";
    public string Usage => "Provide a .NET regex pattern to search for across all supported files. Returns matching lines with context.";
    public Type InputType => typeof(PathAndRegexInput);

    public record GrepResult(bool Succeeded, int Matches, string Results)
    {
        public static GrepResult Failure(string message) => new(false, 0, message);
        public static GrepResult Success(int matches, string results) => new(true, matches, results);
    }

    public async Task<ToolResult> InvokeAsync(object input, Memory memory) => await Log.MethodAsync(async ctx =>
    {
        var regexInput = input as PathAndRegexInput ?? throw new ArgumentException("Expected RegexInput");
        if (string.IsNullOrWhiteSpace(regexInput.Pattern))
        {
            ctx.Failed("Empty search input.", Error.InvalidInput);
            return ToolResult.Failure("Please provide a text pattern to search for.", memory);
        }

        var grepResult = await GrepFilesAsync(regexInput.Pattern, string.IsNullOrEmpty(regexInput.Path) ? Directory.GetCurrentDirectory() : regexInput.Path);

        memory.AddContext($"grep_files({regexInput.Pattern})", string.Join("\n", grepResult.Results));
        ctx.Succeeded(grepResult.Succeeded);
        return ToolResult.Success($"{(grepResult.Succeeded?"Succeeded in finding":"Failed. Found")} {grepResult.Matches} files matching `{regexInput.Pattern}`:\n{grepResult.Results}", memory, false);
    });

    public static async Task<GrepResult> GrepFilesAsync(string regExPattern, string path = ".") => await Log.MethodAsync(async ctx =>
    {
        const int MaxBlockAtMatch = 100; // Maximum number of lines of text to return at each match
        ctx.Append(Log.Data.Input, regExPattern);
        ctx.Append(Log.Data.Path, path);
        if (!Directory.Exists(path))
        {
            ctx.Failed($"Directory '{path}' does not exist.", Error.DirectoryNotFound);
            return GrepResult.Failure($"Directory '{path}' does not exist.");
        }
        if (string.IsNullOrWhiteSpace(regExPattern))
        {
            ctx.Failed("Regular expression pattern cannot be empty.", Error.InvalidInput);
            return GrepResult.Failure("Regular expression pattern cannot be empty.");
        }
        var pattern = new Regex(regExPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => Engine.supportedFileTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        // return all the line numbers, and the at most MaxBlockAtMatch lines of text after the line of each match as a tuple of (line number, text block)
        var results = new List<(string fileName, int LineNumber, string TextBlock)>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);
            var content = await File.ReadAllTextAsync(file);
            var matches = pattern.Matches(content);

            foreach (Match match in matches)
            {
                var startLine = content.Substring(0, match.Index).Count(c => c == '\n') + 1; // 1-based line number
                var endLine = startLine + content.Substring(match.Index, match.Length).Count(c => c == '\n');
                var lines = content.Split('\n').Skip(startLine - 1).Take(Math.Min(MaxBlockAtMatch, endLine - startLine +1)).ToArray();
                var block = string.Join("\n", lines);
                // TODO: consider grouping matched lines that are close to each other into a single block
                results.Add((fileName: relativePath, LineNumber: startLine, TextBlock: block));
            }
            if (matches.Count > 0)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine($"Matched: {relativePath}");
                await Engine.AddContentToVectorStore(content, relativePath);
                stopwatch.Stop();
                var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
                Console.WriteLine($"{elapsedTime}ms required to read file '{file}' contents.");

                ctx.Append(Log.Data.FilePath, relativePath);
            }
        }
        if (results.Count == 0)
        {
            var message = $"No matches found for pattern '{regExPattern}' in directory '{path}'.";
            ctx.Failed(message, Error.NoMatchesFound);
            return GrepResult.Failure(message);
        }

        var resultsString = string.Join("\n", results
            .OrderBy(r => r.fileName)
            .ThenBy(r => r.LineNumber)
            .Select(r => $"--- {r.fileName} line number: {r.LineNumber} ---\n{r.TextBlock}\n--- end match ---")
            .ToList());

        await Engine.AddContentToVectorStore(resultsString, $"grep_files({regExPattern})");

        ctx.Append(Log.Data.Count, results.Count);
        ctx.Succeeded();
        return GrepResult.Success(results.Count, $"grep_files({regExPattern}):\n{resultsString}\n");
    });
}

[IsConfigurable("find_file")]
public class find_file : ITool
{
    public string Description => "Lists files in the current project whose full path matches a provided regular expression.";
    public string Usage => "Provide a .NET regex pattern to match file paths. Only files with supported extensions will be searched.";
    public Type InputType => typeof(PathAndRegexInput);

    public Task<ToolResult> InvokeAsync(object input, Memory memory) => Log.Method(ctx =>
    {
        var findInput = input as PathAndRegexInput ?? throw new ArgumentException("Expected PathAndRegexInput");
        if (string.IsNullOrWhiteSpace(findInput.Pattern))
        {
            ctx.Failed("Empty regex input.", Error.InvalidInput);
            return Task.FromResult(ToolResult.Failure("Please provide a regex pattern to match file paths.", memory));
        }

        Regex pattern;
        try
        {
            pattern = new Regex(findInput.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (Exception ex)
        {
            ctx.Failed("Invalid regex.", ex);
            return Task.FromResult(ToolResult.Failure($"Invalid regex: {ex.Message}", memory));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var baseDir = string.IsNullOrEmpty(findInput.Path) ? Directory.GetCurrentDirectory() : findInput.Path;
        var supportedExtensions = Engine.supportedFileTypes;

        var matching = Directory.GetFiles(baseDir, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(baseDir, f))
            .Where(f => pattern.IsMatch(f))
            .OrderBy(f => f)
            .ToList();

        stopwatch.Stop();
        var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
        Console.WriteLine($"{elapsedTime}ms required to find files that match '{findInput.Pattern}'.");


        if (matching.Count == 0)
        {
            ctx.Failed("No files matched.", Error.ToolFailed);
            return Task.FromResult(ToolResult.Failure($"No files matched regex: `{findInput.Pattern}`", memory));
        }

        var output = $"find_files({findInput.Pattern}):\n{string.Join("\n", matching)}\n";
        memory.AddContext($"matched_files: {findInput.Pattern}", output);

        ctx.Append(Log.Data.Count, matching.Count);
        ctx.Succeeded();
        return Task.FromResult(ToolResult.Success(output, memory, false));
    });
}
