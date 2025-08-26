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
        ctx.OnlyEmitOnFailure();
        var added = RefreshConnections(out var failures);
        ctx.Append(Log.Data.Count, added);
        if (failures.Count > 0)
            ctx.Append(Log.Data.Message, $"Kusto: {failures.Count} config(s) failed to connect; see logs.");
        ctx.Succeeded();
    });

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
    }
    public void Unregister()
    {
        Program.commandManager.SubCommands.RemoveAll(c => c.Name.Equals("Kusto", StringComparison.OrdinalIgnoreCase));
    }

    // ===== Connection Pooling over UserManaged KustoConfig =====

    /// <summary>
    /// Scan UserManagedData for KustoConfig entries and ensure each is connected.
    /// Returns the number of new/updated connections and outputs a list of failed config names.
    /// </summary>
    public int RefreshConnections(out List<string> failures)
    {
        var localFailures = new List<string>();

        var addedOrUpdated = Log.Method(ctx =>
        {
            ctx.OnlyEmitOnFailure();
            var configs = Program.userManagedData.GetItems<KustoConfig>(); // swap to IUserManagedStore if you refactored
            int addedOrUpdatedInner = 0;

        foreach (var cfg in configs)
        {
            if (string.IsNullOrWhiteSpace(cfg.Name) ||
                string.IsNullOrWhiteSpace(cfg.ClusterUri) ||
                string.IsNullOrWhiteSpace(cfg.Database))
            {
                ctx.Append(Log.Data.Message, $"Kusto: skipping config with missing fields (Name/ClusterUri/Database). Name='{cfg?.Name ?? "(null)"}'");
                continue;
            }

            // If we already have a connection but timeout/auth changed, rebuild it.
            if (_connections.TryGetValue(cfg.Name, out var existing))
            {
                var existingTimeout = existing.Props?.Options?.FirstOrDefault(kv => string.Equals(kv.Key, "servertimeout", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
                var desiredTimeout = $"{Math.Max(5, cfg.DefaultTimeoutSeconds)}s";
                var endpointChanged = !UriEquals(existing.Cfg.ClusterUri, cfg.ClusterUri) || !existing.Cfg.Database.Equals(cfg.Database, StringComparison.OrdinalIgnoreCase);

                if (!endpointChanged && string.Equals(existingTimeout, desiredTimeout, StringComparison.Ordinal))
                    continue; // nothing to do, keep current connection

                // Rebuild this connection
                TryConnectConfig(cfg, out var tuple, out var error);
                if (tuple is not null)
                {
                    // Dispose old and swap
                    try { existing.Query.Dispose(); } catch { /* ignore */ }
                    _connections[cfg.Name] = tuple.Value;
                    addedOrUpdatedInner++;

                    // Structured log for reconnection
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
                    localFailures.Add(cfg.Name);
                    // Structured warning for reconnect failure
                    Log.Method(inner =>
                    {
                        inner.Warn($"Reconnect failed for '{cfg.Name}': {error}");
                        inner.Append(Log.Data.Name, cfg.Name);
                        inner.Append(Log.Data.Provider, $"{cfg.ClusterUri}/{cfg.Database}");
                    });
                }
            }
            else
            {
                // New connection
                TryConnectConfig(cfg, out var tuple, out var error);
                if (tuple is not null)
                {
                    _connections[cfg.Name] = tuple.Value;
                    addedOrUpdatedInner++;

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
                    localFailures.Add(cfg.Name);
                    Log.Method(inner =>
                    {
                        inner.Warn($"Connect failed for '{cfg.Name}': {error}");
                        inner.Append(Log.Data.Name, cfg.Name);
                        inner.Append(Log.Data.Provider, $"{cfg.ClusterUri}/{cfg.Database}");
                    });
                }
            }
        }

        // Optional: drop stale connections no longer present in UMD
            var currentNames = new HashSet<string>(configs.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var name in _connections.Keys.ToList())
        {
            if (!currentNames.Contains(name))
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
        }

            ctx.Append(Log.Data.Count, addedOrUpdatedInner);
            return addedOrUpdatedInner;
        });

        failures = localFailures;
        return addedOrUpdated;

        static bool UriEquals(string a, string b)
        {
            if (Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub))
                return Uri.Compare(ua, ub, UriComponents.AbsoluteUri, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void TryConnectConfig(KustoConfig cfg,
        out (ICslQueryProvider Query, ClientRequestProperties Props, KustoConfig Cfg)? tuple,
        out string? error)
    {
        tuple = null;
        error = null;
        try
        {
            var kcsb = new KustoConnectionStringBuilder(cfg.ClusterUri, cfg.Database).WithAadUserPromptAuthentication(); // device-code style
            var query = KustoClientFactory.CreateCslQueryProvider(kcsb);
            var props = new ClientRequestProperties
            {
                ClientRequestId = $"cschat;{Guid.NewGuid()}",
                Application = "cschat"
            };
            props.SetOption("servertimeout", $"{Math.Max(5, cfg.DefaultTimeoutSeconds)}s");

            tuple = (query, props, cfg);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
    }

    // ===== Public API (no active config) =====

    public IReadOnlyCollection<string> GetConnectedConfigNames() => _connections.Keys.ToList();

    public bool IsConnected(string configName) => _connections.ContainsKey(configName);

    public async Task<(IReadOnlyList<string> Columns, List<string[]> Rows)> QueryAsync(
        string configName,
        string kql,
        CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(configName, out var conn))
        {
            throw new InvalidOperationException($"Kusto config '{configName}' is not connected. Run Kusto.RefreshConnections and check logs.");
        }

        using var reader = await conn.Query.ExecuteQueryAsync(conn.Cfg.Database, kql, conn.Props, ct);

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
        var cols = new List<int>();
        var hs = headers.ToList();
        for (int i = 0; i < hs.Count; i++)
        {
            var w = hs[i].Length;
            foreach (var r in rows) w = Math.Max(w, i < r.Length ? r[i].Length : 0);
            cols.Add(Math.Min(w, Math.Max(6, maxWidth / Math.Max(1, hs.Count))));
        }

        string Fit(string s, int w) => (s.Length <= w) ? s.PadRight(w) : s.Substring(0, Math.Max(0, w - 1)) + "…";
        var lines = new List<string>();
        lines.Add(string.Join(" │ ", hs.Select((h, i) => Fit(h, cols[i]))));
        lines.Add(string.Join("─┼─", cols.Select(c => new string('─', c))));
        foreach (var row in rows)
            lines.Add(string.Join(" │ ", hs.Select((_, i) => Fit(i < row.Length ? row[i] : "", cols[i]))));
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