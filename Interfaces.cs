using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using ModelContextProtocol.Protocol;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class IsConfigurable : Attribute
{
    public string Name { get; }
    public IsConfigurable(string name) => Name = name;
}

// Attribute to provide example text for input types, used for generating input prompts
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ExampleText : Attribute
{
    public string Text { get; }
    public ExampleText(string text) => Text = text;
}

public interface IChatProvider
{
    Task<List<string>> GetAvailableModelsAsync();
    Task<string> PostChatAsync(Context history, float temperature);
}

public interface IEmbeddingProvider
{
    Task<float[]> GetEmbeddingAsync(string text);
}

public record SearchResult
{
    public float Score;
    required public string Reference;
    required public string Content;
}

public interface IVectorStore
{
    void Add(List<(string Reference, string Chunk, float[] Embedding)> entries);
    void Clear();
    List<SearchResult> Search(float[] queryEmbedding, int topK = 3);
    List<SearchResult> SearchReferences(string reference);
    bool IsEmpty { get; }
    int Count { get; }

    List<(string Reference, string Content)> GetEntries(Func<string, string, bool>? filter = null);
}

public interface ITextChunker
{
    List<(string Reference, string Content)> ChunkText(string path, string text);
}

public record ToolResult(bool Succeeded, string Response, Context context, string? Error = null)
{
    public static ToolResult Success(string response, Context ctx) =>
        new(true, response, ctx, null);

    public static ToolResult Failure(string errorMessage, Context ctx) =>
        new(false, $"ERROR: {errorMessage}", ctx, Error: errorMessage);
}

public interface ITool
{
    string Description { get; }
    string Usage { get; } // Example: "Add(a, b)"
    Type InputType { get; } // The type expected for the input parameter
    string InputSchema { get; }
    Task<ToolResult> InvokeAsync(object input, Context Context); // Returns response text, and optionally modifies Context for context
}
