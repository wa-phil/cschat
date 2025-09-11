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

[UserManagedAttribute("Spam Rule", "Stochastic LLM-evaluated spam rules")]
public class SpamRule
{
    [UserKey]
    [UserField(required: true)] public Guid Id { get; set; } = Guid.NewGuid();

    // Human-authored or LLM-authored rule description/patterns/heuristics
    [UserField(required: true)] public string RuleText { get; set; } = string.Empty;

    // Optional weight / confidence (for ranking rules if multiple fire)
    [UserField] public float Weight { get; set; } = 1.0f;

    // Simple telemetry to adapt rules
    [UserField] public int TruePositives { get; set; } = 0;
    [UserField] public int FalsePositives { get; set; } = 0;

    public override string ToString() => $"{RuleText} (w={Weight:0.00}, TP={TruePositives}, FP={FalsePositives})";
}
