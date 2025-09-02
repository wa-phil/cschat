using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public static class S360Insights
{
    /// <summary>
    /// Extract anchor tags from KPI description HTML, de-duped by URL.
    /// Also appends the row URL as "S360 item" if provided.
    /// </summary>
    internal static IEnumerable<(string text, string url)> ExtractLinks(string? html, string? fallbackUrl)
    {
        var results = new List<(string text, string url)>();
        if (!string.IsNullOrWhiteSpace(html))
        {
            var rx = new Regex("<a\\s+[^>]*href=[\"'](?<href>[^\"']+)[\"'][^>]*>(?<text>.*?)</a>",
                               RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in rx.Matches(html))
            {
                var href = m.Groups["href"].Value?.Trim();
                var text = Utilities.StripHtml(m.Groups["text"].Value ?? string.Empty);
                text = string.IsNullOrWhiteSpace(text) ? "Link" : Utilities.TruncatePlain(text, 80);
                if (!string.IsNullOrWhiteSpace(href))
                    results.Add((text, href));
            }
        }
        if (!string.IsNullOrWhiteSpace(fallbackUrl))
            results.Add(("S360 item", fallbackUrl!.Trim()));

        // de-dup by URL
        return results
            .GroupBy(x => x.url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());
    }

    /// <summary>
    /// Build a strong-formatting prompt that asks the LLM to output a grouped action plan.
    /// Items are grouped by Service and sorted by DUE date (soonest first).
    /// </summary>
    public static string MakeGroupedActionPlanPrompt(
        IEnumerable<(S360Client.S360Row Row, float Score, Dictionary<string,float> Factors)> scored)
    {
        var groups = scored
            .GroupBy(x => x.Row.ServiceName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new {
                Service = g.Key,
                Items = g.OrderBy(x => ParseDate(x.Row.CurrentDueDate) ?? DateTime.MaxValue)
            });

        var sb = new StringBuilder();
        sb.AppendLine("You are an engineering manager writing a triage action plan.");
        sb.AppendLine("Goal: make it scannable. Group by Service. Under each service, list items sorted by soonest DUE date.");
        sb.AppendLine("For EACH item output exactly:");
        sb.AppendLine("  - <Title> [<SLAState>] (Due: <yyyy-MM-dd or —> | ETA: <value or —> | Owner: <Assigned or Unassigned>)");
        sb.AppendLine("    Summary: <1–2 crisp sentences, max ~240 chars>");
        sb.AppendLine("    Next: <clear next step(s) such as set ETA / assign owner / update status / follow link>");
        sb.AppendLine("    Links:");
        sb.AppendLine("      - <description> — <url>");
        sb.AppendLine("Use the provided link text as the description; if missing, use 'S360 item'. Do not invent URLs or facts. Keep it concise.");
        sb.AppendLine();

        foreach (var g in groups)
        {
            sb.AppendLine($"Service: {g.Service}");
            foreach (var x in g.Items)
            {
                var r = x.Row;
                var title = string.IsNullOrWhiteSpace(r.ActionItemTitle) ? r.KpiTitle : r.ActionItemTitle;
                var status = string.IsNullOrWhiteSpace(r.CurrentStatus) ? "" : $"Status: {Utilities.TruncatePlain(r.CurrentStatus, 200)}";
                var descPlain = Utilities.TruncatePlain(Utilities.StripHtml(r.KpiDescriptionHtml ?? string.Empty), 500);
                var eta = string.IsNullOrWhiteSpace(r.CurrentETA) ? "—" : r.CurrentETA.Trim();
                var due = NormalizeDateString(r.CurrentDueDate);
                var owner = string.IsNullOrWhiteSpace(r.AssignedTo) ? "Unassigned" : r.AssignedTo.Trim();

                sb.AppendLine($"- Title: {title}");
                sb.AppendLine($"  SLA: {r.SLAState}");
                sb.AppendLine($"  Due: {due}");
                sb.AppendLine($"  ETA: {eta}");
                sb.AppendLine($"  Owner: {owner}");
                if (!string.IsNullOrWhiteSpace(status)) sb.AppendLine($"  {status}");
                if (!string.IsNullOrWhiteSpace(descPlain)) sb.AppendLine($"  Description: {descPlain}");

                var links = ExtractLinks(r.KpiDescriptionHtml, r.URL);
                sb.AppendLine("  ItemLinks:");
                foreach (var (text,url) in links)
                    sb.AppendLine($"    - [{text}] {url}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("Now produce the action plan in the REQUIRED output format described above. Do not add extra commentary.");
        return sb.ToString();

        static DateTime? ParseDate(string s)
            => DateTime.TryParse(s, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : (DateTime?)null;

        static string NormalizeDateString(string s)
        {
            if (DateTime.TryParse(s, out var dt)) return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-dd");
            return "—";
        }
    }
}
