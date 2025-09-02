using System;
using Kusto.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using System.Collections.Generic;
using System.Collections.Concurrent;

#nullable enable

[IsConfigurable("Kusto")]
public class KustoClient : ISubsystem
{
    private bool _connected;
    private IDisposable? _umSubscription;

    // configName -> (query provider, request props, user-managed config)
    private readonly ConcurrentDictionary<string, (ICslQueryProvider Query, ClientRequestProperties Props, KustoConfig Cfg)> 
        _connections = new(StringComparer.OrdinalIgnoreCase);

    public Type ConfigType => typeof(KustoConfig);            // for your menus/metadata
    public bool IsAvailable => true;

    public bool IsEnabled
    {
        get => _connected;
        set
        {
            if (value && !_connected)
            {
                _connected = true;
                Connect();
                Register();
            }
            else if (!value && _connected)
            {
                Unregister();
                DisconnectAll();
                _connected = false;
            }
        }
    }

    // ===== Lifecycle =====

    private void Connect() => Log.Method(ctx =>
    {
        // Dispatch the async refresh and don't block the caller
        _ = RefreshConnectionsAsync();
    });

    private static KustoConnectionStringBuilder ApplyAuthMode(KustoConnectionStringBuilder kcsb, KustoConfig cfg)
    {
        // Use static extension methods available in the installed Kusto packages.
        // Prefer interactive/device-prompt for console flows except managed identity.
        switch (cfg.AuthMode)
        {
            case KustoAuthMode.managedIdentity:
                // Call overload accepting clientId; pass empty string for system-assigned
                try { return kcsb.WithAadSystemManagedIdentity(); } catch { return kcsb.WithAadUserPromptAuthentication(); }

            default:
                // devicecode, prompt, azcli, and any unknown modes -> interactive prompt
                try { return kcsb.WithAadUserPromptAuthentication(); } catch { return kcsb; }
        }
    }


    private void DisconnectAll()
    {
        foreach (var kv in _connections)
        {
            try { kv.Value.Query.Dispose(); } catch { /* ignore */ }
        }
        _connections.Clear();
    }

    public void Register()
    {
        Program.commandManager.SubCommands.Add(KustoCommands.Commands(this));
        // Subscribe to UserManagedData changes for KustoConfig
        try
        {
            // Log when handler invoked and dispatch to async worker
            _umSubscription = Program.userManagedData.Subscribe(typeof(KustoConfig), (t, change, item) =>
            {
                try
                {
                    Log.Method(ctx =>
                    {
                        ctx.Append(Log.Data.Name, (item as KustoConfig)?.Name ?? "<unknown>");
                        ctx.Append(Log.Data.Message, $"UMD change handler invoked for Kusto: {change}");
                        ctx.Succeeded();
                    });
                }
                catch { }
                _ = OnUserManagedChangeAsync(t, change, item);
            });
        }
        catch
        {
            // ignore subscription failures
        }
    }
    public void Unregister()
    {
        Program.commandManager.SubCommands.RemoveAll(c => c.Name.Equals("Kusto", StringComparison.OrdinalIgnoreCase));
        try { _umSubscription?.Dispose(); } catch { }
    }

    private async Task OnUserManagedChangeAsync(Type t, UserManagedData.ChangeType change, object? item)
    {
        if (t != typeof(KustoConfig)) return;
        try
        {
            Log.Method(ctx => { ctx.Append(Log.Data.Message, $"Entering Kusto UMD handler: {change}"); ctx.Succeeded(); });
            switch (change)
            {
                case UserManagedData.ChangeType.Added:
                    if (item is KustoConfig added && !string.IsNullOrWhiteSpace(added.Name))
                    {
                        var res = await TryConnectConfigAsync(added);
                        if (res.Query is not null && res.Props is not null && res.Cfg is not null)
                        {
                            _connections[added.Name] = (res.Query, res.Props, res.Cfg);
                            Log.Method(ctx => { ctx.Append(Log.Data.Name, added.Name); ctx.Append(Log.Data.Message, "Kusto: connected via UMD add"); ctx.Succeeded(); });
                        }
                    }
                    break;
                case UserManagedData.ChangeType.Updated:
                    if (item is KustoConfig updated && !string.IsNullOrWhiteSpace(updated.Name))
                    {
                        var res = await TryConnectConfigAsync(updated);
                        if (res.Query is not null && res.Props is not null && res.Cfg is not null)
                        {
                            if (_connections.TryGetValue(updated.Name, out var existing))
                            {
                                try { existing.Query.Dispose(); } catch { }
                            }
                            _connections[updated.Name] = (res.Query, res.Props, res.Cfg);
                            Log.Method(ctx => { ctx.Append(Log.Data.Name, updated.Name); ctx.Append(Log.Data.Message, "Kusto: reconnected via UMD update"); ctx.Succeeded(); });
                        }
                    }
                    break;
                case UserManagedData.ChangeType.Deleted:
                    if (item is KustoConfig deleted && !string.IsNullOrWhiteSpace(deleted.Name))
                    {
                        if (_connections.TryRemove(deleted.Name, out var old))
                        {
                            try { old.Query.Dispose(); } catch { }
                            Log.Method(ctx => { ctx.Append(Log.Data.Name, deleted.Name); ctx.Append(Log.Data.Message, "Kusto: disconnected via UMD delete"); ctx.Succeeded(); });
                        }
                    }
                    break;
            }
            Log.Method(ctx => { ctx.Append(Log.Data.Message, $"Exiting Kusto UMD handler: {change}"); ctx.Succeeded(); });
        }
        catch
        {
            // Swallow handler exceptions to avoid breaking UMD
        }
    }

