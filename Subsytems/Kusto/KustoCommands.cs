using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public static class KustoCommands
{
    public static Command Commands(KustoClient kusto)
    {
        return new Command
        {
            Name = "Kusto",
            Description = () => "Kusto (Azure Data Explorer)",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "refresh",
                    Description = () => "Connect/reconnect to all User-Managed Kusto configs",
                    Action = async () =>
                    {
                        await Task.Yield();
                        var added = kusto.RefreshConnections(out var failures);
                        var connected = string.Join(", ", kusto.GetConnectedConfigNames().OrderBy(s => s));
                        Console.WriteLine($"Updated connections: {added}");
                        Console.WriteLine($"Connected: {(string.IsNullOrWhiteSpace(connected) ? "(none)" : connected)}");
                        if (failures.Count > 0) Console.WriteLine($"Failed: {string.Join(", ", failures)} (see logs)");
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "status",
                    Description = () => "Show connected configs",
                    Action = async () =>
                    {
                        await Task.Yield();
                        var names = kusto.GetConnectedConfigNames().OrderBy(s => s).ToList();
                        if (names.Count == 0) { Console.WriteLine("(no active connections)"); return Command.Result.Success; }
                        foreach (var n in names) Console.WriteLine($"- {n}");
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "schema",
                    Description = () => "Fetch & cache schema for a config; seed it into chat context",
                    Action = async () =>
                    {
                        var cfg = PickConfig();
                        if (cfg is null) return Command.Result.Failed;

                        // Introspect schema
                        var text = cfg.Database.Replace("'", "''");
                        var kql = $".show database ['{text}'] schema";
                        var (cols, rows) = await kusto.QueryAsync(cfg.Name, kql);

                        // Normalize and cache into UMD
                        int iT = Array.FindIndex(cols.ToArray(), c => c.Equals("TableName", StringComparison.OrdinalIgnoreCase));
                        int iC = Array.FindIndex(cols.ToArray(), c => c.Equals("ColumnName", StringComparison.OrdinalIgnoreCase));
                        int iY = Array.FindIndex(cols.ToArray(), c => c.Equals("ColumnType", StringComparison.OrdinalIgnoreCase));
                        var map = new Dictionary<string, List<(string col,string type)>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in rows)
                        {
                            if (iT<0 || iC<0) continue;
                            var t = iT<r.Length ? r[iT] : null;
                            var c = iC<r.Length ? r[iC] : null;
                            var y = iY>=0 && iY<r.Length ? r[iY] : "";
                            if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(c)) continue;
                            if (!map.TryGetValue(t!, out var list)) map[t!] = list = new();
                            list.Add((c!, y ?? ""));
                        }

                        var schemaDto = new
                        {
                            database = cfg.Database,
                            tables = map.Select(kv => new {
                                name = kv.Key,
                                columns = kv.Value.Select(v => new { name = v.col, type = v.type }).ToList()
                            }).OrderBy(t => t.name).ToList()
                        };
                        cfg.SchemaJson = schemaDto.ToJson();
                        Program.userManagedData.UpdateItem(cfg, x => x.Name.Equals(cfg.Name, StringComparison.OrdinalIgnoreCase));

                        // Seed a readable sketch into chat context
                        var lines = new List<string> { $"Kusto schema for {cfg.Name} ({cfg.Database}):" };
                        foreach (var t in map.OrderBy(k => k.Key))
                        {
                            var colsSketch = string.Join(", ", t.Value.OrderBy(v => v.col).Select(v => $"{v.col}:{v.type}"));
                            lines.Add($"- {t.Key}: {colsSketch}");
                        }
                        await ContextManager.AddContent(string.Join("\n", lines), $"kusto/{cfg.Name}/schema");
                        Console.WriteLine($"Cached schema and added RAG context: kusto/{cfg.Name}/schema");
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "list queries",
                    Description = () => "List saved queries for a config",
                    Action = async () =>
                    {
                        await Task.Yield();
                        var cfg = PickConfig();
                        if (cfg is null) return Command.Result.Failed;
                        if (cfg.Queries.Count == 0) { Console.WriteLine("(no saved queries)"); return Command.Result.Success; }
                        foreach (var q in cfg.Queries.OrderBy(q => q.Name))
                            Console.WriteLine($"- {q.Name} — {q.Description}");
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "run saved",
                    Description = () => "Run a saved query under a config (optional export)",
                    Action = async () =>
                    {
                        var cfg = PickConfig();
                        if (cfg is null) return Command.Result.Failed;
                        var q = PickQuery(cfg);
                        if (q is null) return Command.Result.Failed;

                        var (cols, rows) = await kusto.QueryAsync(cfg.Name, q.Kql);
                        var table = KustoClient.ToTable(cols, rows);
                        Console.WriteLine(table);

                        Console.Write("Export? (none/csv/json) ");
                        var how = (User.ReadLineWithHistory() ?? "").Trim().ToLowerInvariant();
                        if (how == "csv" || how == "json")
                        {
                            Console.Write("Path: ");
                            var path = User.ReadLineWithHistory();
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                var content = how == "csv" ? KustoClient.ToCsv(cols, rows) : KustoClient.ToJson(cols, rows);
                                File.WriteAllText(path!, content);
                                Console.WriteLine($"Saved: {path}");
                            }
                        }

                        Program.userManagedData.UpdateItem(cfg, x => x.Name.Equals(cfg.Name, StringComparison.OrdinalIgnoreCase));
                        await ContextManager.AddContent(table, $"kusto/{cfg.Name}/results/{q.Name}");
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "save query",
                    Description = () => "Create or update a saved query under a config",
                    Action = async () =>
                    {
                        await Task.Yield();
                        var cfg = PickConfig();
                        if (cfg is null) return Command.Result.Failed;

                        Console.Write("Query name: ");
                        var name = User.ReadLineWithHistory() ?? "";
                        if (string.IsNullOrWhiteSpace(name)) return Command.Result.Failed;

                        Console.Write("Description: ");
                        var desc = User.ReadLineWithHistory() ?? "";

                        Console.WriteLine("Paste KQL (blank line to finish):");
                        var kql = ReadMultiline();

                        var existing = cfg.Queries.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (existing is null)
                        {
                            cfg.Queries.Add(new KustoQuery { Name = name, Description = desc, Kql = kql });
                        }
                        else
                        {
                            existing.Description = desc;
                            existing.Kql = kql;
                        }
                        Program.userManagedData.UpdateItem(cfg, x => x.Name.Equals(cfg.Name, StringComparison.OrdinalIgnoreCase));
                        Console.WriteLine("Saved.");
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "run adhoc",
                    Description = () => "Run an ad-hoc KQL against a config (optionally save)",
                    Action = async () =>
                    {
                        var cfg = PickConfig();
                        if (cfg is null) return Command.Result.Failed;

                        Console.WriteLine("Enter KQL (blank line to run):");
                        var kql = ReadMultiline();

                        var (cols, rows) = await kusto.QueryAsync(cfg.Name, kql);
                        var table = KustoClient.ToTable(cols, rows);
                        Console.WriteLine(table);
                        await ContextManager.AddContent(table, $"kusto/{cfg.Name}/results/__adhoc__");

                        Console.Write("Save this as a named query? (y/N) ");
                        var save = (User.ReadLineWithHistory() ?? "").Trim().ToLowerInvariant();
                        if (save == "y" || save == "yes")
                        {
                            Console.Write("Query name: "); var name = User.ReadLineWithHistory() ?? "";
                            Console.Write("Description: "); var desc = User.ReadLineWithHistory() ?? "";
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                var existing = cfg.Queries.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                                if (existing is null) cfg.Queries.Add(new KustoQuery { Name = name, Description = desc, Kql = kql });
                                else { existing.Description = desc; existing.Kql = kql; }
                                Program.userManagedData.UpdateItem(cfg, x => x.Name.Equals(cfg.Name, StringComparison.OrdinalIgnoreCase));
                                Console.WriteLine("Saved.");
                            }
                        }

                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "delete query",
                    Description = () => "Delete a saved query under a config",
                    Action = async () =>
                    {
                        await Task.Yield();
                        var cfg = PickConfig();
                        if (cfg is null) return Command.Result.Failed;
                        var q = PickQuery(cfg);
                        if (q is null) return Command.Result.Failed;

                        cfg.Queries.RemoveAll(x => x.Name.Equals(q.Name, StringComparison.OrdinalIgnoreCase));
                        Program.userManagedData.UpdateItem(cfg, x => x.Name.Equals(cfg.Name, StringComparison.OrdinalIgnoreCase));
                        Console.WriteLine("Deleted.");
                        return Command.Result.Success;
                    }
                }
            }
        };

        // ----- local helpers -----
        static KustoConfig? PickConfig()
        {
            var configs = Program.userManagedData.GetItems<KustoConfig>().OrderBy(c => c.Name).ToList();
            if (configs.Count == 0) { Console.WriteLine("No KustoConfig found. Add one in Data → Kusto Config."); return null; }

            if (configs.Count == 1) return configs[0];

            Console.WriteLine("Select config:");
            for (int i = 0; i < configs.Count; i++) Console.WriteLine($"  [{i+1}] {configs[i].Name} @ {configs[i].ClusterUri} | {configs[i].Database}");
            Console.Write("> ");
            if (int.TryParse(User.ReadLineWithHistory(), out var idx) && idx >= 1 && idx <= configs.Count) return configs[idx-1];

            Console.WriteLine("Invalid selection.");
            return null;
        }

        static KustoQuery? PickQuery(KustoConfig cfg)
        {
            if (cfg.Queries.Count == 0) { Console.WriteLine("(no saved queries)"); return null; }
            var list = cfg.Queries.OrderBy(q => q.Name).ToList();
            if (list.Count == 1) return list[0];

            Console.WriteLine("Select query:");
            for (int i = 0; i < list.Count; i++) Console.WriteLine($"  [{i+1}] {list[i].Name} — {list[i].Description}");
            Console.Write("> ");
            if (int.TryParse(User.ReadLineWithHistory(), out var idx) && idx >= 1 && idx <= list.Count) return list[idx-1];

            Console.WriteLine("Invalid selection.");
            return null;
        }

        static string ReadMultiline()
        {
            var lines = new List<string>();
            while (true)
            {
                var ln = Console.ReadLine();
                if (string.IsNullOrEmpty(ln)) break;
                lines.Add(ln);
            }
            return string.Join("\n", lines);
        }
    }
}
