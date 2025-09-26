using System;


[ExampleText("""
{ "Name": "<thread_name>", "Description": "<optional_description>" }

Where:
  * <thread_name> is required and is a succinct and unique name of the chat thread, e.g., "Project Alpha Discussion"
  * <optional_description> is optional and is a brief description of the thread's purpose or topic, e.g., "Discussion about Project Alpha milestones"
""")]
[UserManaged("Chat threads", "History of previous chats.")]
public sealed class ChatThread
{
    [UserKey] public string Name { get; set; } = "";       // unique user-facing name

    [UserField(required: false, FieldKind = UiFieldKind.Text)]
    public string Description { get; set; } = ""; // optional description

    [UserField(required: false, hidden: true)]
    public string LastUsedUtc { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); // for sorting

    public override string ToString() => $"{Name} ({LastUsedUtc}): {Description}";
}
