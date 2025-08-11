using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public static class Engine
{
    public static IVectorStore VectorStore = new SimpleVectorStore();
    public static IChatProvider? Provider = null;
    public static ITextChunker? TextChunker = null;
    public static Planner Planner = new Planner();

    public static List<string> SupportedFileTypes = new List<string>
    {
        ".bash", ".bat",
        ".c", ".cpp", ".cs", ".csproj", ".csv",
        ".h", ".html",
        ".ignore",
        ".js",
        ".log",
        ".md",
        ".py",
        ".sh", ".sln",
        ".ts", ".txt",
        ".xml",
        ".yml"
    };
    
    public static async Task AddFileToGraphStore(string path) => await AddContentItemsToGraphStore(new[] { (path, File.ReadAllText(path)) });

    public static async Task AddDirectoryToGraphStore(string path) => await AddContentItemsToGraphStore(ReadFilesFromDirectory(path));

    public static async Task AddContentItemsToGraphStore(IEnumerable<(string Name, string Content)> items) => await Log.MethodAsync(async ctx =>
    {
        try
        {
            ctx.OnlyEmitOnFailure();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var countItems = 0;
            var knownFiles = new StringBuilder();

            Console.WriteLine("Started processing files for RAG store...");

            foreach (var (name, content) in items)
            {
                Console.WriteLine($"Processing : {name}\n");
                knownFiles.AppendLine(name);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"Adding file '{name}' to RAG store... ");
                await ContextManager.AddGraphContent(content, name);
                Console.ResetColor();
                Console.WriteLine("Done.");
                countItems++;
            }

            await ContextManager.AddContent($"Known files:\n{knownFiles.ToString()}", "known_files");

            stopwatch.Stop();
            Console.WriteLine($"{stopwatch.ElapsedMilliseconds:N0}ms required to process {countItems} items.");
            ctx.Succeeded();
        }
        finally
        {
            Console.ResetColor();
        }
    });

    public static string BuildCommandTreeArt(IEnumerable<Command> commands, string indent = "", bool isLast = true, bool showText = true)
    {
        var sb = new StringBuilder();
        var commandList = commands.ToList();

        for (int i = 0; i < commandList.Count; i++)
        {
            var command = commandList[i];
            bool isLastCommand = i == commandList.Count - 1;
            if (showText)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{indent}{(isLast ? "└── " : "├── ")}");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(command.Name);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" - ");
            }

            var line = $"{indent}{(isLast ? "└── " : "├── ")}{command.Name} - ";
            var description = command.Description() ?? string.Empty;
            var maxWidth = Console.WindowWidth - line.Length - 4; // 4 for ellipses
            if (description.Length > maxWidth)
            {
                description = description.Substring(0, maxWidth) + "...";
            }
            if (showText)
            {
                Console.WriteLine(description);
                Console.ResetColor();
            }

            sb.Append(line);
            sb.AppendLine(description);
            if (command.SubCommands.Any())
            {
                var childIndent = indent + (isLastCommand ? "    " : "│   ");
                sb.Append(BuildCommandTreeArt(command.SubCommands, childIndent, isLastCommand, showText));
            }
        }

        return sb.ToString();
    }    

    public static async Task AddContentItemsToVectorStore(IEnumerable<(string Name, string Content)> items) => await Log.MethodAsync(async ctx =>
    {
        try
        {
            ctx.OnlyEmitOnFailure();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var countItems = 0;
            var knownFiles = new StringBuilder();

            foreach (var (name, content) in items)
            {
                knownFiles.AppendLine(name);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"Adding file '{name}' to RAG store... ");
                await ContextManager.AddContent(content, name);
                Console.ResetColor();
                Console.WriteLine("Done.");
                countItems++;
            }

            await ContextManager.AddContent($"Known files:\n{knownFiles.ToString()}", "known_files");

            stopwatch.Stop();
            Console.WriteLine($"{stopwatch.ElapsedMilliseconds:N0}ms required to process {countItems} items.");
            ctx.Succeeded();
        }
        finally
        {
            Console.ResetColor();
        }
    });

    public static async Task AddDirectoryToVectorStore(string path) => await AddContentItemsToVectorStore(ReadFilesFromDirectory(path));

    private static IEnumerable<(string Name, string Content)> ReadFilesFromDirectory(string path)
    {
        if (!Directory.Exists(path))
            yield break;

        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedFileTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            yield return (Path.GetRelativePath(Directory.GetCurrentDirectory(), file), File.ReadAllText(file));
        }
    }

    public static async Task AddZipFileToVectorStore(string zipPath) => await AddContentItemsToVectorStore(ReadFilesFromZip(zipPath));

    private static IEnumerable<(string Name, string Content)> ReadFilesFromZip(string zipPath)
    {
        if (!File.Exists(zipPath))
            yield break;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var ext = Path.GetExtension(entry.FullName);
            if (string.IsNullOrWhiteSpace(ext) || !SupportedFileTypes.Contains(ext, StringComparer.OrdinalIgnoreCase))
                continue;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            yield return ($"{zipPath}::{entry.FullName}", reader.ReadToEnd());
        }
    }

    public static async Task AddFileToVectorStore(string path) => await AddContentItemsToVectorStore(new[] { (path, File.ReadAllText(path)) });

    public static void SetTextChunker(string chunkerName) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Program.config.RagSettings.ChunkingStrategy = chunkerName;
        ctx.Append(Log.Data.Name, chunkerName);
        Program.serviceProvider.ThrowIfNull("Service provider is not initialized.");
        if (Program.Chunkers.TryGetValue(chunkerName, out var type))
        {
            TextChunker = (ITextChunker?)Program.serviceProvider?.GetService(type);
            ctx.Append(Log.Data.Message, $"Using chunker: {type.Name} as implementation for {chunkerName}");
        }
        else if (Program.Chunkers.Count > 0)
        {
            var first = Program.Chunkers.First();
            TextChunker = (ITextChunker?)Program.serviceProvider?.GetService(first.Value);
            ctx.Append(Log.Data.Message, $"{chunkerName} not found, using default chunker: {first.Key}");
        }
        else
        {
            TextChunker = null!;
            ctx.Failed($"No chunkers available. Please check your configuration or add a chunker.", Error.ChunkerNotConfigured);
            return;
        }
        ctx.Succeeded(TextChunker != null);
    });

    public static void SetProvider(string providerName) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Program.config.Provider = providerName;
        ctx.Append(Log.Data.Provider, providerName);
        Program.serviceProvider.ThrowIfNull("Service provider is not initialized.");
        if (Program.Providers.TryGetValue(providerName, out var type))
        {
            Provider = (IChatProvider?)Program.serviceProvider?.GetService(type);
        }
        else if (Program.Providers.Count > 0)
        {
            var first = Program.Providers.First();
            Provider = (IChatProvider?)Program.serviceProvider?.GetService(first.Value);
            ctx.Append(Log.Data.Message, $"{providerName} not found, using default provider: {first.Key}");
        }
        else
        {
            Provider = null;
            ctx.Failed($"No providers available. Please check your configuration or add a provider.", Error.ProviderNotConfigured);
            return;
        }
        ctx.Append(Log.Data.ProviderSet, Provider != null);
        ctx.Succeeded();
    });

    public static async Task<string?> SelectModelAsync() => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        if (Provider == null)
        {
            Console.WriteLine("Provider is not set.");
            ctx.Failed("Provider is not set.", Error.ProviderNotConfigured);
            return null;
        }

        var models = await Provider.GetAvailableModelsAsync();
        if (models == null || models.Count == 0)
        {
            Console.WriteLine("No models available.", Error.ModelNotFound);
            ctx.Failed("No models available.", Error.ModelNotFound);
            return null;
        }

        var selected = User.RenderMenu("Available models:", models, models.IndexOf(Program.config.Model));
        ctx.Append(Log.Data.Model, selected ?? "<nothing>");
        ctx.Succeeded();
        return selected;
    });

    public static async Task<(string result, Context Context)> PostChatAsync(Context history)
    {
        Provider.ThrowIfNull("Provider is not set.");
        var input = history.Messages().LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        await ContextManager.InvokeAsync(input, history);
        return await Planner.PostChatAsync(history);
    }
}