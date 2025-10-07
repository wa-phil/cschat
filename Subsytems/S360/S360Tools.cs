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
        var table = await s360.FetchAsync(profile);

        // filter and format the table output to something more easily digested by humans.
        var projected = Table.FromEnumerable(table.SelectRows(a => new
        {
            Service = a("ServiceName"),
            Title = a("KpiTitle"),
            ActionItem = a("ActionItemTitle"),
            DueDate = a("CurrentDueDate"),
            State = a("SLAState"),
            Eta = a("CurrentETA"),
            URL = a("URL"),
            Description = a("KpiDescriptionHtml")
        }));

        Program.ui.RenderTable(projected, "active S360 items");
        await ContextManager.AddContent(table.ToCsv(), $"s360/{profile.Name}/results");

        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = Path.GetExtension(p.Export).ToLowerInvariant();
            var content = ext switch {
                ".json" => table.ToJson(),
                _       => table.ToCsv()
            };
            File.WriteAllText(p.Export!, content);
            ctx.AddToolMessage($"Saved: {p.Export}");
        }
        return ToolResult.Success($"{profile.Name}: returned {table.Rows.Count} rows", ctx);
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
        var table = await s360.FetchAsync(profile); // <-- built-in Kusto
        var scored = s360.Score(table, profile);

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

        var outTable = new Table(headers, outRows);
        Program.ui.RenderTable(outTable, "S360 Results");
        ctx.AddToolMessage(outTable.ToCsv());

        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = Path.GetExtension(p.Export).ToLowerInvariant();
            var content = ext switch {
                ".json" => outTable.ToJson(),
                _       => outTable.ToCsv()
            };
            File.WriteAllText(p.Export!, content);
            ctx.AddToolMessage($"Saved: {p.Export}");
        }
        return ToolResult.Success($"{profile.Name}: returned {outTable.Rows.Count} rows", ctx);
    }
}
