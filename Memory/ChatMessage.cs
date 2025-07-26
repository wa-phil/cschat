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