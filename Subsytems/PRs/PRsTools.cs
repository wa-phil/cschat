using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.TeamFoundation.SourceControl.WebApi;

public sealed class FetchPRsInput
{
    [UserField(required:true, display:"Profile Name")] public string ProfileName { get; set; } = "";
    [UserField(display:"Export Path (.csv/.json)")]   public string? Export { get; set; }
}

[IsConfigurable("tool.prs.fetch")]
public sealed class PRsFetchTool : ITool
{
    public string Description => "Fetch PRs (stale/new/closed) for a profile and cache results.";
    public string Usage       => "PRsFetch({ \"ProfileName\":\"Deployment\" })";
    public Type   InputType   => typeof(FetchPRsInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ProfileName\":{\"type\":\"string\"},\"Export\":{\"type\":\"string\"}},\"required\":[\"ProfileName\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (FetchPRsInput)input;
        var profile = Program.userManagedData.GetItems<PRsProfile>()
            .FirstOrDefault(x => x.Name.Equals(p.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ToolResult.Failure($"No PRs Profile named '{p.ProfileName}'.", ctx);

        var prs = Program.SubsystemManager.Get<PRsClient>();
        var (cols, rows) = await prs.FetchAsync(profile);

        var table = Utilities.ToTable(cols, rows, Console.WindowWidth);
        ctx.AddToolMessage(table);
        await ContextManager.AddContent(table, $"prs/{profile.Name}/results");

        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = Path.GetExtension(p.Export).ToLowerInvariant();
            var content = ext switch
            {
                ".csv"  => Utilities.ToCsv(cols, rows),
                ".json" => Utilities.ToJson(cols, rows),
                _       => table
            };
            File.WriteAllText(p.Export!, content);
            ctx.AddToolMessage($"Saved: {p.Export}");
        }
        return ToolResult.Success(table, ctx);
    }
}

public sealed class ReportPRsInput
{
    [UserField(required:true, display:"Profile Name")] public string ProfileName { get; set; } = "";
    [UserField(display:"Top N stale per IC")] public int TopN { get; set; } = 3;
}

[IsConfigurable("tool.prs.report")]
public sealed class PRsReportTool : ITool
{
    public string Description => "Manager report — counts per IC, oldest stale list per IC (LLM-free + optional summary).";
    public string Usage       => "PRsReport({ \"ProfileName\":\"Deployment\", \"TopN\":3 })";
    public Type   InputType   => typeof(ReportPRsInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ProfileName\":{\"type\":\"string\"},\"TopN\":{\"type\":\"integer\"}},\"required\":[\"ProfileName\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (ReportPRsInput)input;
        var profile = Program.userManagedData.GetItems<PRsProfile>()
            .FirstOrDefault(x => x.Name.Equals(p.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ToolResult.Failure($"No PRs Profile named '{p.ProfileName}'.", ctx);

        var prs = Program.SubsystemManager.Get<PRsClient>();
        var (cols, rows) = await prs.FetchAsync(profile);

        // Column ordinals
        int iCat = cols.Col("Category"), iName = cols.Col("CreatedByDisplayName"), iTitle = cols.Col("Title");
        int iLink = cols.Col("Link"), iCreated = cols.Col("CreationDate"), iClosed = cols.Col("ClosedDate"), iAge = cols.Col("AgeDays");

        var items = rows.Select(r => new {
            Category = r[iCat], Name = r[iName], Title = r[iTitle], Link = r[iLink], Created = r[iCreated], Closed = r[iClosed], Age = r[iAge]
        }).ToList();

        var byIC = items.GroupBy(x => x.Name).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Manager Briefing (counts per IC):");
        foreach (var g in byIC)
        {
            int stale = g.Count(x => x.Category == "stale");
            int fresh = g.Count(x => x.Category == "new");
            int closed = g.Count(x => x.Category == "closed");
            sb.AppendLine($"- {g.Key}: {stale} stale, {fresh} new, {closed} closed (30d)");
        }
        sb.AppendLine();

        // Oldest stale per IC
        sb.AppendLine($"Oldest stale per IC (top {Math.Max(1, p.TopN)}):");
        foreach (var g in byIC)
        {
            var stale = g.Where(x => x.Category == "stale")
                         .OrderBy(x => DateTime.TryParse(x.Created, out var d) ? d : DateTime.MaxValue)
                         .Take(Math.Max(1, p.TopN))
                         .ToList();
            if (stale.Count == 0) continue;
            sb.AppendLine($"{g.Key}:");
            foreach (var s in stale) sb.AppendLine($"  • {s.Created[..10]} — {Utilities.TruncatePlain(s.Title, 110)} → {s.Link}");
        }

        var output = sb.ToString();
        ctx.AddToolMessage(output);
        await ContextManager.AddContent(output, $"prs/{profile.Name}/report");
        return ToolResult.Success(output, ctx);
    }
}

public sealed class SlicePRsInput
{
    [UserField(required:true, display:"Profile Name")] public string ProfileName { get; set; } = "";
    [UserField(required:true, display:"Slice (stale|new|closed)")] public string Slice { get; set; } = "stale";
    public int Limit { get; set; } = 25;
    public string? Export { get; set; }
}

[IsConfigurable("tool.prs.slice")]
public sealed class PRsSliceTool : ITool
{
    public string Description => "Filter by slice and print/export a focused list (Title, Created, Link).";
    public string Usage       => "PRsSlice({ \"ProfileName\":\"Deployment\", \"Slice\":\"stale\", \"Limit\":25 })";
    public Type   InputType   => typeof(SlicePRsInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ProfileName\":{\"type\":\"string\"},\"Slice\":{\"type\":\"string\"},\"Limit\":{\"type\":\"integer\"},\"Export\":{\"type\":\"string\"}},\"required\":[\"ProfileName\",\"Slice\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (SlicePRsInput)input;
        var profile = Program.userManagedData.GetItems<PRsProfile>()
            .FirstOrDefault(x => x.Name.Equals(p.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ToolResult.Failure($"No PRs Profile named '{p.ProfileName}'.", ctx);

        var prs = Program.SubsystemManager.Get<PRsClient>();
        var (cols, rows) = await prs.FetchAsync(profile);

        int iCat = cols.Col("Category"), iTitle = cols.Col("Title"), iLink = cols.Col("Link"), iCreated = cols.Col("CreationDate");
        var filtered = rows.Where(r => string.Equals(r[iCat], p.Slice, StringComparison.OrdinalIgnoreCase))
                           .OrderBy(r => DateTime.TryParse(r[iCreated], out var d) ? d : DateTime.MaxValue)
                           .Take(Math.Max(1, p.Limit))
                           .ToList();

        var lines = new List<string>();
        lines.Add($"Slice: {p.Slice} (limit {p.Limit})");
        foreach (var r in filtered) lines.Add($"- {r[iCreated][..10]} — {Utilities.TruncatePlain(r[iTitle], 120)} → {r[iLink]}");
        var output = string.Join("\n", lines);

        ctx.AddToolMessage(output);
        await ContextManager.AddContent(output, $"prs/{profile.Name}/slice/{p.Slice}");

        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = Path.GetExtension(p.Export).ToLowerInvariant();
            var content = ext switch
            {
                ".csv"  => Utilities.ToCsv(cols, filtered),
                ".json" => Utilities.ToJson(cols, filtered),
                _       => output
            };
            File.WriteAllText(p.Export!, content);
            ctx.AddToolMessage($"Saved: {p.Export}");
        }
        return ToolResult.Success(output, ctx);
    }
}

// Coach: fetch comment threads via ADO SDK and summarize recurring themes
public sealed class CoachPRsInput
{
    [UserField(required:true, display:"Profile Name")] public string ProfileName { get; set; } = "";
    [UserField(display:"Max PRs per IC to analyze")] public int MaxPerIC { get; set; } = 2;
}

[IsConfigurable("tool.prs.coach")]
public sealed class PRsCoachTool : ITool
{
    public string Description => "Pull ADO PR threads (oldest stale first per IC) and produce coaching suggestions.";
    public string Usage       => "PRsCoach({ \"ProfileName\":\"Deployment\", \"MaxPerIC\":2 })";
    public Type   InputType   => typeof(CoachPRsInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ProfileName\":{\"type\":\"string\"},\"MaxPerIC\":{\"type\":\"integer\"}},\"required\":[\"ProfileName\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (CoachPRsInput)input;
        var profile = Program.userManagedData.GetItems<PRsProfile>()
            .FirstOrDefault(x => x.Name.Equals(p.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ToolResult.Failure($"No PRs Profile named '{p.ProfileName}'.", ctx);

        var prs = Program.SubsystemManager.Get<PRsClient>();
        var (cols, rows) = await prs.FetchAsync(profile);

        int iCat = cols.Col("Category"), iName = cols.Col("CreatedByDisplayName");
        int iOrg = cols.Col("OrganizationName"), iProj = cols.Col("RepositoryProjectName"), iRepo = cols.Col("RepositoryName"), iId = cols.Col("PullRequestId"), iCreated = cols.Col("CreationDate");
        int iLink = cols.Col("Link");

        // Build per-IC picks (oldest first) – prefer stale, fall back to new
        var perIc = rows.Where(r => r[iCat] == "stale")
                        .GroupBy(r => r[iName])
                        .ToDictionary(g => g.Key, g => g.OrderBy(r => DateTime.TryParse(r[iCreated], out var d) ? d : DateTime.MaxValue)
                                                        .Take(Math.Max(1, p.MaxPerIC))
                                                        .Select(r => (Org: r[iOrg], Proj: r[iProj], Repo: r[iRepo], Id: int.Parse(r[iId]), Link: iLink >= 0 ? r[iLink] : string.Empty))
                                                        .ToList());

        // Fallback to “new” if there are no stale PRs at all
        if (perIc.Count == 0)
        {
            perIc = rows.Where(r => r[iCat] == "new")
                        .GroupBy(r => r[iName])
                        .ToDictionary(g => g.Key, g => g.OrderBy(r => DateTime.TryParse(r[iCreated], out var d) ? d : DateTime.MaxValue)
                                                        .Take(Math.Max(1, p.MaxPerIC))
                                                        .Select(r => (Org: r[iOrg], Proj: r[iProj], Repo: r[iRepo], Id: int.Parse(r[iId]), Link: iLink >= 0 ? r[iLink] : string.Empty))
                                                        .ToList());
        }

        var ado = Program.SubsystemManager.Get<AdoClient>();
        var sb = new StringBuilder();
        sb.AppendLine("Coaching Hints (by IC):");

        int printed = 0;
        foreach (var kv in perIc)
        {
            var ic = kv.Key; var picks = kv.Value;
            var bullets = new List<string>();

            foreach (var pr in picks)
            {
                List<GitPullRequestCommentThread> threads;
                try
                {
                    threads = await ado.GetThreadsAsync(pr.Proj, pr.Repo, pr.Id);
                    
                    // Build a few concrete, actionable examples with discussion URLs
                    var examples = new List<string>();

                    IEnumerable<(int ThreadId, string Snippet)> PickExamples()
                    {
                        // Flatten comments with their thread id
                        var all = threads
                            .SelectMany(t => (t.Comments ?? new List<Comment>()).Select(c => new { t.Id, c.Content }))
                            .Where(x => !string.IsNullOrWhiteSpace(x.Content));

                        // Light-weight keyword buckets to find representative, actionable examples
                        var buckets = new (string Tag, string[] Keys)[]
                        {
                            ("tests", new[] {"test", "unit", "coverage", "flaky"}),
                            ("size",  new[] {"too big", "scope", "split", "refactor"}),
                            ("ci",    new[] {"CI", "build", "pipeline", "fail", "flake"}),
                            ("perf",  new[] {"perf", "latency", "alloc", "hot path"}),
                            ("docs",  new[] {"doc", "comment", "readme", "description"}),
                            ("style", new[] {"nit", "style", "naming", "format", "convention"}),
                            ("merge", new[] {"merge conflict", "rebase", "out of date"})
                        };

                        // score each comment by bucket
                        var scored = all.Select(x =>
                        {
                            var lower = x.Content.ToLowerInvariant();
                            var (tag, score) = buckets
                                .Select(b => (b.Tag, Score: b.Keys.Count(k => lower.Contains(k.ToLowerInvariant()))))
                                .OrderByDescending(t => t.Score)
                                .FirstOrDefault();
                            return new { x.Id, x.Content, tag, score };
                        })
                        .Where(s => s.score > 0);

                        // take top few distinct tags to avoid redundancy
                        var picked = scored
                            .GroupBy(s => s.tag)
                            .OrderByDescending(g => g.Max(s => s.score))
                            .Take(3)
                            .Select(g =>
                            {
                                var best = g.OrderByDescending(s => s.score).First();
                                var snippet = Utilities.TruncatePlain(best.Content.Replace("\r", " ").Replace("\n", " "), 160);
                                return (ThreadId: best.Id, Snippet: snippet);
                            });

                        return picked;
                    }

                    foreach (var ex in PickExamples())
                    {
                        var tUrl = AdoClient.BuildDiscussionUrl(pr.Org, pr.Proj, pr.Repo, pr.Id, ex.ThreadId);
                        examples.Add($"- \"{ex.Snippet}\"  \n    ↳ {tUrl}");
                    }

                    if (examples.Count > 0)
                    {
                        bullets.Add("Examples:\n" + string.Join("\n", examples) + "\n");
                    }

                }
                catch (Exception ex)
                {
                    bullets.Add($"PR {pr.Id}: (error fetching threads) {ex.Message}");
                    continue;
                }

                var text = string.Join("\n", threads
                    .SelectMany(t => t.Comments?.Select(c => c.Content) ?? Enumerable.Empty<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

                if (string.IsNullOrWhiteSpace(text))
                {
                    bullets.Add($"PR {pr.Id}: no review comments yet.");
                    continue;
                }

                var prompt = @"""
You are an EM reviewing PR review threads. Summarize the recurring coaching themes in 3-5 bullets. Use short, actionable language.
Focus areas to look for: test coverage, build/CI issues, PR size/scope, unclear descriptions, style/nits churn, rebase/merge conflicts, perf concerns, ownership or responsiveness.
Output only bullets (no preamble). """ + "\n\n" + Utilities.TruncatePlain(text, 4000);

                var summary = await Engine.Provider!.PostChatAsync(new Context(prompt), 0.2f);

                var prUrl = !string.IsNullOrWhiteSpace(pr.Link)
                    ? pr.Link
                    : AdoClient.BuildPullRequestUrl(pr.Org, pr.Proj, pr.Repo, pr.Id);

                bullets.Add($"PR {pr.Id} — {prUrl}:\n{summary.Trim()}\n");
            }

            // Always print the IC header; show “no items” if needed
            sb.AppendLine($"{ic}:");
            if (bullets.Count == 0)
            {
                sb.AppendLine("  (no actionable coaching items found)");
            }
            else
            {
                foreach (var b in bullets) sb.AppendLine("  " + b.Replace("\n", "\n  "));
                printed++;
            }
        }

        // If nothing at all was printed, add a friendly note
        if (printed == 0)
        {
            sb.AppendLine("No stale or new PRs with review discussion were found for this profile and window.");
        }

        var output = sb.ToString();
        ctx.AddToolMessage(output);
        await ContextManager.AddContent(output, $"prs/{profile.Name}/coach");
        return ToolResult.Success(output, ctx);
    }
}


public static class PrsExtensions
{
    // Thin wrapper since GitHttpClient is already initialized in AdoClient; keep the public method here to avoid exposing internals.
    public static async Task<List<GitPullRequestCommentThread>> GetThreadsAsync(this AdoClient ado, string project, string repoName, int prId)
        => await ado.GetPullRequestThreadsAsync(project, repoName, prId);

    public static int Col(this IReadOnlyList<string> cols, string name) =>
        cols.Select((c, i) => new { c, i }).FirstOrDefault(x => string.Equals(x.c, name, StringComparison.OrdinalIgnoreCase))?.i ?? -1;

}