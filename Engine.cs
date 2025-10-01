using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

public static class Engine
{
    private static readonly object _consoleLock = new();
    public static IVectorStore VectorStore = new SimpleVectorStore();
    public static IChatProvider? Provider = null;
    public static ITextChunker? TextChunker = null;
    public static Planner Planner = new Planner();

    // Backing list retained for performance; kept in sync from UserManagedData RagFileType entries.
    public static List<string> SupportedFileTypes = new List<string>();

    public static void RefreshSupportedFileTypesFromUserManaged()
    {
        try
        {
            var items = Program.userManagedData.GetItems<RagFileType>()
                .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Extension))
                .Select(r => r.Extension.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
            SupportedFileTypes = items;
        }
        catch
        {
            // On early startup (before userManagedData connected) fall back to config
            SupportedFileTypes = Program.config.RagSettings.SupportedFileTypes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        }
    }
    
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

            Program.ui.WriteLine("Started processing files for RAG store...");

            foreach (var (name, content) in items)
            {
                Program.ui.WriteLine($"Processing : {name}\n");
                knownFiles.AppendLine(name);
                Program.ui.ForegroundColor = ConsoleColor.DarkGray;
                Program.ui.Write($"Adding file '{name}' to RAG store... ");
                await ContextManager.AddGraphContent(content, name);
                Program.ui.ResetColor();
                Program.ui.WriteLine("Done.");
                countItems++;
            }

            await ContextManager.AddContent($"Known files:\n{knownFiles.ToString()}", "known_files");

            stopwatch.Stop();
            Program.ui.WriteLine($"{stopwatch.ElapsedMilliseconds:N0}ms required to process {countItems} items.");
            ctx.Succeeded();
        }
        finally
        {
            Program.ui.ResetColor();
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
                Program.ui.ForegroundColor = ConsoleColor.DarkGray;
                Program.ui.Write($"{indent}{(isLast ? "└── " : "├── ")}");
                Program.ui.ForegroundColor = ConsoleColor.Green;
                Program.ui.Write(command.Name);
                Program.ui.ForegroundColor = ConsoleColor.DarkGray;
                Program.ui.Write(" - ");
            }

            var line = $"{indent}{(isLast ? "└── " : "├── ")}{command.Name} - ";
            var description = command.Description() ?? string.Empty;
            var maxWidth = Program.ui.Width - line.Length - 4; // 4 for ellipses
            if (description.Length > maxWidth)
            {
                description = description.Substring(0, maxWidth) + "...";
            }
            if (showText)
            {
                Program.ui.WriteLine(description);
                Program.ui.ResetColor();
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
        ctx.OnlyEmitOnFailure();

        var list = items?.ToList() ?? new();
        using var cts = new CancellationTokenSource();

        var (results, failures, canceled) =
            await AsyncProgress.For("Adding content to RAG")
                .WithCancellation(cts)
                .Run<(string Name, string Content), bool>(
                    items: () => list,
                    nameOf: x => x.Name,
                    processAsync: async (tuple, pi, ct) =>
                    {
                        await ContextManager.AddContent(
                            tuple.Content, tuple.Name, ct,
                            setTotalSteps: total => pi.SetTotal(total),
                            advance: (delta, note) => pi.Advance(delta, note)
                        );
                        return true; // returning a result value for the caller
                    });

        // optional: persist "known files" context if not canceled
        if (!canceled && list.Count > 0)
        {
            await ContextManager.AddContent("Known files:\n" + string.Join("\n", list.Select(x => x.Name).OrderBy(x => x)), "known_files");
        }

        // If you want an extra assistant message beyond the artifact:
        if (failures.Count > 0)
        {
            Program.ui.RenderChatMessage(new ChatMessage {
                Role = Roles.Assistant,
                Content = $"Finished with {failures.Count} failures."
            });
        }
        ctx.Succeeded(0 == failures.Count);
    });

    public static async Task AddDirectoryToVectorStore(string path) => await AddContentItemsToVectorStore(ReadFilesFromDirectory(path));

    private static IEnumerable<(string Name, string Content)> ReadFilesFromDirectory(string path)
    {
        if (!Directory.Exists(path))
            yield break;
        RefreshSupportedFileTypesFromUserManaged();
        var rftItems = new List<RagFileType>();
        try { rftItems = Program.userManagedData.GetItems<RagFileType>(); } catch { }
        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => ShouldIncludeFile(f, rftItems));

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
        RefreshSupportedFileTypesFromUserManaged();
        var rftItems = new List<RagFileType>();
        try { rftItems = Program.userManagedData.GetItems<RagFileType>(); } catch { }
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var ext = Path.GetExtension(entry.FullName);
            if (!ShouldIncludePath(ext, entry.FullName, rftItems))
                continue;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            yield return ($"{zipPath}::{entry.FullName}", reader.ReadToEnd());
        }
    }

    public static async Task AddFileToVectorStore(string path) => await AddContentItemsToVectorStore(new[] { (path, File.ReadAllText(path)) });

    private static bool ShouldIncludeFile(string filePath, List<RagFileType> items)
    {
        var ext = Path.GetExtension(filePath);
        return ShouldIncludePath(ext, filePath, items);
    }

    private static bool ShouldIncludePath(string ext, string relativePath, List<RagFileType> items)
    {
        if (string.IsNullOrWhiteSpace(ext)) return false;
        var match = items.FirstOrDefault(i => i.Enabled && i.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase));
        if (match == null) return false;
        // If include patterns exist, path must match one
        if (match.Include.Any())
        {
            bool included = match.Include.Any(p => SafeRegexIsMatch(relativePath, p));
            if (!included) return false;
        }
        // Exclude patterns override
        if (match.Exclude.Any(p => SafeRegexIsMatch(relativePath, p))) return false;
        return true;
    }

    private static bool SafeRegexIsMatch(string input, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        try { return Regex.IsMatch(input, pattern); } catch { return false; }
    }

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
            Program.ui.WriteLine("Provider is not set.");
            ctx.Failed("Provider is not set.", Error.ProviderNotConfigured);
            return null;
        }

        var models = await Provider.GetAvailableModelsAsync();
        if (models == null || models.Count == 0)
        {
            Program.ui.WriteLine($"No models available: {Error.ModelNotFound.ToString()}");
            ctx.Failed("No models available.", Error.ModelNotFound);
            return null;
        }

        var selected = Program.ui.RenderMenu("Available models:", models, models.IndexOf(Program.config.Model));
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