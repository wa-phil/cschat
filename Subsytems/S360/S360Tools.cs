using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public sealed class FetchS360Input
{
    [UserField(required:true, display:"Profile Name")] public string ProfileName { get; set; } = "";
    [UserField(display:"Export Path (.csv/.json)")]   public string? Export { get; set; }
}

[IsConfigurable("tool.s360.fetch")]
public sealed class S360FetchTool : ITool
{
    public string Description => "Fetch active S360 action items for a profile and cache results.";
    public string Usage       => "S360Fetch({ \"ProfileName\":\"Deployment\" })";
    public Type   InputType   => typeof(FetchS360Input);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ProfileName\":{\"type\":\"string\"},\"Export\":{\"type\":\"string\"}},\"required\":[\"ProfileName\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (FetchS360Input)input;
        var profile = Program.userManagedData.GetItems<S360Profile>()
            .FirstOrDefault(x => x.Name.Equals(p.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ToolResult.Failure($"No S360 Profile named '{p.ProfileName}'.", ctx);

        var s360 = Program.SubsystemManager.Get<S360Client>();
        var (cols, rows) = await s360.FetchAsync(profile);  // <-- built-in Kusto

        var table = KustoClient.ToTable(cols, rows, Console.WindowWidth);
        ctx.AddToolMessage(table);
        await ContextManager.AddContent(table, $"s360/{profile.Name}/results");

        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = Path.GetExtension(p.Export).ToLowerInvariant();
            var content = ext switch {
                ".csv"  => KustoClient.ToCsv(cols, rows),
                ".json" => KustoClient.ToJson(cols, rows),
                _       => table
            };
            File.WriteAllText(p.Export!, content);
            ctx.AddToolMessage($"Saved: {p.Export}");
        }
        return ToolResult.Success(table, ctx);
    }
}

public sealed class TriageS360Input
{
    [UserField(required:true, display:"Profile Name")] public string ProfileName { get; set; } = "";
    [UserField(display:"Top N for action plan")]      public int TopN { get; set; } = 15;
}

