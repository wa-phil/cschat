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
        var table = await prs.FetchAsync(profile);

        // filter and format the table output to something more easily digested by a user.
        var projected = table.SelectRows(a => new
        {
            Author = a("CreatedByDisplayName"),
            Category = a("Category"),
            Created = DateTime.Parse(a("CreationDate")).ToString("MM/dd/yyyy"),
            Closed = DateTime.TryParse(a("ClosedDate"), out var dt) ? dt.ToString("MM/dd/yyyy") : "",
            Status = a("Status"),
            Title = Utilities.TruncatePlain(a("Title"), 80),
            Link = a("Link")
        });
        table = Table.FromEnumerable(projected);

        Program.ui.RenderTable(table, "PRs");
        await ContextManager.AddContent(table.ToCsv(), $"prs/{profile.Name}/results");

        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = Path.GetExtension(p.Export).ToLowerInvariant();
            var content = ext switch
            {
                ".json" => table.ToJson(),
                _       => table.ToCsv()
            };
            File.WriteAllText(p.Export!, content);
            ctx.AddToolMessage($"Saved: {p.Export}");
        }
        return ToolResult.Success($"{profile.Name}: returned {table.Rows.Count} rows", ctx);
    }
}

public sealed class ReportPRsInput
{
    [UserField(required:true, display:"Profile Name")] public string ProfileName { get; set; } = "";
    [UserField(display:"Recent window (days)")] public int WindowDays { get; set; } = 14;
}

