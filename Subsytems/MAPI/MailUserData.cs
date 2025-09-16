using System;
using System.Collections.Generic;

[UserManagedAttribute("Favorite Mail Folder", "Stores user-favorited mail folders for quick access")]
public class FavoriteMailFolder
{
    [UserKey]
    [UserField(required: true)] public string IdOrName { get; set; } = string.Empty;

    [UserField(required: true)] public string DisplayName { get; set; } = string.Empty;

    public FavoriteMailFolder() { }

    public FavoriteMailFolder(string idOrName, string displayName)
    {
        IdOrName   = idOrName;
        DisplayName = displayName;
    }

    public override string ToString() => $"{DisplayName} [{IdOrName}]";
}

[UserManagedAttribute("Mail Topic", "Keywords and description for topic-based mail summaries")]
public class MailTopic
{
    [UserKey]
    [UserField(required: true)] public string Name { get; set; } = string.Empty;

    [UserField(required: true)] public string Description { get; set; } = string.Empty;

    // Comma- or newline-separated keywords/phrases used to match emails into this topic
    [UserField(required: true)] public string Keywords { get; set; } = string.Empty;

    public override string ToString() => $"{Name} — {Description}";
}
