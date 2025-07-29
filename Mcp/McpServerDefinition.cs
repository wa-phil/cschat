using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

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
    
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; } = true;
    
    [DataMember(Name = "createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [DataMember(Name = "updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class McpServerList
{
    public List<McpServerDefinition> Servers { get; set; } = new List<McpServerDefinition>();
}
