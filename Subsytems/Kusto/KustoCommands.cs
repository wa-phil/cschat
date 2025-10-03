using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public static class KustoCommands
{
    private class ExportModel { public string Format { get; set; } = "none"; public string Path { get; set; } = "results.txt"; }
    private class SaveQueryModel { public string Name { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; }
    private class KqlInputModel { public string Kql { get; set; } = string.Empty; }

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
                        Program.ui.WriteLine($"Updated connections: {added}");
                        Program.ui.WriteLine($"Connected: {(string.IsNullOrWhiteSpace(connected) ? "(none)" : connected)}");
                        if (failures.Count > 0) Program.ui.WriteLine($"Failed: {string.Join(", ", failures)} (see logs)");
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
                        if (names.Count == 0) { Program.ui.WriteLine("(no active connections)"); return Command.Result.Success; }
                        foreach (var n in names) Program.ui.WriteLine($"- {n}");
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
                        Program.ui.WriteLine(resp);
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
                        if (cfg.Queries.Count == 0) { Program.ui.WriteLine("(no saved queries)"); return Command.Result.Success; }
                        foreach (var q in cfg.Queries.OrderBy(q => q.Name))
                        {
                            Program.ui.WriteLine($"- {q.Name} — {q.Description}");
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "run saved",
                    Description = () => "Run a saved query under a config (optional export)",
                    Action = async () => await Log.MethodAsync(async ctx=>
                    {
                        ctx.OnlyEmitOnFailure();
                        var cfg = PickConfig();
                        if (cfg is null) return Command.Result.Failed;
                        var q = PickQuery(cfg);
                        if (q is null) return Command.Result.Failed;

                        var table = await kusto.QueryAsync(cfg, q.Kql);
                        Program.ui.RenderTable(table, $"Results: {cfg.Name} / {q.Name}");

                        var exportForm = UiForm.Create("Export results", new ExportModel { Format = "none", Path = "results.txt" });
                        exportForm.AddChoice<ExportModel>("Format", new[]{"none","csv","json"}, m => m.Format, (m,v)=> m.Format = v);
                        exportForm.AddPath<ExportModel>("Path", m => m.Path, (m,v)=> m.Path = v)
                                  .WithPathMode(PathPickerMode.SaveFile)
                                  .MakeOptionalIf(true)
                                  .WithHelp("File path if exporting.");
                        bool exportSucceed = true;
                        if (await Program.ui.ShowFormAsync(exportForm))
                        {
                            var em = (ExportModel)exportForm.Model!;
                            if (em.Format == "csv" || em.Format == "json")
                            {
                                try
                                {
                                    var content = em.Format == "csv" ? table.ToCsv() : table.ToJson();
                                    File.WriteAllText(em.Path, content);
                                }
                                catch (Exception ex)
                                {
                                    exportSucceed = false;
                                    ctx.Append(Log.Data.Message, $"Export failed: {ex.Message}");
                                }
                            }
                        }

                        Program.userManagedData.UpdateItem(cfg, x => x.Name.Equals(cfg.Name, StringComparison.OrdinalIgnoreCase));
                        Program.ui.RenderTable(table, $"Results: {cfg.Name} / {q.Name}");
                        await ContextManager.AddContent(table.ToCsv(), $"kusto/{cfg.Name}/results/{q.Name}");
                        ctx.Succeeded(exportSucceed);
                        return Command.Result.Success;
                    })
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

                        var saveForm = UiForm.Create("Save query", new SaveQueryModel { Name = "", Description = "" });
                        saveForm.AddString<SaveQueryModel>("Name", m => m.Name, (m,v)=> m.Name = v);
                        saveForm.AddText<SaveQueryModel>("Description", m => m.Description, (m,v)=> m.Description = v).MakeOptional();
                        if (!await Program.ui.ShowFormAsync(saveForm)) { return Command.Result.Cancelled; }
                        var name = ((SaveQueryModel)saveForm.Model!).Name;
                        var desc = ((SaveQueryModel)saveForm.Model!).Description;
                        if (string.IsNullOrWhiteSpace(name)) { return Command.Result.Failed; }

                        // Collect KQL using a form (single text field) instead of manual multiline capture
                        var kqlForm = UiForm.Create("Enter KQL", new KqlInputModel { Kql = string.Empty });
                        kqlForm.AddText<KqlInputModel>("KQL", m => m.Kql, (m,v)=> m.Kql = v).WithHelp("Enter the full KQL query.");
                        if (!await Program.ui.ShowFormAsync(kqlForm)) { return Command.Result.Cancelled; }
                        var kql = ((KqlInputModel)kqlForm.Model!).Kql;

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
                        Program.ui.WriteLine("Saved.");
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

                        // Collect adhoc KQL via a form
                        var adhocForm = UiForm.Create("Adhoc KQL", new KqlInputModel { Kql = string.Empty });
                        adhocForm.AddText<KqlInputModel>("KQL", m => m.Kql, (m,v)=> m.Kql = v).WithHelp("Enter the KQL to run.");
                        if (!await Program.ui.ShowFormAsync(adhocForm)) { return Command.Result.Cancelled; }
                        var kql = ((KqlInputModel)adhocForm.Model!).Kql;

                        var table = await kusto.QueryAsync(cfg, kql);
                        Program.ui.RenderTable(table, $"Results: {cfg.Name} / __adhoc__");
                        await ContextManager.AddContent(table.ToCsv(), $"kusto/{cfg.Name}/results/__adhoc__");

                        if (await Program.ui.ConfirmAsync("Save this as a named query?", false))
                        {
                            var adhocSaveForm = UiForm.Create("Save adhoc query", new SaveQueryModel { Name = "", Description = "" });
                            adhocSaveForm.AddString<SaveQueryModel>("Name", m => m.Name, (m,v)=> m.Name = v);
                            adhocSaveForm.AddText<SaveQueryModel>("Description", m => m.Description, (m,v)=> m.Description = v).MakeOptional();
                            if (await Program.ui.ShowFormAsync(adhocSaveForm))
                            {
                                var nm = ((SaveQueryModel)adhocSaveForm.Model!).Name;
                                var ds = ((SaveQueryModel)adhocSaveForm.Model!).Description;
                                if (!string.IsNullOrWhiteSpace(nm))
                                {
                                    var existing = cfg.Queries.FirstOrDefault(x => x.Name.Equals(nm, StringComparison.OrdinalIgnoreCase));
                                    if (existing is null) cfg.Queries.Add(new KustoQuery { Name = nm, Description = ds, Kql = kql });
                                    else { existing.Description = ds; existing.Kql = kql; }
                                    Program.userManagedData.UpdateItem(cfg, x => x.Name.Equals(cfg.Name, StringComparison.OrdinalIgnoreCase));
                                }
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
                        return Command.Result.Success;
                    }
                }
            }
        };

        // ----- local helpers -----
        static KustoConfig? PickConfig()
        {
            var configs = Program.userManagedData.GetItems<KustoConfig>().OrderBy(c => c.Name).ToList();
            if (configs.Count == 0) { Program.ui.WriteLine("No KustoConfig found. Add one in Data \u2192 Kusto Config."); return null; }

            if (configs.Count == 1) return configs[0];

            var choices = configs.Select(c => $"{c.Name} @ {c.ClusterUri} | {c.Database}").ToList();
            var sel = Program.ui.RenderMenu("Select config:", choices);
            if (sel == null) return null;
            var idx = choices.IndexOf(sel);
            if (idx >= 0) return configs[idx];

            // Fallback: match by name before the '@'
            var name = sel.Split('@')[0].Trim();
            return configs.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        static KustoQuery? PickQuery(KustoConfig cfg)
        {
            if (cfg.Queries.Count == 0) { Program.ui.WriteLine("(no saved queries)"); return null; }
            var list = cfg.Queries.OrderBy(q => q.Name).ToList();
            if (list.Count == 1) return list[0];

            var choices = list.Select(q => $"{q.Name} — {q.Description}").ToList();
            var sel = Program.ui.RenderMenu($"Select query for {cfg.Name}:", choices);
            if (sel == null) return null;
            var idx = choices.IndexOf(sel);
            if (idx >= 0) return list[idx];

            // Fallback: match by name before the '—'
            var name = sel.Split('—')[0].Trim();
            return list.FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

    }
}
