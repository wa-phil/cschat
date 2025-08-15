using System;

[UserManagedAttribute("User Selected Query", "Stores user-selected ADO queries with their names and GUIDs")]
public class UserSelectedQuery
{
    [UserKey] // use ID as the logical key for updates
    [UserField(required: true)]
    public Guid Id { get; set; }
    
    [UserField(required: true)]
    public string Name { get; set; } = string.Empty;
    
    [UserField(required: true)]
    public string Project { get; set; } = string.Empty;
    
    [UserField(required: true)]
    public string Path { get; set; } = string.Empty;

    public UserSelectedQuery() { }

    public UserSelectedQuery(Guid id, string name, string project, string path)
    {
        Id = id;
        Name = name;
        Project = project;
        Path = path;
    }

    public override string ToString() => $"{Name}: [{Id}] {Project}/{Path}";
}
