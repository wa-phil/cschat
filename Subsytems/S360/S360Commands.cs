using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


public static class S360Commands
{
    private class S360LimitModel { public int Limit { get; set; } = 25; }
    public static Command Commands(S360Client s360)
    {
        return new Command
        {
            Name = "S360",
            Description = () => "S360 triage & summaries",
            SubCommands = new List<Command>
            {
                new Command {
                    Name = "fetch", Description = () => "Fetch action items for a profile",
                    Action = async () => {
                        var prof = await PickProfile(); if (prof is null) return Command.Result.Failed;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.s360.fetch",
                            new FetchS360Input { ProfileName = prof.Name });
                        using var output = Program.ui.BeginRealtime("Fetching action items...");
                        output.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "triage", Description = () => "Score & summarize (briefing + action plan)",
                    Action = async () => {
                        var prof = await PickProfile(); if (prof is null) return Command.Result.Failed;

                        var form = UiForm.Create("Triage options", 15);
                        form.AddInt("Top N")
                            .IntBounds(min: 1, max: 100)
                            .WithHelp("Number of top action items to include in the summary, range is 1 to 100.");

                        if (!await Program.ui.ShowFormAsync(form)){
                            return Command.Result.Cancelled;
                        }
                        var n = (int)form.Model!;

                        // Perform the fetch/score and build a Report (LLM-backed action plan when available).
                        var s360 = Program.SubsystemManager.Get<S360Client>();
                        using var realtime = Program.ui.BeginRealtime("Generating triage...");
                        realtime.WriteLine($"Fetching S360 items for profile '{prof.Name}'...");
                        var table = await s360.FetchAsync(prof);
                        realtime.WriteLine($"Scoring {table.Rows.Count} items...");
                        var scored = s360.Score(table, prof);

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

                        var bulletLines = grouped.Select(x => $"{x.Service}: {x.Total} items, {x.AtRisk} at-risk, {x.MissingEta} missing ETA");

                        // Action plan → grouped by service, items ordered by DUE (soonest first), with per-item LLM summaries/next-steps
                        var top = scored.Take(Math.Max(1, n)).ToList();

                        // Build tables for the report
                        var snapshotRows = grouped.Select(g => new[] { g.Service ?? "", g.Total.ToString(), g.AtRisk.ToString(), g.MissingEta.ToString() }).ToList();
                        var snapshotTable = new Table(new[] { "Service", "Total", "AtRisk", "MissingETA" }, snapshotRows);

                        // Top items table (top N) — include score and key fields
                        var topRows = top.Select(x => {
                            var r = x.Row;
                            var title = string.IsNullOrWhiteSpace(r.ActionItemTitle) ? r.KpiTitle : r.ActionItemTitle;
                            var due = DateTime.TryParse(r.CurrentDueDate, out var dd) ? dd.ToString("yyyy-MM-dd") : "—";
                            var eta = string.IsNullOrWhiteSpace(r.CurrentETA) ? "—" : r.CurrentETA.Trim();
                            var owner = string.IsNullOrWhiteSpace(r.AssignedTo) ? "Unassigned" : r.AssignedTo.Trim();
                            return new[] { r.ServiceName ?? "", Utilities.TruncatePlain(title, 60), due, eta, owner, r.SLAState ?? "", x.Score.ToString("0.0"), r.URL ?? "" };
                        }).ToList();
                        var topTable = new Table(new[] { "Service", "Title", "Due", "ETA", "Owner", "SLA", "Score", "URL" }, topRows);

                        // Build and render report (with structured tables and the action plan)
                        var report = Report.Create($"S360 Triage: {prof.Name}")
                            .Section("Snapshot", sec => sec.TableBlock(snapshotTable))
                            .Section($"Top {top.Count} Items", sec => sec.TableBlock(topTable))
                            .Section("Manager Briefing", sec => sec.Bulleted(bulletLines.ToArray()));

                        // Append structured action plan (async; will call provider per-item when available)
                        await S360Insights.AppendGroupedActionPlanAsync(report, top);

                        // Persist and render
                        await ContextManager.AddContent(report.ToMarkdown(), $"s360/{prof.Name}/triage");
                        Program.ui.RenderReport(report);

                        realtime.WriteLine("Triage complete.");
                        return Command.Result.Success;

                    }
                },
                new Command {
                    Name = "slices", Description = () => "Show focused slices: stale/no-eta/due-soon/needs-owner/at-risk-sla/delegated/churny-eta/off-track-wave/burndown-negative",
                    Action = async () => {
                        var prof = await PickProfile(); if (prof is null) return Command.Result.Failed;
                        var sliceNames = new[]{"stale","no-eta","due-soon","needs-owner","at-risk-sla","delegated","churny-eta","off-track-wave","burndown-negative"};
                        var sel = await Program.ui.RenderMenuAsync("Pick slice:", sliceNames.ToList());
                        var chosen = sel ?? sliceNames[0];
                        var limitForm = UiForm.Create("Slice options", new S360LimitModel { Limit = 25 });
                        limitForm.AddInt<S360LimitModel>("Limit", m => m.Limit, (m,v)=> m.Limit = v).IntBounds(1,500).WithHelp("Max items to fetch (1-500).");
                        if (!await Program.ui.ShowFormAsync(limitForm)) { return Command.Result.Cancelled; }
                        var lim = ((S360LimitModel)limitForm.Model!).Limit;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.s360.slice",
                            new SliceS360Input { ProfileName = prof.Name, Slice = chosen, Limit = lim });
                        using var output = Program.ui.BeginRealtime($"Fetching '{chosen}' slice...");
                        output.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
            }
        };

        // ----- local helpers (mirror KustoCommands style) -----
        static async Task<S360Profile?> PickProfile()
        {
            var profiles = Program.userManagedData.GetItems<S360Profile>().OrderBy(p => p.Name).ToList();
            if (profiles.Count == 0)
            {
                using var output = Program.ui.BeginRealtime("Looking for S360 profiles...");
                output.WriteLine("No S360 Profile found. Add one in Data → S360 Profile.");
                return null;
            }

            if (profiles.Count == 1) return profiles[0];
            var choices = profiles.Select(p => $"{p.Name} (services:{p.ServiceIds.Count})").ToList();
            var sel = await Program.ui.RenderMenuAsync("Select S360 profile:", choices);
            if (sel == null) return null;
            var idx = choices.IndexOf(sel);
            if (idx >= 0) return profiles[idx];
            var name = sel.Split('(')[0].Trim();
            return profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}