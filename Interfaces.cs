using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

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
    bool IsEmpty { get; }
    int Count { get; }
}

public interface ITextChunker
{
    List<(string Reference, string Content)> ChunkText(string path, string text);
}

public record ToolResult(bool Succeeded, string Response, Context Context, bool Summarize = true, string? Error = null)
{
    public static ToolResult Success(string response, Context Context, bool summarize = true) =>
        new(true, response, Context, summarize, null);

    public static ToolResult Failure(string errorMessage, Context Context) =>
        new(false, $"ERROR: {errorMessage}", Context, Summarize: false, Error: errorMessage);
}

public interface ITool
{
    string Description { get; }
    string Usage { get; } // Example: "Add(a, b)"
    Type InputType { get; } // The type expected for the input parameter
    Task<ToolResult> InvokeAsync(object input, Context Context); // Returns response text, and optionally modifies Context for context
}
