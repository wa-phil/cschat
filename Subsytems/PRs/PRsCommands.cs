using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

public static class PRsCommands
{
    private class PRsWindowModel { public int Window { get; set; } = 14; }
    private class PRsLimitModel { public int Limit { get; set; } = 25; }
    private class PRsMaxModel { public int Max { get; set; } = 2; }
    public static Command Commands(PRsClient prs)
    {
        return new Command
        {
            Name = "PRs",
            Description = () => "PR triage & coaching (Kusto-backed)",
            SubCommands = new List<Command>
            {
                new Command {
                    Name = "fetch", Description = () => "Fetch PRs for a profile (stale/new/closed)",
                    Action = async () => {
                        var prof = PickProfile(); if (prof is null) return Command.Result.Failed;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.prs.fetch",
                            new FetchPRsInput { ProfileName = prof.Name });
                        Program.ui.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "report", Description = () => "Manager report (recent window: counts + linkable bullets)",
                    Action = async () => {
                        var prof = PickProfile(); if (prof is null) return Command.Result.Failed;
                        var winForm = UiForm.Create("Report options", new PRsWindowModel { Window = 14 });
                        winForm.AddInt<PRsWindowModel>("WindowDays", m => m.Window, (m,v)=> m.Window = v).IntBounds(1,60).WithHelp("Days back (1-60).");
                        if (!await Program.ui.ShowFormAsync(winForm)) { return Command.Result.Cancelled; }
                        var w = ((PRsWindowModel)winForm.Model!).Window;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.prs.report",
                            new ReportPRsInput { ProfileName = prof.Name, WindowDays = w });
                        Program.ui.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "slice", Description = () => "Slice: stale/new/closed (filter & export)",
                    Action = async () => {
                        var prof = PickProfile(); if (prof is null) return Command.Result.Failed;
                        var sliceNames = new[]{"stale","new","closed"};
                        var sel = Program.ui.RenderMenu("Pick slice:", sliceNames.ToList());
                        var chosen = sel ?? sliceNames[0];
                        var sliceForm = UiForm.Create("Slice options", new PRsLimitModel { Limit = 25 });
                        sliceForm.AddInt<PRsLimitModel>("Limit", m => m.Limit, (m,v)=> m.Limit = v).IntBounds(1,500).WithHelp("Max items (1-500).");
                        if (!await Program.ui.ShowFormAsync(sliceForm)) { return Command.Result.Cancelled; }
                        var lim = ((PRsLimitModel)sliceForm.Model!).Limit;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.prs.slice",
                            new SlicePRsInput { ProfileName = prof.Name, Slice = chosen, Limit = lim });
                        Program.ui.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "coach", Description = () => "Analyze PR comment threads and suggest coaching points",
                    Action = async () => {
                        var prof = PickProfile(); if (prof is null) return Command.Result.Failed;
                        var coachForm = UiForm.Create("Coach options", new PRsMaxModel { Max = 2 });
                        coachForm.AddInt<PRsMaxModel>("MaxPerIC", m => m.Max, (m,v)=> m.Max = v).IntBounds(1,10).WithHelp("Max PRs per IC (1-10).");
                        if (!await Program.ui.ShowFormAsync(coachForm)) { return Command.Result.Cancelled; }
                        var m = ((PRsMaxModel)coachForm.Model!).Max;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.prs.coach",
                            new CoachPRsInput { ProfileName = prof.Name, MaxPerIC = m });
                        Program.ui.WriteLine(resp);
                        return Command.Result.Success;
                    }
                }
            }
        };

        static PRsProfile? PickProfile()
        {
            var profiles = Program.userManagedData.GetItems<PRsProfile>().OrderBy(p => p.Name).ToList();
            if (profiles.Count == 0) { Program.ui.WriteLine("No PRs Profile found. Add one in Data → PRs Profile."); return null; }
            if (profiles.Count == 1) return profiles[0];
            var choices = profiles.Select(p => p.ToString()).ToList();
            var sel = Program.ui.RenderMenu("Select PRs profile:", choices);
            if (sel == null) return null;
            var idx = choices.IndexOf(sel);
            if (idx >= 0) return profiles[idx];
            var name = sel.Split('(')[0].Trim();
            return profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
