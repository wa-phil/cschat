using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public sealed class KustoRefreshConnectionsInput { }

[IsConfigurable("tool.kusto.refresh")]
public sealed class KustoRefreshConnectionsTool : ITool
{
    public string Description => "Connect/reconnect to all User-Managed Kusto configs and report failures.";
    public string Usage => "KustoRefreshConnections()";
    public Type InputType => typeof(KustoRefreshConnectionsInput);
    public string InputSchema => "{}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        await Task.Yield(); // keep async happy
    var kusto = Program.SubsystemManager.Get<KustoClient>();
    var (failures, added) = kusto.RefreshConnections();

    var connected = string.Join(", ", kusto.GetConnectedConfigNames().OrderBy(s => s));
    var msg = $"Kusto connections updated: {added} new/updated.\n" +
          $"Connected: {(string.IsNullOrWhiteSpace(connected) ? "(none)" : connected)}\n" +
          (failures.Count > 0 ? $"Failed: {string.Join(", ", failures)} (check logs)\n" : "");

        ctx.AddToolMessage(msg);
        return ToolResult.Success(msg, ctx);
    }
}

[IsConfigurable("tool.kusto.list_configs")]
public sealed class ListKustoConfigsTool : ITool
{
    public string Description => "List user-managed Kusto configurations.";
    public string Usage => "ListKustoConfigs()";
    public Type InputType => typeof(NoInput);
    public string InputSchema => "{}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        await Task.Yield(); // keep async happy
        var configs = Program.userManagedData.GetItems<KustoConfig>();
        var lines = configs.Select(c => $"- {c.Name} @ {c.ClusterUri} | {c.Database} (queries: {c.Queries.Count})");
        return ToolResult.Success(string.Join("\n", lines), ctx);
    }
}

public sealed class IntrospectKustoSchemaInput
{
    [UserField(required: true, display: "Config Name")] public string ConfigName { get; set; } = "";
}

[IsConfigurable("tool.kusto.introspect_schema")]
public sealed class IntrospectKustoSchemaTool : ITool
{
    public string Description => "Fetch tables/columns/types for a KustoConfig and cache them for the chat engine.";
    public string Usage => "IntrospectKustoSchema({ \"ConfigName\": \"Prod\" })";
    public Type InputType => typeof(IntrospectKustoSchemaInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ConfigName\":{\"type\":\"string\"}},\"required\":[\"ConfigName\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (IntrospectKustoSchemaInput)input;
        var umd = Program.userManagedData; // or DI IUserManagedStore if you refactor later

        var cfg = umd.GetItems<KustoConfig>()
                     .FirstOrDefault(c => c.Name.Equals(p.ConfigName, StringComparison.OrdinalIgnoreCase));
        if (cfg == null)
            return ToolResult.Failure($"No KustoConfig named '{p.ConfigName}'.", ctx);

        var kusto = Program.SubsystemManager.Get<KustoClient>();

        // Use Kusto to query schema for this DB
        // `.show database ['db'] schema` is explicit; some clusters also support `.show schema`
        var text = cfg.Database.Replace("'", "''");
        var kql = $".show database ['{text}'] schema";
        var (cols, rows) = await kusto.QueryAsync(cfg, kql);

        // Normalize: tables -> columns -> types
        // Common columns for this command typically include: TableName, ColumnName, ColumnType
        int idxTable = Array.FindIndex(cols.ToArray(), c => c.Equals("TableName", StringComparison.OrdinalIgnoreCase));
        int idxCol   = Array.FindIndex(cols.ToArray(), c => c.Equals("ColumnName", StringComparison.OrdinalIgnoreCase));
        int idxType  = Array.FindIndex(cols.ToArray(), c => c.Equals("ColumnType", StringComparison.OrdinalIgnoreCase));

        var tables = new Dictionary<string, List<(string col, string type)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var t = (idxTable >= 0 && idxTable < r.Length) ? r[idxTable] : "";
            var c = (idxCol   >= 0 && idxCol   < r.Length) ? r[idxCol]   : "";
            var ty= (idxType  >= 0 && idxType  < r.Length) ? r[idxType]  : "";
            if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(c)) continue;
            if (!tables.TryGetValue(t, out var list)) tables[t] = list = new();
            list.Add((c, ty));
        }

        // Cache JSON in the UMD object and persist
        var schemaDto = new
        {
            database = cfg.Database,
            tables = tables.Select(kv => new {
                name = kv.Key,
                columns = kv.Value.Select(x => new { name = x.col, type = x.type }).ToList()
            }).OrderBy(t => t.name).ToList()
        };

        // persist schemaDto in RAG
        await ContextManager.AddContent(schemaDto.ToJson(), $"kusto/{cfg.Name}/schema");

        // Seed a readable sketch into the conversation context for RAG-assisted KQL authoring
        var sketchLines = new List<string> { $"Kusto schema for {cfg.Name} ({cfg.Database}):" };
        foreach (var t in tables.OrderBy(kv => kv.Key))
        {
            var colItems = t.Value.OrderBy(v => v.col).Select(v => $"{v.col}:{v.type}");
            var wrapped = string.Join("\n    ", WrapColumns(colItems, perLine: 8));
            sketchLines.Add($"- {t.Key}:\n    {wrapped}");
        }
        var sketch = string.Join("\n", sketchLines);
        await ContextManager.AddContent(sketch, $"kusto/{cfg.Name}/schema");

        var msg = $"Cached schema for '{cfg.Name}'. Added RAG context key: kusto/{cfg.Name}/schema";
        ctx.AddToolMessage(msg);
        return ToolResult.Success(msg, ctx);
    }
    
    static IEnumerable<string> WrapColumns(IEnumerable<string> cols, int perLine = 8)
    {
        var block = new List<string>(perLine);
        foreach (var c in cols)
        {
            block.Add(c);
            if (block.Count == perLine)
            {
                yield return string.Join(", ", block);
                block.Clear();
            }
        }
        if (block.Count > 0) yield return string.Join(", ", block);
    }
}

