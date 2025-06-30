using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

public class ChatMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}

public interface IChatProvider
{
    string Name { get; }
    Task<List<string>> GetAvailableModelsAsync(Config config);
    Task<string> PostChatAsync(Config config, List<ChatMessage> history, string input);
}
