using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
public enum Roles
{
    System,
    User,
    Assistant
}
public class ChatMessage
{
    public Roles Role { get; set; }
    public string Content { get; set; } = string.Empty; // Ensure non-nullable property is initialized
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class IsConfigurable : Attribute
{
    public string Name { get; }
    public IsConfigurable(string name)
    {
        Name = name;
    }
}

public interface IChatProvider
{
    Task<List<string>> GetAvailableModelsAsync();
    Task<string> PostChatAsync(Memory history);
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

public record ToolResult (bool Summarize, string ResponseText, Memory Memory);

public interface ITool
{
    string Description { get; }
    string Usage { get; } // Example: "Add(a, b)"
    Task<ToolResult> InvokeAsync(string input, Memory memory); // Returns response text, and optionally modifies memory for context
}
