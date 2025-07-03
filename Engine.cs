using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public static class Engine
{
    public static IVectorStore VectorStore = new InMemoryVectorStore();
    public static IChatProvider? Provider = null; // Allow nullable field

    public static List<string> supportedFileTypes = new List<string>
    {
        ".bash", ".bat",
        ".c", ".cpp", ".cs", ".csproj", ".csv",
        ".h", ".html",
        ".ignore",
        ".js", ".json",
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

        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedFileTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        await Task.WhenAll(files.Select(f => AddFileToVectorStore(f)));
        ctx.Append(Log.Data.Count, files.Count);
        ctx.Succeeded();
    });

    public static async Task AddFileToVectorStore(string path) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.FilePath, path);
        IEmbeddingProvider? embeddingProvider = Engine.Provider as IEmbeddingProvider;
        embeddingProvider.ThrowIfNull("Current configured provider does not support embeddings.");

        var embeddings = new List<(int, string, float[])>();
        var chunks = ChunkText(File.ReadAllText(path));
        ctx.Append(Log.Data.Count, chunks.Count);

        // Use async-await pattern for better performance
        await Task.WhenAll(chunks.Select(async chunk => embeddings.Add((chunk.Offset, chunk.Content, await embeddingProvider!.GetEmbeddingAsync(chunk.Content)))));

        Engine.VectorStore.Add(path, embeddings);
        ctx.Succeeded();
    });

    public static List<(int Offset, string Content)> ChunkText(string text, int chunkSize = 500, int overlap = 50)
    {
        var chunks = new List<(int, string)>();
        for (int i = 0; i < text.Length; i += chunkSize - overlap)
        {
            chunks.Add((i, text.Substring(i, Math.Min(chunkSize, text.Length - i))));
        }
        return chunks;
    }

    public static async Task<string> GetRagQueryAsync(string userMessage, Memory history) => await Log.MethodAsync(async ctx =>
    {
        Provider.ThrowIfNull("Provider is not set. Please configure a provider before making requests.");

        var prompt = $"""
            Based on this user message:
            "{userMessage}"

            Generate a concise list of one or more keywords that are to be used to retrieve relevant document fragments from a semantic search-based knowledge base. 
            If no external context is needed, respond only with: "NO_RAG"
        """;

        var query = new Memory(new[] {
            new ChatMessage { Role = Roles.System, Content = "You are a query generator for knowledge retrieval." },
            new ChatMessage { Role = Roles.User, Content = prompt }
        });

        var response = await Provider!.PostChatAsync(query); // be sure to use the provider directly and don't go through the front door! :O
        ctx.Append(Log.Data.Result, response);
        var result = response.Trim() == "NO_RAG" ? string.Empty : response.Trim();
        ctx.Succeeded();
        return result;
    });

    public static void SetProvider(string providerName) => Log.Method(ctx =>
    {
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

    public static async Task<string> PostChatAsync(Memory history)
    {
        Provider.ThrowIfNull("Provider is not set. Please configure a provider before making requests.");

        if (null != VectorStore && !VectorStore.IsEmpty)
        {
            var embeddingProvider = Provider! as IEmbeddingProvider;
            embeddingProvider.ThrowIfNull("Current configured provider does not support embeddings.");

            var query = await GetRagQueryAsync(history.Messages.LastOrDefault()?.Content ?? string.Empty, history);
            if (!string.IsNullOrEmpty(query))
            {
                float[]? queryEmbedding = await embeddingProvider!.GetEmbeddingAsync(query);
                if (queryEmbedding != null)
                {
                    var results = VectorStore.Search(queryEmbedding, 3);
                    foreach (var result in results)
                    {
                        history.AddSystemMessage($"Context from file_path:\"{result.FilePath}\" offset:\"{result.Offset}\"\n```\n{result.Content}\n```\n");
                    }
                    history.AddSystemMessage($"When referring to the provided context in your answer, explicitly state from which file and offset you are referencing in the form 'as per file:[file_path] at offset:[offset], [your answer]'.");
                }
            }
        }

        return await Provider!.PostChatAsync(history);
    }
}