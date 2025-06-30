using System.Reflection;
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
    public string Content { get; set; }
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
    Task<List<string>> GetAvailableModelsAsync(Config config);
    Task<string> PostChatAsync(Config config, List<ChatMessage> history);
}
