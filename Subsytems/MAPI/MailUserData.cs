using System;
using System.Collections.Generic;

[UserManagedAttribute("Favorite Mail Folders", "Stores user-favorited mail folders for quick access")]
public class FavoriteMailFolder
{
    [UserKey]
    [UserField(required: true, FieldKind = UiFieldKind.String)] public string IdOrName { get; set; } = string.Empty;

    [UserField(required: true, FieldKind = UiFieldKind.String)] public string DisplayName { get; set; } = string.Empty;

    public FavoriteMailFolder() { }

    public FavoriteMailFolder(string idOrName, string displayName)
    {
        IdOrName   = idOrName;
        DisplayName = displayName;
    }

    public override string ToString() => $"{DisplayName} [{IdOrName}]";
}

[UserManagedAttribute("Mail Topics", "Keywords and description for topic-based mail summaries")]
public class MailTopic
{
    [UserKey]
    [UserField(required: true, FieldKind = UiFieldKind.String)] public string Name { get; set; } = string.Empty;

    [UserField(required: true, FieldKind = UiFieldKind.Text)] public string Description { get; set; } = string.Empty;

    // Comma- or newline-separated keywords/phrases used to match emails into this topic
    [UserField(required: true, FieldKind = UiFieldKind.String)] public string Keywords { get; set; } = string.Empty;

    public override string ToString() => $"{Name} — {Description}";
}