public sealed class RunSavedKustoQueryInput
{
    [UserField(required: true, display: "Config Name")]
    public string ConfigName { get; set; } = "";

    [UserField(required: true, display: "Query Name")]
    public string QueryName { get; set; } = "";

    [UserField(display: "Export Path", hint: "Optional .csv or .json")]
    public string? Export { get; set; }
}

[IsConfigurable("tool.kusto.run_saved_query")]
public sealed class RunSavedKustoQueryTool : ITool
{
    public string Description => "Run a user-saved KQL query stored under a specific KustoConfig.";
    public string Usage => "RunSavedKustoQuery({ \"ConfigName\":\"Prod\", \"QueryName\":\"TopErrors24h\", \"Export\":\"c:/tmp/out.csv\" })";
    public Type InputType => typeof(RunSavedKustoQueryInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ConfigName\":{\"type\":\"string\"},\"QueryName\":{\"type\":\"string\"},\"Export\":{\"type\":\"string\"}},\"required\":[\"ConfigName\",\"QueryName\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (RunSavedKustoQueryInput)input;
        var cfg = Program.userManagedData
            .GetItems<KustoConfig>()
            .FirstOrDefault(c => c.Name.Equals(p.ConfigName, StringComparison.OrdinalIgnoreCase));

        if (cfg == null)
        {
            return ToolResult.Failure($"KustoConfig '{p.ConfigName}' not found.", ctx);
        }

        var q = cfg.Queries.FirstOrDefault(x => x.Name.Equals(p.QueryName, StringComparison.OrdinalIgnoreCase));
        if (q == null)
        {
            return ToolResult.Failure($"Query '{p.QueryName}' not found under '{p.ConfigName}'.", ctx);
        }

        var kusto = Program.SubsystemManager.Get<KustoClient>();
        var (cols, rows) = await kusto.QueryAsync(cfg, q.Kql);

        // Render table for console/chat
        var table = KustoClient.ToTable(cols, rows);
        ctx.AddToolMessage(table);

        // Optional export
        if (!string.IsNullOrWhiteSpace(p.Export))
        {
            var ext = System.IO.Path.GetExtension(p.Export).ToLowerInvariant();
            var content = ext switch
            {
                ".csv"  => KustoClient.ToCsv(cols, rows),
                ".json" => KustoClient.ToJson(cols, rows),
                _       => table
            };
            System.IO.File.WriteAllText(p.Export!, content);
            ctx.AddToolMessage($"Saved: {p.Export}");
        }

        // Seed a snippet of the results into context for follow-on prompts (“summarize”, “triage”, etc.)
        await ContextManager.AddContent(table, $"kusto/{cfg.Name}/results/{q.Name}");

        return ToolResult.Success(table, ctx);
    }
}