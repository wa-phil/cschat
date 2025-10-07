using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
    /// Append a structured action plan into a Report object. Items are grouped by Service
    /// and sorted by due date. This produces a Report (tables & bullets) suitable for
    /// rendering with the app's Report API.
    /// </summary>
    public static async Task AppendGroupedActionPlanAsync(Report parent, IEnumerable<(S360Client.S360Row Row, float Score, Dictionary<string,float> Factors)> scored)
    {
        if (null == Engine.Provider) throw new InvalidOperationException("LLM provider is not configured.");

        var groups = scored
            .GroupBy(x => x.Row.ServiceName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new {
                Service = g.Key,
                Items = g.OrderBy(x => ParseDate(x.Row.CurrentDueDate) ?? DateTime.MaxValue)
            });

        foreach (var g in groups)
        {
            // Prepare per-item summaries (await provider outside of the report builders)
            var preparedItems = new List<(string title, S360Client.S360Row r, string summary, string next, List<(string text,string url)> links)>();
            foreach (var x in g.Items)
            {
                var r = x.Row;
                var title = string.IsNullOrWhiteSpace(r.ActionItemTitle) ? r.KpiTitle : r.ActionItemTitle;
                var deterministicSummary = r.KpiDescriptionHtml ?? string.Empty;
                string summaryText = deterministicSummary;
                string nextStep = "set ETA / assign owner / update status";
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("You are an assistant that writes a concise 3-4 sentence summary and a one-line next step for an engineering action item.");
                    sb.AppendLine("Provide output as two lines beginning with 'Summary:' and 'Next:' respectively.");
                    sb.AppendLine();
                    sb.AppendLine($"Title: {title}");
                    sb.AppendLine($"SLAState: {r.SLAState}");
                    sb.AppendLine($"Due: {NormalizeDateString(r.CurrentDueDate)}");
                    sb.AppendLine($"ETA: { (string.IsNullOrWhiteSpace(r.CurrentETA)? "—": r.CurrentETA.Trim()) }");
                    sb.AppendLine($"Owner: { (string.IsNullOrWhiteSpace(r.AssignedTo)? "Unassigned": r.AssignedTo.Trim()) }");
                    if (!string.IsNullOrWhiteSpace(deterministicSummary)) sb.AppendLine($"Description: {deterministicSummary}");
                    var links = ExtractLinks(r.KpiDescriptionHtml, r.URL).Take(5).ToList();
                    if (links.Any())
                    {
                        sb.AppendLine("Links:");
                        foreach (var (t,u) in links) sb.AppendLine($"- {t} — {u}");
                    }

                    var ctx = new Context(sb.ToString());
                    var resp = await Engine.Provider!.PostChatAsync(ctx, 0.2f);
                    if (!string.IsNullOrWhiteSpace(resp))
                    {
                        var lines = resp.Split(new[] {'\n','\r'}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim()).ToList();
                        var sumLine = lines.FirstOrDefault(l => l.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase));
                        var nextLine = lines.FirstOrDefault(l => l.StartsWith("Next:", StringComparison.OrdinalIgnoreCase));
                        if (sumLine != null) summaryText = sumLine.Substring(sumLine.IndexOf(':')+1).Trim();
                        if (nextLine != null) nextStep = nextLine.Substring(nextLine.IndexOf(':')+1).Trim();
                        if (sumLine == null && lines.Count > 0) summaryText = lines[0];
                    }
                }
                catch (Exception ex)
                {
                    Log.Method(ctx=>ctx.Failed("LLM error", ex));
                }

                preparedItems.Add((Utilities.TruncatePlain(title,120), r, summaryText, nextStep, ExtractLinks(r.KpiDescriptionHtml, r.URL).ToList()));
            }

            // Each service becomes a section. Under each service, create a subsection per item
            parent.Section(g.Service ?? "(unknown)", sec => {
                foreach (var it in preparedItems)
                {
                    sec.Section(it.title, item => {
                        item.Paragraph($"[{it.r.SLAState}] (Due: {NormalizeDateString(it.r.CurrentDueDate)} | ETA: {(string.IsNullOrWhiteSpace(it.r.CurrentETA)? "—": it.r.CurrentETA.Trim())} | Owner: {(string.IsNullOrWhiteSpace(it.r.AssignedTo)? "Unassigned": it.r.AssignedTo.Trim())})");
                        if (!string.IsNullOrWhiteSpace(it.summary)) item.Section("Summary", s => s.Paragraph(it.summary));
                        if (!string.IsNullOrWhiteSpace(it.next)) item.Section("Next steps", s => s.Bulleted(it.next));
                        if (it.links.Any()) item.Section("Links", l => l.Bulleted(it.links.Select(lk => $"{lk.text} — {lk.url}").ToArray()));
                    });
                }
            });
        }

        static DateTime? ParseDate(string s)
            => DateTime.TryParse(s, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : (DateTime?)null;

        static string NormalizeDateString(string s)
        {
            if (DateTime.TryParse(s, out var dt)) return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-dd");
            return "—";
        }
    }
}
