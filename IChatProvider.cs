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
    public string Content { get; set; } = string.Empty; // Ensure non-nullable property is initialized
}

public class Memory
{
    protected List<ChatMessage> _systemMessages = new List<ChatMessage>();
    protected List<ChatMessage> _messages = new List<ChatMessage>();
    public Memory(string systemPrompt) => AddSystemMessage(systemPrompt);

    public Memory(IEnumerable<ChatMessage> messages)
    {
        foreach (var msg in messages)
        {
            if (msg.Role == Roles.System)
                _systemMessages.Add(msg);
            else
                _messages.Add(msg);
        }
    }
    
    public IEnumerable<ChatMessage> Messages
    {
        get
        {
            var result = new List<ChatMessage>(_systemMessages);
            result.AddRange(_messages);
            return result;
        }
    }

    public void Clear() 
    {
        _systemMessages.Clear();
        _messages.Clear();
    }

    public void AddUserMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.User, Content = content });
    public void AddAssistantMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.Assistant, Content = content });
    public void AddSystemMessage(string content) => _systemMessages.Add(new ChatMessage { Role = Roles.System, Content = content });
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
