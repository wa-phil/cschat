using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
public enum Roles
{
    System,
    User,
    Assistant,
    Tool,
}
public class ChatMessage
{
    public Roles Role { get; set; }
    public string Content { get; set; } = string.Empty; // Ensure non-nullable property is initialized
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

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
    Task<string> PostChatAsync(Memory history, float temperature);
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

public record ToolResult(bool Succeeded, string Response, Memory Memory, bool Summarize = true, string? Error = null)
{
    public static ToolResult Success(string response, Memory memory, bool summarize = true) =>
        new(true, response, memory, summarize, null);

    public static ToolResult Failure(string errorMessage, Memory memory) =>
        new(false, $"ERROR: {errorMessage}", memory, Summarize: false, Error: errorMessage);
}

public interface ITool
{
    string Description { get; }
    string Usage { get; } // Example: "Add(a, b)"
    Type InputType { get; } // The type expected for the input parameter
    Task<ToolResult> InvokeAsync(object input, Memory memory); // Returns response text, and optionally modifies memory for context
}