    // ===== Connection Pooling over UserManaged KustoConfig =====

    public async Task<(List<string> Failures, int Added)> RefreshConnectionsAsync() => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var failures = new List<string>();
        int addOrUpdated = 0;

        // Collect valid configs
        var configs = Program.userManagedData.GetItems<KustoConfig>()
            .Where(cfg => !string.IsNullOrWhiteSpace(cfg.Name)
                       && !string.IsNullOrWhiteSpace(cfg.ClusterUri)
                       && !string.IsNullOrWhiteSpace(cfg.Database))
            .ToList();

        var currentNames = new HashSet<string>(configs.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        // 1) Drop stale connections that are no longer present in UMD
        var staleConnections = _connections.Select(kv => kv.Key).Where(name => !currentNames.Contains(name)).ToList();
        foreach (var name in staleConnections)
        {
            if (_connections.TryRemove(name, out var old))
            {
                try { old.Query.Dispose(); } catch { /* ignore */ }
                Log.Method(inner =>
                {
                    inner.Append(Log.Data.Name, name);
                    inner.Append(Log.Data.Message, "Removed connection for deleted config");
                    inner.Succeeded();
                });
            }
        }

        // Partition configs into those we already have and new ones
        var existingConfigs = configs.Where(cfg => _connections.ContainsKey(cfg.Name)).ToList();
        var newConfigs = configs.Where(cfg => !_connections.ContainsKey(cfg.Name)).ToList();

        // 2) Handle rebuilds for existing connections where endpoint or timeout changed
        foreach (var cfg in existingConfigs)
        {
            if (!_connections.TryGetValue(cfg.Name, out var existing))
            {
                continue; // concurrent removal
            }

            var existingTimeout = existing.Props?.Options?.FirstOrDefault(kv => string.Equals(kv.Key, "servertimeout", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
            var desiredTimeout = $"{Math.Max(5, cfg.DefaultTimeoutSeconds)}s";
            var endpointChanged = !UriEquals(existing.Cfg.ClusterUri, cfg.ClusterUri) || !existing.Cfg.Database.Equals(cfg.Database, StringComparison.OrdinalIgnoreCase);

            if (!endpointChanged && string.Equals(existingTimeout, desiredTimeout, StringComparison.Ordinal))
            {
                continue; // nothing changed
            }

            var connResult = await TryConnectConfigAsync(cfg);
            if (connResult.Query is not null && connResult.Props is not null && connResult.Cfg is not null)
            {
                try { existing.Query.Dispose(); } catch { /* ignore */ }
                _connections[cfg.Name] = (connResult.Query, connResult.Props, connResult.Cfg);
                addOrUpdated++;

                Log.Method(inner =>
                {
                    inner.Append(Log.Data.Name, cfg.Name);
                    inner.Append(Log.Data.Provider, $"{cfg.ClusterUri}/{cfg.Database}");
                    inner.Append(Log.Data.Message, "Reconnected");
                    inner.Succeeded();
                });
            }
            else
            {
                failures.Add(cfg.Name);
                Log.Method(inner =>
                {
                    inner.Warn($"Reconnect failed for '{cfg.Name}': {connResult.Error}");
                    inner.Append(Log.Data.Name, cfg.Name);
                    inner.Append(Log.Data.Provider, $"{cfg.ClusterUri}/{cfg.Database}");
                });
            }
        }

        // 3) Handle new connections
        foreach (var cfg in newConfigs)
        {
            var connResult = await TryConnectConfigAsync(cfg);
            if (connResult.Query is not null && connResult.Props is not null && connResult.Cfg is not null)
            {
                _connections[cfg.Name] = (connResult.Query, connResult.Props, connResult.Cfg);
                addOrUpdated++;

                Log.Method(inner =>
                {
                    inner.Append(Log.Data.Name, cfg.Name);
                    inner.Append(Log.Data.Provider, $"{cfg.ClusterUri}/{cfg.Database}");
                    inner.Append(Log.Data.Message, "Connected");
                    inner.Succeeded();
                });
            }
            else
            {
                failures.Add(cfg.Name);
                Log.Method(inner =>
                {
                    inner.Warn($"Connect failed for '{cfg.Name}': {connResult.Error}");
                    inner.Append(Log.Data.Name, cfg.Name);
                    inner.Append(Log.Data.Provider, $"{cfg.ClusterUri}/{cfg.Database}");
                });
            }
        }

        ctx.Append(Log.Data.Count, addOrUpdated);
        return (failures, addOrUpdated);

        static bool UriEquals(string a, string b)
        {
            if (Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub))
                return Uri.Compare(ua, ub, UriComponents.AbsoluteUri, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    });

    public record ConnectionResult(ICslQueryProvider? Query, ClientRequestProperties? Props, KustoConfig? Cfg, string? Error)
    {
        public static ConnectionResult Success(ICslQueryProvider query, ClientRequestProperties props, KustoConfig cfg) => new ConnectionResult(query, props, cfg, null);
        public static ConnectionResult Failure(string error) => new ConnectionResult(null, null, null, error);
    }

    private static async Task<ConnectionResult> TryConnectConfigAsync(KustoConfig cfg) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        try
        {
            // Create query provider using synchronous factory on background thread (SDK is sync)
            var query = await Task.Run(() => KustoClientFactory.CreateCslQueryProvider(ApplyAuthMode(new KustoConnectionStringBuilder(cfg.ClusterUri, cfg.Database), cfg)));
            var props = new ClientRequestProperties
            {
                ClientRequestId = $"cschat;{Guid.NewGuid()}",
                Application = "cschat"
            };
            props.SetOption("servertimeout", $"{Math.Max(5, cfg.DefaultTimeoutSeconds)}s");
            ctx.Succeeded();
            return ConnectionResult.Success(query, props, cfg);
        }
        catch (Exception ex)
        {
            ctx.Failed($"Failed to connect to {cfg.Name}.", ex);
            return ConnectionResult.Failure(ex.Message);
        }
    });


