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
public class ProviderNameAttribute : Attribute
{
    public string Name { get; }
    public ProviderNameAttribute(string name)
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

public interface IVectorStore
{
    void Add(string filePath, List<(int Offset, string Chunk, float[] Embedding)> entries);
    void Clear();
    List<(string FilePath, int Offset, string Content)> Search(float[] queryEmbedding, int topK = 3);
    bool IsEmpty { get; }
}