[IsConfigurable("tool.prs.report")]
public sealed class PRsReportTool : ITool
{
    public string Description => "Manager report — recent window: counts per IC + linkable bullets with brief descriptions.";
    public string Usage       => "PRsReport({ \"ProfileName\":\"Deployment\", \"WindowDays\":14 })";
    public Type   InputType   => typeof(ReportPRsInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ProfileName\":{\"type\":\"string\"},\"WindowDays\":{\"type\":\"integer\"}},\"required\":[\"ProfileName\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (ReportPRsInput)input;
        var profile = Program.userManagedData.GetItems<PRsProfile>()
            .FirstOrDefault(x => x.Name.Equals(p.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ToolResult.Failure($"No PRs Profile named '{p.ProfileName}'.", ctx);

        var prs = Program.SubsystemManager.Get<PRsClient>();
        var table = await prs.FetchAsync(profile);

        // Column ordinals + projection
        var items = table.SelectRows(accessor => new {
            Category = accessor("Category"),
            Name     = accessor("CreatedByDisplayName"),
            Title    = accessor("Title"),
            Link     = accessor("Link"),
            Created  = accessor("CreationDate"),
            Closed   = accessor("ClosedDate"),
            Age      = accessor("AgeDays"),
            State    = accessor("Status")
        }).ToList();

        // Build all ICs (stable roster), then compute recent-window counts per IC
        var allICs = items.GroupBy(x => x.Name)
                          .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                          .Select(g => g.Key)
                          .ToList();

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, p.WindowDays));
        static DateTime Parse(string s) => DateTime.TryParse(s, out var d) ? d : DateTime.MinValue;
        bool InWindow(string created, string closed)
            => Parse(created) >= cutoff || Parse(closed) >= cutoff;

        var recent = items.Where(it => InWindow(it.Created, it.Closed)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Manager Briefing (counts per IC — last {Math.Max(1, p.WindowDays)}d):");
        foreach (var icName in allICs)
        {
            var g = recent.Where(x => x.Name == icName);
            int stale  = g.Count(x => x.Category == "stale");
            int fresh  = g.Count(x => x.Category == "new");
            int closed = g.Count(x => x.Category == "closed");
            sb.AppendLine($"- {icName}: {stale} stale, {fresh} new, {closed} closed ({Math.Max(1,p.WindowDays)}d)");
        }
        sb.AppendLine();

        // Bulleted, most-recent PRs (created or closed in-window) per IC
        foreach (var icName in allICs)
        {
            sb.AppendLine($"## {icName}:");

            var ordered = recent
                .Where(x => x.Name == icName)
                .Select(x => new
                {
                    x.Category,
                    x.Title,
                    x.Link,
                    Created = Parse(x.Created),
                    Closed = Parse(x.Closed),
                    State = x.State
                })
                .OrderByDescending(x => (x.Closed > x.Created ? x.Closed : x.Created))
                .ToList();

            if (ordered.Count == 0)
            {
                sb.AppendLine("  (no recent PRs in window)");
                continue;
            }

            foreach (var s in ordered)
            {
                var date = (s.Closed > s.Created ? s.Closed : s.Created);
                var when = date == DateTime.MinValue ? "" : date.ToString("yyyy-MM-dd");
                var title = Utilities.TruncatePlain(s.Title ?? string.Empty, 110);

                sb.AppendLine($"Title: {title}");
                sb.AppendLine($"URL: {s.Link}");
                sb.AppendLine($"[{s.Category}] - {s.State} on {when}");
                sb.AppendLine();
            }
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
        if (profile is null) { return ToolResult.Failure($"No PRs Profile named '{p.ProfileName}'.", ctx); }

        var prs = Program.SubsystemManager.Get<PRsClient>();
        var table = await prs.FetchAsync(profile);

        // Use Table.Slice to exclude rows whose Category does not match the requested slice, then project via accessor
        var filteredTable = table.Slice("Category", val => !string.Equals(val, p.Slice, StringComparison.OrdinalIgnoreCase));
        var projected = filteredTable.SelectRows(a => new
        {
            Created = DateTime.TryParse(a("CreationDate"), out var created) ? created : DateTime.MaxValue,
            Title = a("Title"),
            Link = a("Link"),
            Author = a("CreatedByDisplayName"),
            State = a("Status")
        });
        var filtered = projected
                       .OrderBy(x => x.Created)
                       .Take(Math.Max(1, p.Limit))
                       .ToList();
        // For exports, use the underlying rows from filteredTable in the same ordering
        // Use Table helpers: order by CreationDate and then take the requested number of rows
        var orderedRows = filteredTable.OrderBy("CreationDate", cd => DateTime.TryParse(cd, out var dt) ? dt : DateTime.MaxValue)
                            .Take(Math.Max(1, p.Limit))
                            .Rows
                            .ToList();

        var lines = new List<string>();
        lines.Add($"Slice: {p.Slice} (limit {p.Limit})");
        foreach (var r in filtered)
        {
            lines.Add($"- {r.Created:MM/dd/yyyy} — {r.Link} - {Utilities.TruncatePlain(r.Author, 20)} [{r.State}] {Utilities.TruncatePlain(r.Title, 110)}");
        }
        var output = string.Join("\n", lines);

        ctx.AddToolMessage(output);
        await ContextManager.AddContent(output, $"prs/{profile.Name}/slice/{p.Slice}");

        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = Path.GetExtension(p.Export).ToLowerInvariant();
            // Build a small Table for export using same headers
            var exportTable = new Table(table.Headers, orderedRows);
            var content = ext switch
            {
                ".csv"  => exportTable.ToCsv(),
                ".json" => exportTable.ToJson(),
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
    // Internal typed projection for PR rows used by the coach tool
    private record PrRow(string Org, string Proj, string Repo, int Id, string Link, string AuthorUnique, DateTime Created);

    public string Description => "Pull ADO PR threads (newest first per IC) and produce attributed, linkable coaching suggestions.";
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
        var table = await prs.FetchAsync(profile);

        // Choose newest-first per IC: prefer "new", else "stale", else "closed"
        var newestTable = SelectNewest(table, "new");
        if (newestTable.Rows.Count == 0) newestTable = SelectNewest(table, "stale");
        if (newestTable.Rows.Count == 0) newestTable = SelectNewest(table, "closed");

        // Build per-IC typed dictionary using Table.GroupRowsBy<T>
        var perIc = newestTable.GroupRowsBy("CreatedByDisplayName", acc => {
            var org = acc("OrganizationName");
            var proj = acc("RepositoryProjectName");
            var repo = acc("RepositoryName");
            var id = int.TryParse(acc("PullRequestId"), out var idn) ? idn : 0;
            var link = acc("Link");
            var au = acc("CreatedByUniqueName");
            var created = DateTime.TryParse(acc("CreationDate"), out var dt) ? dt : DateTime.MinValue;
            return new PrRow(org, proj, repo, id, link, au, created);
        });

        var ado = Program.SubsystemManager.Get<AdoClient>();
        var sb = new StringBuilder();
        sb.AppendLine("Coaching Hints (by IC):");

        int printed = 0;
        foreach (var kv in perIc.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ic = kv.Key;
            var picks = kv.Value;
            var bullets = new List<string>();

            foreach (var pr in picks)
            {
                List<GitPullRequestCommentThread> threads;
                try
                {
                    // Pull all comment threads for this PR
                    threads = await ado.GetPullRequestThreadsAsync(pr.Proj, pr.Repo, pr.Id);
                }
                catch (Exception ex)
                {
                    bullets.Add($"PR {pr.Id}: (error fetching threads) {ex.Message}");
                    continue;
                }

                // Partition comments into "received" (others -> IC) and "given" (IC -> others).
                var received = new List<(int ThreadId, string Who, string Content)>();
                var given    = new List<(int ThreadId, string Who, string Content)>();

                foreach (var t in threads)
                {
                    var comments = t.Comments ?? new List<Comment>();
                    foreach (var c in comments)
                    {
                        if (string.IsNullOrWhiteSpace(c.Content)) continue;

                        var whoUnique  = c.Author?.UniqueName ?? "";
                        var whoDisplay = !string.IsNullOrWhiteSpace(c.Author?.DisplayName) ? c.Author!.DisplayName
                                        : (!string.IsNullOrWhiteSpace(whoUnique) ? whoUnique : "Reviewer");

                        // Heuristic: downrank/skip AI boilerplate comments
                        if (LooksAI(c.Content)) continue;

                        if (!string.IsNullOrWhiteSpace(pr.AuthorUnique) &&
                            whoUnique.Equals(pr.AuthorUnique, StringComparison.OrdinalIgnoreCase))
                        {
                            // Author of PR
                            given.Add((t.Id, whoDisplay, c.Content));
                        }
                        else
                        {
                            // Reviewers (received feedback)
                            received.Add((t.Id, whoDisplay, c.Content));
                        }
                    }
                }

                // If we filtered everything away as AI, allow a tiny fallback so the section isn't empty
                if (received.Count == 0)
                {
                    var any = threads.SelectMany(t => (t.Comments ?? new List<Comment>())
                                  .Where(c => !string.IsNullOrWhiteSpace(c.Content))
                                  .Select(c => (ThreadId: t.Id,
                                                Who: c.Author?.DisplayName ?? c.Author?.UniqueName ?? "Reviewer",
                                                Content: c.Content)))
                                  .Where(x => string.IsNullOrWhiteSpace(pr.AuthorUnique) ||
                                              !string.Equals(x.Who, pr.AuthorUnique, StringComparison.OrdinalIgnoreCase))
                                  .Take(2);
                    received.AddRange(any);
                }
                if (given.Count == 0)
                {
                    var any = threads.SelectMany(t => (t.Comments ?? new List<Comment>())
                                  .Where(c => !string.IsNullOrWhiteSpace(c.Content) &&
                                              string.Equals(c.Author?.UniqueName ?? "", pr.AuthorUnique ?? "",
                                                            StringComparison.OrdinalIgnoreCase))
                                  .Select(c => (ThreadId: t.Id,
                                                Who: c.Author?.DisplayName ?? c.Author?.UniqueName ?? "Author",
                                                Content: c.Content)))
                                  .Take(2);
                    given.AddRange(any);
                }

                // Pick representative examples (by “bucket” keywords) for received/given
                var recvExamples = PickExamples(received);
                var gaveExamples = PickExamples(given);

                // Build per-PR coaching summary for received/given feedback
                string recvThemes = SummarizeThemes(recvExamples.Select(e => e.Content));
                string gaveThemes = SummarizeThemes(gaveExamples.Select(e => e.Content), focusOnReviewerStyle:true);

                // PR URL in header (use Link from KQL; fallback to builder)
                var prUrl = !string.IsNullOrWhiteSpace(pr.Link)
                    ? pr.Link
                    : AdoClient.BuildPullRequestUrl(pr.Org, pr.Proj, pr.Repo, pr.Id);

                var block = new StringBuilder();
                block.AppendLine($"PR {pr.Id} — {prUrl}");

                if (!string.IsNullOrWhiteSpace(recvThemes))
                {
                    block.AppendLine("  Feedback on your PRs — themes:");
                    foreach (var line in recvThemes.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)))
                        block.AppendLine("  - " + line.Trim());
                }
                if (!string.IsNullOrWhiteSpace(gaveThemes))
                {
                    block.AppendLine("  Feedback you wrote — themes:");
                    foreach (var line in gaveThemes.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)))
                        block.AppendLine("  - " + line.Trim());
                }

                // Examples: received (reviewers) first, then given (author)
                if (recvExamples.Any())
                {
                    block.AppendLine("  Examples (what reviewers told you):");
                    foreach (var ex in recvExamples)
                    {
                        var tUrl = AdoClient.BuildDiscussionUrl(pr.Org, pr.Proj, pr.Repo, pr.Id, ex.ThreadId);
                        block.AppendLine($"    - **{ex.Who}:** \"{Trunc(ex.Content, 160)}\"  \n      ↳ {tUrl}");
                    }
                }
                if (gaveExamples.Any())
                {
                    block.AppendLine("  Examples (feedback you wrote):");
                    foreach (var ex in gaveExamples)
                    {
                        var tUrl = AdoClient.BuildDiscussionUrl(pr.Org, pr.Proj, pr.Repo, pr.Id, ex.ThreadId);
                        block.AppendLine($"    - **{ex.Who}:** \"{Trunc(ex.Content, 160)}\"  \n      ↳ {tUrl}");
                    }
                }

                bullets.Add(block.ToString());
            }

            // IC header + bullets (or “no items”)
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

        if (printed == 0)
        {
            sb.AppendLine("No relevant PRs with review discussion were found for this profile and window.");
        }

        var output = sb.ToString();
        ctx.AddToolMessage(output);
        await ContextManager.AddContent(output, $"prs/{profile.Name}/coach");
        return ToolResult.Success(output, ctx);

        // ===== local helpers =====

        Table SelectNewest(Table srcTable, string category)
        {
            // Use Table.LatestPerGroup to build a small table with newest per IC
            var selCols = new[] { "OrganizationName", "RepositoryProjectName", "RepositoryName", "PullRequestId", "Link", "CreatedByUniqueName", "CreationDate" };
            return srcTable.LatestPerGroup("CreatedByDisplayName", "Category", category, "CreationDate", Math.Max(1, p.MaxPerIC), selCols);
        }

        static bool LooksAI(string content)
        {
            var s = content.Trim().ToLowerInvariant();
            // crude but effective filters for AI boilerplate
            return s.Contains("ai description") ||
                   s.Contains("generated by") ||
                   s.Contains("copilot") ||
                   s.Contains("chatgpt") ||
                   s.StartsWith("ai:") ||
                   s.StartsWith("[ai]");
        }

        static string Trunc(string s, int n)
        {
            var t = s.Replace("\r", " ").Replace("\n", " ");
            if (t.Length <= n) return t;
            return t.Substring(0, Math.Max(0, n - 1)) + "…";
        }

        // Score comments by lightweight buckets to pick representative examples
        static IEnumerable<(int ThreadId, string Who, string Content)> PickExamples(IEnumerable<(int ThreadId, string Who, string Content)> items)
        {
            var buckets = new (string Tag, string[] Keys)[] {
                ("tests", new[] {"test", "unit", "coverage", "flaky"}),
                ("size",  new[] {"too big", "scope", "split", "refactor"}),
                ("ci",    new[] {"ci", "build", "pipeline", "fail", "flake"}),
                ("perf",  new[] {"perf", "latency", "alloc", "hot path"}),
                ("docs",  new[] {"doc", "comment", "readme", "description"}),
                ("style", new[] {"nit", "style", "naming", "format", "convention"}),
                ("merge", new[] {"merge conflict", "rebase", "out of date"})
            };

            var scored = items.Select(x => {
                                var lower = x.Content.ToLowerInvariant();
                                var (tag, score) = buckets
                                    .Select(b => (b.Tag, Score: b.Keys.Count(k => lower.Contains(k))))
                                    .OrderByDescending(t => t.Score)
                                    .FirstOrDefault();
                                return (x.ThreadId, x.Who, x.Content, tag, score);
                            })
                            .Where(s => s.score > 0);

            if (scored.Any())
            {
                return scored.GroupBy(s => s.tag)
                             .OrderByDescending(g => g.Max(s => s.score))
                             .Take(3)
                             .Select(g => {
                                 var best = g.OrderByDescending(s => s.score).First();
                                 return (best.ThreadId, best.Who, best.Content);
                             });
            }

            // Fallback: first few raw comments
            return items.Take(3);
        }

        // Summarize themes deterministically; if model available, use it; else simple heuristics
        string SummarizeThemes(IEnumerable<string> comments, bool focusOnReviewerStyle = false) => Log.Method(ctx =>
        {
            ctx.OnlyEmitOnFailure();
            var text = string.Join("\n", comments.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(text))
            {
                ctx.Warn("No text to summarize");
                return "";
            }

            try
            {
                var prompt = focusOnReviewerStyle
                    ? "You are an EM reviewing how an engineer gives feedback in PRs. In 3-5 bullets, summarize strengths and improvement areas in their review comments. Prefer concrete, actionable guidance. Output only bullets."
                    : "You are an EM coaching an engineer based on review feedback they received on their PRs. In 3-5 bullets, summarize recurring themes with actionable guidance. Output only bullets.";
                var summary = Engine.Provider!.PostChatAsync(new Context(prompt + "\n\n" + Utilities.TruncatePlain(text, 4000)), 0.2f).GetAwaiter().GetResult();
                ctx.Succeeded();
                return summary.Trim();
            }
            catch (Exception ex)
            {
                ctx.Failed("Failed to summarize themes", ex);
                // Non-LLM fallback: keyword tallies → bullets
                var l = text.ToLowerInvariant();
                var bullets = new List<string>();
                if (l.Contains("test")) bullets.Add("Increase test coverage and include edge cases.");
                if (l.Contains("nit") || l.Contains("style")) bullets.Add("Reduce style/nit churn; batch fixes and follow guidelines.");
                if (l.Contains("rebase") || l.Contains("merge")) bullets.Add("Rebase regularly to avoid merge conflicts.");
                if (l.Contains("perf")) bullets.Add("Watch for perf risk in hot paths.");
                if (l.Contains("ci") || l.Contains("build")) bullets.Add("Keep CI/build green; address failures promptly.");
                return string.Join("\n", bullets);
            }
        });
    }
}