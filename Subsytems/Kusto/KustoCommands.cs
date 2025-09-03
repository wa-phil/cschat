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
                        var (failures, added) = await kusto.RefreshConnectionsAsync();
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

                        // Delegate to the configured tool which performs the introspection and caching.
                        var input = new IntrospectKustoSchemaInput { ConfigName = cfg.Name };
                        var resp = await ToolRegistry.InvokeToolAsync("tool.kusto.introspect_schema", input);
                        Console.WriteLine(resp);
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
                        {
                            Console.WriteLine($"- {q.Name} — {q.Description}");
                        }
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

                        var (cols, rows) = await kusto.QueryAsync(cfg, q.Kql);
                        var table = Utilities.ToTable(cols, rows, Console.WindowWidth);
                        Console.WriteLine(table);

                        Console.Write("Export? (none/csv/json) ");
                        var how = (User.ReadLineWithHistory() ?? "").Trim().ToLowerInvariant();
                        if (how == "csv" || how == "json")
                        {
                            Console.Write("Path: ");
                            var path = User.ReadLineWithHistory();
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                var content = how == "csv" ? Utilities.ToCsv(cols, rows) : Utilities.ToJson(cols, rows);
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

                        var (cols, rows) = await kusto.QueryAsync(cfg, kql);
                        var table = Utilities.ToTable(cols, rows, Console.WindowWidth);
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
            if (configs.Count == 0) { Console.WriteLine("No KustoConfig found. Add one in Data \u2192 Kusto Config."); return null; }

            if (configs.Count == 1) return configs[0];

            var choices = configs.Select(c => $"{c.Name} @ {c.ClusterUri} | {c.Database}").ToList();
            var sel = User.RenderMenu("Select config:", choices);
            if (sel == null) return null;
            var idx = choices.IndexOf(sel);
            if (idx >= 0) return configs[idx];

            // Fallback: match by name before the '@'
            var name = sel.Split('@')[0].Trim();
            return configs.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        static KustoQuery? PickQuery(KustoConfig cfg)
        {
            if (cfg.Queries.Count == 0) { Console.WriteLine("(no saved queries)"); return null; }
            var list = cfg.Queries.OrderBy(q => q.Name).ToList();
            if (list.Count == 1) return list[0];

            var choices = list.Select(q => $"{q.Name} — {q.Description}").ToList();
            var sel = User.RenderMenu($"Select query for {cfg.Name}:", choices);
            if (sel == null) return null;
            var idx = choices.IndexOf(sel);
            if (idx >= 0) return list[idx];

            // Fallback: match by name before the '—'
            var name = sel.Split('—')[0].Trim();
            return list.FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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
