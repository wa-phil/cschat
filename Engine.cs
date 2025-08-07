using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public static class Engine
{
    public static IVectorStore VectorStore = new SimpleVectorStore();
    public static IChatProvider? Provider = null;
    public static ITextChunker? TextChunker = null;
    public static Planner Planner = new Planner();

    public static List<string> supportedFileTypes = new List<string>
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

    public static async Task AddDirectoryToVectorStore(string path) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Path, path);
        if (!Directory.Exists(path))
        {
            ctx.Failed($"Directory '{path}' does not exist.", Error.DirectoryNotFound);
            return;
        }
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedFileTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        await Task.WhenAll(files.Select(f => AddFileToVectorStore(f)));
        Console.WriteLine($"Added {files.Count} files to vector store from directory '{path}'.");

        // Aggregate the list of relative file names into a new line delimited string and add it to the vector store.
        var knownFiles = files.Aggregate(new StringBuilder(), (sb, f) => sb.AppendLine(Path.GetRelativePath(Directory.GetCurrentDirectory(), f))).ToString().TrimEnd();
        var embeddings = new List<(string Reference, string Chunk, float[] Embedding)>();
        var chunks = TextChunker!.ChunkText("known files", knownFiles);

        await Task.WhenAll(chunks.Select(async chunk =>
            embeddings.Add((
                Reference: chunk.Reference,
                Chunk: chunk.Content,
                Embedding: await (Engine.Provider as IEmbeddingProvider)!.GetEmbeddingAsync(chunk.Content)
            ))
        ));
        Engine.VectorStore.Add(embeddings);
        Console.WriteLine($"Added directory index to vector store in {chunks.Count} chunks.");

        stopwatch.Stop();
        var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
        Console.WriteLine($"{elapsedTime}ms required to process files in '{path}'.");

        ctx.Append(Log.Data.Count, files.Count);
        ctx.Succeeded();
    });

    public static async Task AddFileToVectorStore(string path) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        // start a timer to measure the time taken to add the file
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await ContextManager.AddContent(File.ReadAllText(path), path);
        ctx.Append(Log.Data.FilePath, path);
        stopwatch.Stop();
        var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
        Console.WriteLine($"{elapsedTime}ms required to read file '{path}' contents.");
        ctx.Succeeded();
    });

    public static async Task AddFileToGraphStore(string path) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        // start a timer to measure the time taken to add the file
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await ContextManager.AddGraphContent(File.ReadAllText(path), path);
        ctx.Append(Log.Data.FilePath, path);
        stopwatch.Stop();
        var elapsedTime = stopwatch.ElapsedMilliseconds.ToString("N0");
        Console.WriteLine($"{elapsedTime}ms required to read file '{path}' contents.");
        ctx.Succeeded();
    });       

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
        var input = history.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        await ContextManager.InvokeAsync(input, history);
        return await Planner.PostChatAsync(history);
    }
}