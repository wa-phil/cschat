using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


public static class S360Commands
{
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
                        var prof = PickProfile(); if (prof is null) return Command.Result.Failed;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.s360.fetch",
                            new FetchS360Input { ProfileName = prof.Name });
                        Program.ui.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "triage", Description = () => "Score & summarize (briefing + action plan)",
                    Action = async () => {
                        var prof = PickProfile(); if (prof is null) return Command.Result.Failed;

                        var form = UiForm.Create("Triage options", 15);
                        form.AddInt("Top N")
                            .IntBounds(min: 1, max: 100)
                            .WithHelp("Number of top action items to include in the summary, range is 1 to 100.");

                        if (!await Program.ui.ShowFormAsync(form)){
                            return Command.Result.Cancelled;
                        }
                        var n = (int)form.Model!;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.s360.triage",
                            new TriageS360Input { ProfileName = prof.Name, TopN = n });
                        Program.ui.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "slices", Description = () => "Show focused slices: stale/no-eta/due-soon/needs-owner/at-risk-sla/delegated/churny-eta/off-track-wave/burndown-negative",
                    Action = async () => {
                        var prof = PickProfile(); if (prof is null) return Command.Result.Failed;
                        var sliceNames = new[]{"stale","no-eta","due-soon","needs-owner","at-risk-sla","delegated","churny-eta","off-track-wave","burndown-negative"};
                        var sel = Program.ui.RenderMenu("Pick slice:", sliceNames.ToList());
                        var chosen = sel ?? sliceNames[0];
                        Program.ui.Write("Limit (default 25): "); var lim = int.TryParse(Program.ui.ReadLineWithHistory(), out var lv) ? lv : 25;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.s360.slice",
                            new SliceS360Input { ProfileName = prof.Name, Slice = chosen, Limit = lim });
                        Program.ui.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
            }
        };

        // ----- local helpers (mirror KustoCommands style) -----
        static S360Profile? PickProfile()
        {
            var profiles = Program.userManagedData.GetItems<S360Profile>().OrderBy(p => p.Name).ToList();
            if (profiles.Count == 0)
            {
                Program.ui.WriteLine("No S360 Profile found. Add one in Data → S360 Profile."); return null;
            }

            if (profiles.Count == 1) return profiles[0];
            var choices = profiles.Select(p => $"{p.Name} (services:{p.ServiceIds.Count})").ToList();
            var sel = Program.ui.RenderMenu("Select S360 profile:", choices);
            if (sel == null) return null;
            var idx = choices.IndexOf(sel);
            if (idx >= 0) return profiles[idx];
            var name = sel.Split('(')[0].Trim();
            return profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}