[IsConfigurable("tool.s360.triage")]
public sealed class S360TriageTool : ITool
{
    public string Description => "Score and summarize S360 items for managers (briefing + action plan).";
    public string Usage       => "S360Triage({ \"ProfileName\":\"Deployment\", \"TopN\":20 })";
    public Type   InputType   => typeof(TriageS360Input);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ProfileName\":{\"type\":\"string\"},\"TopN\":{\"type\":\"integer\"}},\"required\":[\"ProfileName\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (TriageS360Input)input;
        var profile = Program.userManagedData.GetItems<S360Profile>()
            .FirstOrDefault(x => x.Name.Equals(p.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ToolResult.Failure($"No S360 Profile named '{p.ProfileName}'.", ctx);

        var s360 = Program.SubsystemManager.Get<S360Client>();
        var (cols, rows) = await s360.FetchAsync(profile); // <-- built-in Kusto
        var scored = s360.Score(cols, rows, profile);

        // Manager Briefing (numeric snapshot; LLM-free so you always get something useful)
        var grouped = scored
            .GroupBy(x => x.Row.ServiceName)
            .Select(g => new {
                Service = g.Key,
                Total = g.Count(),
                AtRisk = g.Count(x => x.Factors.ContainsKey("slaAtRisk")),
                MissingEta = g.Count(x => x.Factors.ContainsKey("missingEta"))
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        var bulletLines = grouped.Select(x => $"- {x.Service}: {x.Total} items, {x.AtRisk} at-risk, {x.MissingEta} missing ETA");
        var briefingBlock = "Manager Briefing:\n" + string.Join("\n", bulletLines);

        // Action plan → grouped by service, items ordered by DUE (soonest first), with LLM summaries/next-steps
        var top = scored.Take(Math.Max(1, p.TopN)).ToList();
        string plan;
        try
        {
            var planPrompt = S360Insights.MakeGroupedActionPlanPrompt(top);
            var planCtx = new Context(planPrompt);
            plan = await Engine.Provider!.PostChatAsync(planCtx, 0.2f);
        }
        catch (Exception ex)
        {
            // Fallback: show a deterministic non-LLM plan if the model call fails
            var bySvc = top.GroupBy(x => x.Row.ServiceName);
            var lines = new List<string>();
            foreach (var g in bySvc)
            {
                lines.Add($"Service: {g.Key}");
                foreach (var x in g.OrderBy(xx => ParseDate(xx.Row.CurrentDueDate) ?? DateTime.MaxValue))
                {
                    var r = x.Row;
                    var title = string.IsNullOrWhiteSpace(r.ActionItemTitle) ? r.KpiTitle : r.ActionItemTitle;
                    var eta = string.IsNullOrWhiteSpace(r.CurrentETA) ? "—" : r.CurrentETA.Trim();
                    var due = DateTime.TryParse(r.CurrentDueDate, out var dd) ? dd.ToString("yyyy-MM-dd") : "—";
                    var owner = string.IsNullOrWhiteSpace(r.AssignedTo) ? "Unassigned" : r.AssignedTo.Trim();
                    lines.Add($"  - {title} [{r.SLAState}] (Due: {due} | ETA: {eta} | Owner: {owner})");
                    var summary = Utilities.TruncatePlain(Utilities.StripHtml(r.KpiDescriptionHtml ?? string.Empty), 240);
                    if (!string.IsNullOrWhiteSpace(summary)) lines.Add($"    Summary: {summary}");
                    lines.Add($"    Next: set ETA / assign owner / update status");
                    lines.Add($"    Links:");
                    lines.Add($"      - S360 item — {r.URL}");
                }
            }
            lines.Add($"[LLM action-plan failed: {ex.Message}]");
            plan = string.Join("\n", lines);
        }

        var output = $"{briefingBlock}\n\nAction Plan (grouped by service, top {top.Count}):\n{plan}";
        ctx.AddToolMessage(output);
        await ContextManager.AddContent(output, $"s360/{profile.Name}/triage");
        return ToolResult.Success(output, ctx);

        static DateTime? ParseDate(string s)
            => DateTime.TryParse(s, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : (DateTime?)null;
    }
}

public sealed class SliceS360Input
{
    [UserField(required:true, display:"Profile Name")] public string ProfileName { get; set; } = "";
    [UserField(required:true, display:"Slice (stale|no-eta|due-soon|needs-owner|at-risk-sla|delegated|churny-eta|off-track-wave|burndown-negative)")]
    public string Slice { get; set; } = "stale";
    public int    Limit  { get; set; } = 25;
    public string? Export { get; set; }
}

[IsConfigurable("tool.s360.slice")]
public sealed class S360SliceTool : ITool
{
    public string Description => "Filter S360 results by slice (stale/no-eta/due-soon/etc.) and print/export a focused list.";
    public string Usage       => "S360Slice({ \"ProfileName\":\"Deployment\", \"Slice\":\"due-soon\", \"Limit\":20 })";
    public Type   InputType   => typeof(SliceS360Input);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ProfileName\":{\"type\":\"string\"},\"Slice\":{\"type\":\"string\"},\"Limit\":{\"type\":\"integer\"},\"Export\":{\"type\":\"string\"}},\"required\":[\"ProfileName\",\"Slice\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (SliceS360Input)input;
        var profile = Program.userManagedData.GetItems<S360Profile>()
            .FirstOrDefault(x => x.Name.Equals(p.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ToolResult.Failure($"No S360 Profile named '{p.ProfileName}'.", ctx);

        var s360 = Program.SubsystemManager.Get<S360Client>();
        var (cols, rows) = await s360.FetchAsync(profile); // <-- built-in Kusto
        var scored = s360.Score(cols, rows, profile);

        bool IsStale((S360Client.S360Row Row, float Score, Dictionary<string,float> Factors) x)
        {
            var changed = DateTime.TryParse(x.Row.LastUpdateTime, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.MinValue;
            return changed != DateTime.MinValue && (DateTime.UtcNow - dt).TotalDays > profile.FreshDays;
        }

        IEnumerable<(S360Client.S360Row Row, float Score, Dictionary<string,float> Factors)> slice = p.Slice.ToLowerInvariant() switch
        {
            "stale"             => scored.Where(IsStale),
            "no-eta"            => scored.Where(x => string.IsNullOrWhiteSpace(x.Row.CurrentETA)),
            "due-soon"          => scored.Where(x => { var d = DateTime.TryParse(x.Row.CurrentDueDate, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.MaxValue; return (d - DateTime.UtcNow).TotalDays <= profile.SoonDays; }),
            "needs-owner"       => scored.Where(x => string.IsNullOrWhiteSpace(x.Row.AssignedTo)),
            "at-risk-sla"       => scored.Where(x => !string.Equals(x.Row.SLAState, "OnTrack", StringComparison.OrdinalIgnoreCase)),
            "delegated"         => scored.Where(x => !string.IsNullOrWhiteSpace(x.Row.DelegatedAssignedTo)),
            "churny-eta"        => scored.Where(x => int.TryParse(x.Row.ETACount, out var ec) && ec >= 3),
            "off-track-wave"    => scored.Where(x => x.Factors.ContainsKey("offTrackWave")),
            "burndown-negative" => scored.Where(x => x.Factors.ContainsKey("burndownNonImproving")),
            _                   => scored
        };

        var top = slice.Take(Math.Max(1, p.Limit)).ToList();
        var headers = new[]{"Service","Title","SLA","ETA","Due","Assignee","Delegated","Score","Signals","URL"};
        var outRows = top.Select(x => new[]{
            x.Row.ServiceName,
            string.IsNullOrWhiteSpace(x.Row.ActionItemTitle) ? x.Row.KpiTitle : x.Row.ActionItemTitle,
            x.Row.SLAState,
            x.Row.CurrentETA,
            x.Row.CurrentDueDate,
            x.Row.AssignedTo,
            x.Row.DelegatedAssignedTo,
            x.Score.ToString("0.0"),
            string.Join(",", x.Factors.Keys),
            x.Row.URL
        }).ToList();

        var table = KustoClient.ToTable(headers, outRows, Console.WindowWidth);
        ctx.AddToolMessage(table);

        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = Path.GetExtension(p.Export).ToLowerInvariant();
            var content = ext switch {
                ".csv"  => KustoClient.ToCsv(headers, outRows),
                ".json" => KustoClient.ToJson(headers, outRows),
                _       => table
            };
            File.WriteAllText(p.Export!, content);
            ctx.AddToolMessage($"Saved: {p.Export}");
        }
        return ToolResult.Success(table, ctx);
    }
}