    // ===== Public API (no active config) =====

    public async Task<bool> EnsureConnectedAsync(KustoConfig cfg) => await Log.MethodAsync(async ctx =>
    {
        if (_connections.ContainsKey(cfg.Name))
        {
            ctx.OnlyEmitOnFailure();
            ctx.Succeeded();
            return true;
        }

        var res = await TryConnectConfigAsync(cfg);
        if (res.Query is not null && res.Props is not null && res.Cfg is not null)
        {
            _connections[cfg.Name] = (res.Query, res.Props, res.Cfg);
            ctx.Append(Log.Data.Name, cfg.Name);
            ctx.Append(Log.Data.Provider, $"{cfg.ClusterUri}/{cfg.Database}");
            ctx.Append(Log.Data.Message, "Connected (ephemeral)");
            ctx.Succeeded();
            return true;
        }

        ctx.Warn($"Ephemeral connect failed for '{cfg.Name}': {res.Error}");
        ctx.Append(Log.Data.Name, cfg.Name);
        ctx.Append(Log.Data.Provider, $"{cfg.ClusterUri}/{cfg.Database}");
        return false;
    });


    public IReadOnlyCollection<string> GetConnectedConfigNames() => _connections.Keys.ToList();

    public bool IsConnected(string configName) => _connections.ContainsKey(configName);

