using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;


[IsConfigurable("S360")]
[DependsOn("Kusto")]
public class S360Client : ISubsystem
{
    // S360Client.cs (top-level in class)
    private const string S360_CLUSTER = "https://s360prodro.kusto.windows.net";
    private const string S360_DATABASE = "service360db";
    private const int    S360_TIMEOUT_SECONDS = 30;

    // Build a transient KustoConfig so we don't rely on UMD
    private static KustoConfig BuiltInKustoConfig() => new KustoConfig {
        Name = "S360 (built-in)",
        ClusterUri = S360_CLUSTER,
        Database = S360_DATABASE,
        AuthMode = KustoAuthMode.devicecode,
        DefaultTimeoutSeconds = S360_TIMEOUT_SECONDS
    };

    public bool IsAvailable => true;

    private bool _active = false;

    // Replace your current IsEnabled with this:
    public bool IsEnabled
    {
        get => _active;
        set
        {
            if (value && !_active)
            {
                _active = true;
                Register();      // adds the S360 command group
            }
            else if (!value && _active)
            {
                Unregister();    // removes the S360 command group
                _active = false;
            }
        }
    }

    public Type ConfigType => typeof(S360Profile);

    public void Register() => Program.commandManager.SubCommands.Add(S360Commands.Commands(this));

    public void Unregister() => Program.commandManager.SubCommands.RemoveAll(c => c.Name.Equals("S360", StringComparison.OrdinalIgnoreCase));

    // KQL for S360 items
    public static string BuildActionItemsKql(IEnumerable<Guid> serviceIds)
    {
        var idList = string.Join(", ", serviceIds.Select(g => $"'{g:D}'"));
        return @"
let S360KpiMetadata =
S360_Kpi_Metadata
| partition hint.strategy=native by KpiId ( top 1 by CreatedDate )
| project KpiId, KpiType, CreatedBy, DisplayName, KpiDescriptionHtml = base64_decode_tostring(Description);
GetActiveActionItems()
| join kind=inner S360_ServiceDetails_RT on $left.TargetId == $right.ServiceId
| join kind=inner S360KpiMetadata on $left.ActionItemId == $right.KpiId
| where TargetId in~ (" + idList + @")
| project
ServiceName,
KpiTitle = DisplayName,
KpiOwnerAlias = CreatedBy,
ActionItemTitle = Title,
WavesMetadata = tostring(parse_json(CustomDimensions)['S360_WavesMetadata']),
OriginalPublishTime, LastUpdateTime,
OriginalDueDate, CurrentDueDate,
SLAState, CurrentETA, ETACount,
CurrentETAAuthor, CurrentStatus, CurrentStatusAuthor,
KpiId, KpiType, ActionItemId,
S360Id = ID, URL, ServiceId, AssignedTo, DelegatedAssignedTo, KpiDescriptionHtml
| order by ServiceName asc, KpiTitle asc, ActionItemTitle asc, CurrentETA, CurrentDueDate";
    }

