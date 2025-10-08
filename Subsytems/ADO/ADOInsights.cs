using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

public sealed class AdoInsightsConfig
{
    public int FreshDays = 7;            // “fresh” window
    public int SoonDays = 7;             // due-date “soon”
    public float W_Fresh = 2.0f;
    public float W_RecentChange = 1.2f;
    public float W_Unassigned = 0.8f;
    public float W_PriorityHigh = 2.5f;  // P1/P2
    public float W_CriticalTag = 3.0f;   // security, data loss, escalation, blocker
    public float W_DueSoon = 2.2f;
    public List<string> CriticalTags = new List<string> { "security","data loss","escalation","blocker","sev1","sev2" };
}

public sealed class ScoredItem
{
    public WorkItemSummary Item = null!;
    public float Score;
    public Dictionary<string,float> Factors = new();
}

public static class AdoInsights
{
    public static (List<ScoredItem> ranked, Dictionary<string,int> countsByState,
                   Dictionary<string,int> countsByTag, Dictionary<string,int> countsByArea) 
        Analyze(IEnumerable<WorkItemSummary> items, AdoInsightsConfig cfg)
    {
        var now = DateTime.UtcNow;
        var list = items.ToList();

        var ranked = list.Select(wi =>
        {
            var f = new Dictionary<string,float>();
            float s = 0;

            // Freshness
            if ((now - wi.CreatedDate).TotalDays <= cfg.FreshDays)
            { f["fresh"] = cfg.W_Fresh; s += cfg.W_Fresh; }

            // Recently changed
            if ((now - wi.ChangedDate).TotalDays <= cfg.FreshDays)
            { f["recentChange"] = cfg.W_RecentChange; s += cfg.W_RecentChange; }

            // Unassigned bump
            if (string.IsNullOrWhiteSpace(wi.AssignedTo) || wi.AssignedTo == "Unassigned")
            { f["unassigned"] = cfg.W_Unassigned; s += cfg.W_Unassigned; }

            // Priority (treat 1/2 as high)
            if (int.TryParse(wi.Priority, out var p) && p <= 2)
            { f["priorityHigh"] = cfg.W_PriorityHigh; s += cfg.W_PriorityHigh; }

            // Due soon
            if (wi.DueDate.HasValue && (wi.DueDate.Value - now).TotalDays <= cfg.SoonDays)
            { f["dueSoon"] = cfg.W_DueSoon; s += cfg.W_DueSoon; }

            // Critical tags
            var hasCritical = wi.Tags.Any(t => cfg.CriticalTags.Any(ct =>
                t.Contains(ct, StringComparison.OrdinalIgnoreCase)));
            if (hasCritical)
            { f["criticalTag"] = cfg.W_CriticalTag; s += cfg.W_CriticalTag; }

            return new ScoredItem { Item = wi, Score = s, Factors = f };
        })
        .OrderByDescending(x => x.Score)
        .ThenBy(x => x.Item.DueDate ?? DateTime.MaxValue)
        .ThenBy(x => x.Item.Priority) // string compare okay for "1","2","3" if single-digit
        .ToList();

        var countsByState = list.GroupBy(x => x.State).OrderByDescending(g=>g.Count())
            .ToDictionary(g => g.Key ?? "(unknown)", g => g.Count());

        var countsByTag = list.SelectMany(x => x.Tags).Where(t=>!string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var countsByArea = list.GroupBy(x => x.AreaPath ?? "(none)")
            .OrderByDescending(g=>g.Count()).ToDictionary(g=>g.Key, g=>g.Count());

        return (ranked, countsByState, countsByTag, countsByArea);
    }

    public static string MakeManagerBriefingPrompt(
        Dictionary<string,int> byState, Dictionary<string,int> byTag, Dictionary<string,int> byArea,
        IEnumerable<WorkItemSummary> sampleTitles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an engineering manager. Write a crisp 30-second briefing for your manager.");
        sb.AppendLine("Use bullet points. Focus on clusters/themes, risks, and what's changed recently.");
        sb.AppendLine();
        sb.AppendLine("Counts by state:");
        foreach (var kv in byState.Take(10)) sb.AppendLine($"- {kv.Key}: {kv.Value}");

        sb.AppendLine("\nTop tags:");
        foreach (var kv in byTag.Take(10)) sb.AppendLine($"- {kv.Key}: {kv.Value}");

        sb.AppendLine("\nTop areas:");
        foreach (var kv in byArea.Take(10)) sb.AppendLine($"- {kv.Key}: {kv.Value}");

        sb.AppendLine("\nSample item titles (for clustering intuition):");
        foreach (var t in sampleTitles.Take(20)) sb.AppendLine($"- {t.Title}");

        sb.AppendLine("\nWrite 6-10 bullets. Avoid restating the raw counts.");
        return sb.ToString();
    }

    public static string MakeActionPlanPrompt(IEnumerable<ScoredItem> top)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an EM preparing a triage action plan for the following items (ranked).");
        sb.AppendLine("For each item, give: 1) Assign to (junior/senior + discipline) but do not name the engineer, 2) Next concrete step, 3) Likely resolution (fix/duplicate/transfer), 4) Preventative investment.");
        sb.AppendLine("Finish with 3-5 'big rocks' bullets for the team to focus on for the time being.");
        sb.AppendLine();
        foreach (var s in top)
        {
            sb.AppendLine($"ID {s.Item.Id} [{s.Item.State}] P:{s.Item.Priority} " +
                          $"Assignee:{(string.IsNullOrWhiteSpace(s.Item.AssignedTo)?"(unassigned)":s.Item.AssignedTo)}");
            if (s.Item.DueDate.HasValue) sb.AppendLine($"Due: {s.Item.DueDate:u}");
            sb.AppendLine($"Title: {s.Item.Title}");
            if (!string.IsNullOrWhiteSpace(s.Item.Description))
                sb.AppendLine($"Desc: {Utilities.TruncatePlain(s.Item.Description, 400)}");
            if (s.Item.Tags.Any()) sb.AppendLine("Tags: " + string.Join(", ", s.Item.Tags));
            sb.AppendLine($"Signals: {string.Join(", ", s.Factors.Keys)} | Score: {s.Score:0.0}");
            sb.AppendLine();
        }
        sb.AppendLine("Be decisive and specific. Keep total output under ~600 words.");
        return sb.ToString();
    }
}