    public async Task<(IReadOnlyList<string> Columns, List<string[]> Rows)> QueryAsync(KustoConfig cfg, string kql)
    {
        if (!_connections.TryGetValue(cfg.Name, out var conn))
        {
            throw new InvalidOperationException($"Kusto config '{cfg.Name}' is not connected. Run Kusto.RefreshConnections and check logs.");
        }

        using var reader = await conn.Query.ExecuteQueryAsync(conn.Cfg.Database, kql, conn.Props);

        var columns = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        var rows = new List<string[]>();
        while (reader.Read())
        {
            var vals = new string[reader.FieldCount];
            for (int i = 0; i < vals.Length; i++)
                vals[i] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i)) ?? "";
            rows.Add(vals);
        }
        return (columns, rows);
    }

    // Convenience helpers (renderers kept static as before)
    public static string ToTable(IEnumerable<string> headers, IEnumerable<string[]> rows, int maxWidth = 140)
    {
        var hs = headers.ToList();
        var rowList = rows.ToList();

        int origCount = hs.Count;
        var indices = Enumerable.Range(0, origCount).ToList();

        // If we have rows, drop columns where every row value is null/empty (noise).
        if (rowList.Count > 0)
        {
            indices = indices.Where(i => rowList.Any(r => i < r.Length && !string.IsNullOrEmpty(r[i]))).ToList();
            // If that removed everything, fall back to keeping all columns so we still show headers
            if (indices.Count == 0) indices = Enumerable.Range(0, origCount).ToList();
        }

        // Compute max content length per column (header vs cell content)
        var maxLens = new List<int>();
        foreach (var i in indices)
        {
            int w = hs[i]?.Length ?? 0;
            foreach (var r in rowList)
            {
                if (i < r.Length && r[i] != null)
                    w = Math.Max(w, r[i].Length);
            }
            maxLens.Add(w);
        }

        int colCount = indices.Count;
        if (colCount == 0) return string.Empty;

        // Compute available content width (excluding separators " │ " between columns)
        int sepWidth = 3 * Math.Max(0, colCount - 1);
        int contentMax = Math.Max(1, maxWidth - sepWidth);

        int minCol = 6;
        if (contentMax < colCount * minCol)
        {
            // Shrink minCol if overall space is limited
            minCol = Math.Max(1, contentMax / colCount);
        }

        // Greedy left-to-right allocation: try to give each column its needed width
        var widths = new int[colCount];
        int allocated = 0;
        for (int idx = 0; idx < colCount; idx++)
        {
            int remaining = colCount - idx - 1;
            int minForRemaining = remaining * minCol;
            int availableForThis = contentMax - allocated - minForRemaining;
            int desired = Math.Min(maxLens[idx], contentMax);
            int w = Math.Clamp(desired, minCol, Math.Max(minCol, availableForThis));
            widths[idx] = w;
            allocated += w;
        }

        // If we under-allocated due to clamping, distribute leftover space left-to-right
        int leftover = contentMax - allocated;
        for (int i = 0; leftover > 0 && i < colCount; i++)
        {
            int add = Math.Min(leftover, Math.Max(0, maxLens[i] - widths[i]));
            widths[i] += add;
            leftover -= add;
        }
        // If still leftover, give one char per column left-to-right
        for (int i = 0; leftover > 0 && i < colCount; i++)
        {
            widths[i] += 1;
            leftover -= 1;
        }

        string Fit(string s, int w) => (s.Length <= w) ? s.PadRight(w) : s.Substring(0, Math.Max(0, w - 1)) + "…";

        var lines = new List<string>();

        // Header
        lines.Add(string.Join(" │ ", indices.Select((origIdx, j) => Fit(hs[origIdx] ?? "", widths[j]))));
        // Separator
        lines.Add(string.Join("─┼─", widths.Select(c => new string('─', Math.Max(1, c)))));

        // Rows
        foreach (var row in rowList)
        {
            var parts = new List<string>();
            for (int j = 0; j < colCount; j++)
            {
                var origIdx = indices[j];
                var s = (origIdx < row.Length) ? (row[origIdx] ?? "") : "";
                parts.Add(Fit(s, widths[j]));
            }
            lines.Add(string.Join(" │ ", parts));
        }

        return string.Join("\n", lines);
    }

    public static string ToCsv(IReadOnlyList<string> headers, List<string[]> rows)
    {
        static string E(string s) => s.Contains('"') || s.Contains(',') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var lines = new List<string> { string.Join(",", headers) };
        lines.AddRange(rows.Select(r => string.Join(",", r.Select(E))));
        return string.Join("\n", lines);
    }

    public static string ToJson(IReadOnlyList<string> headers, List<string[]> rows)
    {
        var list = rows.Select(r =>
        {
            var o = new Dictionary<string, object?>();
            for (int i = 0; i < headers.Count; i++)
                o[headers[i]] = i < r.Length ? r[i] : null;
            return o;
        }).ToList();
        return list.ToJson();
    }
}