    public async Task<Table> FetchAsync(S360Profile profile)
    {
        var kusto = Program.SubsystemManager.Get<KustoClient>();
        var kql = BuildActionItemsKql(profile.ServiceIds);

        // 1) Prefer a connected UMD config that matches the S360 endpoint (if present)
        var umdCfg = Program.userManagedData.GetItems<KustoConfig>()
            .FirstOrDefault(c => UriEquals(c.ClusterUri, S360_CLUSTER) && c.Database.Equals(S360_DATABASE, StringComparison.OrdinalIgnoreCase));

        if (umdCfg is not null && kusto.IsConnected(umdCfg.Name))
        {
            return await kusto.QueryAsync(umdCfg, kql);
        }

        // 2) Otherwise, ensure-connect the built-in cfg and use it
        var built = BuiltInKustoConfig();
        await kusto.EnsureConnectedAsync(built);
        return await kusto.QueryAsync(built, kql);

        static bool UriEquals(string a, string b)
        {
            if (Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub))
                return Uri.Compare(ua, ub, UriComponents.AbsoluteUri, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
            return string.Equals(a?.TrimEnd('/'), b?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }
    }

    // Row projection
    public sealed class S360Row
    {
        public string ServiceName = "", KpiTitle = "", ActionItemTitle = "", SLAState = "", URL = "";
        public string AssignedTo = "", DelegatedAssignedTo = "", CurrentETA = "", CurrentStatus = "";
        public string KpiType = "", KpiOwnerAlias = "", WavesMetadata = "";
        public string LastUpdateTime = "", CurrentDueDate = "", ETACount = "";
        public string KpiDescriptionHtml = "";
    }

    // Lightweight model we use for wave analysis
    public sealed class WaveSnapshot
    {
        public DateTime? StartDate;
        public DateTime? EndDate;
        public int? Total;
        public int? Completed;
        public int? Remaining;
        public List<(DateTime when, int remaining)> History = new();
    }

    private static DateTime ParseUtcOrMin(string s) => DateTime.TryParse(s, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.MinValue;

    private static WaveSnapshot ParseWave(string json, int minPoints)
    {
        var wave = new WaveSnapshot();
        if (string.IsNullOrWhiteSpace(json)) return wave;

        try
        {
            // TinyJson returns Dictionary<string, object> / List<object> graphs
            var root = json.FromJson<object>();
            object obj = root!;
            if (root is List<object> arr && arr.Count > 0) obj = arr[0];

            if (obj is Dictionary<string, object> d)
            {
                string? GetS(Dictionary<string, object> map, params string[] names)
                {
                    foreach (var n in names)
                        if (map.TryGetValue(n, out var v) && v != null)
                            return v.ToString();
                    return null;
                }
                int? GetI(Dictionary<string, object> map, params string[] names)
                {
                    var s = GetS(map, names);
                    return (s != null && int.TryParse(s, out var val)) ? val : null;
                }
                DateTime? GetD(Dictionary<string, object> map, params string[] names)
                {
                    var s = GetS(map, names);
                    if (s != null && DateTime.TryParse(s, out var v)) return DateTime.SpecifyKind(v, DateTimeKind.Utc);
                    return null;
                }
                object? GetAny(Dictionary<string, object> map, params string[] names)
                {
                    foreach (var n in names)
                        if (map.TryGetValue(n, out var v) && v != null) return v;
                    return null;
                }

                wave.Total     = GetI(d, "Total","total","planned","plannedItems");
                wave.Completed = GetI(d, "Completed","completed","done");
                wave.Remaining = GetI(d, "Remaining","remaining","open");
                wave.StartDate = GetD(d, "StartDate","start","plannedStart");
                wave.EndDate   = GetD(d, "EndDate","end","plannedEnd","TargetDate");

                var hist = GetAny(d, "History","history","burndown","checkpoints");
                if (hist is List<object> ha)
                {
                    foreach (var p in ha)
                    {
                        if (p is Dictionary<string, object> ho)
                        {
                            var ws = GetS(ho, "when","date","timestamp");
                            var rs = GetS(ho, "remaining","open","count");
                            if (ws != null && DateTime.TryParse(ws, out var wdt) && rs != null && int.TryParse(rs, out var rem))
                                wave.History.Add((DateTime.SpecifyKind(wdt, DateTimeKind.Utc), rem));
                        }
                    }
                    wave.History = wave.History.OrderBy(x => x.when).ToList();
                }
            }
        }
        catch
        {
            // best-effort parse
        }

        return wave;
    }

    private static float BurndownSlope(List<(DateTime when, int remaining)> points)
    {
        if (points == null || points.Count < 2) return 0f;

        // Sort by time and skip duplicate timestamps
        points.Sort((a, b) => a.when.CompareTo(b.when));
        var t0 = points[0].when;

        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        int n = 0;
        DateTime lastWhen = DateTime.MinValue;

        foreach (var (when, remaining) in points)
        {
            if (when == lastWhen) continue;
            lastWhen = when;

            var x = (when - t0).TotalDays; // days since first point
            var y = (double)remaining;

            sumX += x;
            sumY += y;
            sumXX += x * x;
            sumXY += x * y;
            n++;
        }

        if (n < 2) return 0f;

        var denom = (n * sumXX) - (sumX * sumX);
        if (Math.Abs(denom) < 1e-9) return 0f; // vertical line / no time delta

        var slope = ((n * sumXY) - (sumX * sumY)) / denom; // remaining per day
        return (float)slope;
    }

    
    public List<(S360Row Row, float Score, Dictionary<string, float> Factors)> Score(
        Table table, S360Profile p)
    {
        int C(string name) => table.Col(name);
        var cols = table.Headers;
        var rows = table.Rows;
        var idx = new Dictionary<string, int>
        {
            ["ServiceName"] = C("ServiceName"),
            ["KpiTitle"] = C("KpiTitle"),
            ["ActionItemTitle"] = C("ActionItemTitle"),
            ["SLAState"] = C("SLAState"),
            ["URL"] = C("URL"),
            ["AssignedTo"] = C("AssignedTo"),
            ["DelegatedAssignedTo"] = C("DelegatedAssignedTo"),
            ["CurrentETA"] = C("CurrentETA"),
            ["CurrentStatus"] = C("CurrentStatus"),
            ["KpiType"] = C("KpiType"),
            ["KpiOwnerAlias"] = C("KpiOwnerAlias"),
            ["LastUpdateTime"] = C("LastUpdateTime"),
            ["CurrentDueDate"] = C("CurrentDueDate"),
            ["ETACount"] = C("ETACount"),
            ["WavesMetadata"] = C("WavesMetadata"),
            ["KpiDescriptionHtml"] = C("KpiDescriptionHtml"),
        };

        var now = DateTime.UtcNow;
        var scored = new List<(S360Row Row, float Score, Dictionary<string, float> Factors)>();

        foreach (var r in rows)
        {
            var row = new S360Row
            {
                ServiceName = r.SafeGet(idx, "ServiceName"),
                KpiTitle = r.SafeGet(idx, "KpiTitle"),
                ActionItemTitle = r.SafeGet(idx, "ActionItemTitle"),
                SLAState = r.SafeGet(idx, "SLAState"),
                URL = r.SafeGet(idx, "URL"),
                AssignedTo = r.SafeGet(idx, "AssignedTo"),
                DelegatedAssignedTo = r.SafeGet(idx, "DelegatedAssignedTo"),
                CurrentETA = r.SafeGet(idx, "CurrentETA"),
                CurrentStatus = r.SafeGet(idx, "CurrentStatus"),
                KpiType = r.SafeGet(idx, "KpiType"),
                KpiOwnerAlias = r.SafeGet(idx, "KpiOwnerAlias"),
                WavesMetadata = r.SafeGet(idx, "WavesMetadata"),
                LastUpdateTime = r.SafeGet(idx, "LastUpdateTime"),
                CurrentDueDate = r.SafeGet(idx, "CurrentDueDate"),
                ETACount = r.SafeGet(idx, "ETACount"),
                KpiDescriptionHtml = r.SafeGet(idx, "KpiDescriptionHtml"),
            };

            var f = new Dictionary<string, float>();
            float s = 0;

            // Recent change
            var changed = ParseUtcOrMin(row.LastUpdateTime);
            if (changed != DateTime.MinValue && (now - changed).TotalDays <= p.FreshDays) { f["recentChange"] = p.W_RecentChange; s += p.W_RecentChange; }

            // Due soon
            var due = ParseUtcOrMin(row.CurrentDueDate);
            if (due != DateTime.MinValue && (due - now).TotalDays <= p.SoonDays) { f["dueSoon"] = p.W_DueSoon; s += p.W_DueSoon; }

            // SLA at risk
            if (!string.IsNullOrWhiteSpace(row.SLAState) && !row.SLAState.Equals("OnTrack", StringComparison.OrdinalIgnoreCase))
            {
                f["slaAtRisk"] = p.W_SlaAtRisk; s += p.W_SlaAtRisk;
            }

            // missing ETA
            if (string.IsNullOrWhiteSpace(row.CurrentETA)) { f["missingEta"] = p.W_MissingEta; s += p.W_MissingEta; }

            // churny ETA
            if (int.TryParse(row.ETACount, out var ec) && ec >= 3) { f["manyEtaChanges"] = p.W_ManyEtaChgs; s += p.W_ManyEtaChgs; }

            // ownership
            if (string.IsNullOrWhiteSpace(row.AssignedTo)) { f["unassigned"] = p.W_Unassigned; s += p.W_Unassigned; }
            if (!string.IsNullOrWhiteSpace(row.DelegatedAssignedTo)) { f["delegated"] = p.W_Delegated; s += p.W_Delegated; }

            // Waves: off-track & burndown
            var wave = ParseWave(row.WavesMetadata, p.BurnDownMinPoints);
            if (wave.EndDate.HasValue && wave.Remaining.HasValue)
            {
                if ((now - wave.EndDate.Value).TotalDays > p.OffTrackGraceDays && wave.Remaining.Value > 0)
                {
                    f["offTrackWave"] = p.W_OffTrackWave; s += p.W_OffTrackWave;
                }
            }
            if (wave.History.Count >= p.BurnDownMinPoints)
            {
                var slope = BurndownSlope(wave.History);
                if (slope >= 0)
                {
                    f["burndownNonImproving"] = p.W_BurnDownNeg; s += p.W_BurnDownNeg;
                }
            }

            scored.Add((row, s, f));
        }

        return scored
            .OrderByDescending(x => x.Item2)
            .ThenBy(x =>
            {
                var d = ParseUtcOrMin(x.Row.CurrentDueDate);
                return d == DateTime.MinValue ? DateTime.MaxValue : d;
            })
            .ToList();
    }
}

public static class S360RowExtensions
{
    public static string SafeGet(this string[] r, Dictionary<string,int> idx, string name) => (idx.TryGetValue(name, out var i) && i >= 0 && i < r.Length) ? r[i] : string.Empty;
}