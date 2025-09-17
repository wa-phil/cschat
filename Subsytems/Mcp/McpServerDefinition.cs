using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

[UserManaged("McpServers", "MCP server definition")]
public class McpServerDefinition
{
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "description")]
    public string Description { get; set; } = string.Empty;

    [DataMember(Name = "command")]
    public string Command { get; set; } = string.Empty;

    [DataMember(Name = "args")]
    public List<string> Args { get; set; } = new List<string>();

    [DataMember(Name = "environment")]
    public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

    [DataMember(Name = "workingDirectory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{Name}\n\tDescription:{Description}\n\tCommand:{Command}\n\tArgs:{string.Join(", ", Args)}\n\tEnvironment:{string.Join(", ", Environment.Select(kvp => $"{kvp.Key}={kvp.Value}"))}\n\tWorkingDirectory:{WorkingDirectory}";
    }
}
