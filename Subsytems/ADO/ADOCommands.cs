using System.Linq;
using System.Text.RegularExpressions;

public static class ADOCommands
{
    private class WorkItemLookupModel { public int Id { get; set; } }
    private class TopNModel { public int N { get; set; } = 15; }
    private class ProjectPickModel { public string? Project { get; set; } }

    public static Command Commands()
    {
        return new Command
        {
            Name = "ADO",
            Description = () => "Azure DevOps commands",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "workitem", Description = () => "Work item management commands",
                    SubCommands = new List<Command>
                    {
                        new Command
                        {
                            Name = "lookup by ID", Description = () => "Get work item by ID",
                            Action = async () =>
                            {
                                var adoClient = Program.SubsystemManager.Get<AdoClient>();
                                var form = UiForm.Create("Lookup Work Item", new WorkItemLookupModel { Id = 0 });
                                form.AddInt<WorkItemLookupModel>("ID", m => m.Id, (m,v)=> m.Id = v).IntBounds(1, 10_000_000);
                                if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                                var id = ((WorkItemLookupModel)form.Model!).Id;
                                var workItem = await Program.SubsystemManager.Get<AdoClient>().GetWorkItemSummaryById(id);
                                Program.ui.WriteLine(workItem.ToString());
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "summarize item", Description = () => "Select items from a saved query and summarize it",
                            Action = async () =>
                            {
                                // Get saved queries from UserManagedData
                                var savedQueries = Program.userManagedData.GetItems<UserSelectedQuery>();

                                if (savedQueries == null || savedQueries.Count == 0)
                                {
                                    Program.ui.WriteLine("No saved queries found. Use 'ADO queries browse' to add queries first.");
                                    return Command.Result.Cancelled;
                                }

                                // Build menu choices from saved queries
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var selected = Program.ui.RenderMenu(header, choices);

                                if (string.IsNullOrWhiteSpace(selected))
                                {
                                    return Command.Result.Cancelled;
                                }

                                // Find the selected query by matching the display text
                                var selectedIndex = choices.IndexOf(selected);
                                if (selectedIndex < 0 || selectedIndex >= savedQueries.Count)
                                {
                                    Program.ui.WriteLine("Invalid selection.");
                                    return Command.Result.Failed;
                                }

                                var selectedQuery = savedQueries[selectedIndex];
                                var queryId = selectedQuery.Id;

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var results = await ado.GetWorkItemSummariesByQueryId(queryId);
                                if (results == null || results.Count == 0)
                                {
                                    Program.ui.WriteLine("(no work items found)");
                                    return Command.Result.Success;
                                }

                                // Build aligned rows for the interactive menu
                                var workItemChoices = results.ToMenuRows();

                                // Header text with aligned columns
                                const int idW = 9, changedW = 10, assignedW = 25;
                                int consoleW = Program.ui.Width;

                                var workItemHeader =
                                    $"{ "ID".PadLeft(idW) } {"Changed".PadLeft(changedW)} {"Assigned".PadRight(assignedW)} Title\n" +
                                    new string('─', Math.Max(60, consoleW - 1));

                                // Loop: show work-item menu, summarize chosen item, then ask to repeat
                                while (true)
                                {
                                    // Use the existing interactive menu infra
                                    var selectedWorkItem = Program.ui.RenderMenu(workItemHeader, workItemChoices); // returns selected row text or null
                                    if (string.IsNullOrWhiteSpace(selectedWorkItem))
                                    {
                                        return Command.Result.Cancelled;
                                    }

                                    // Parse the ID from the selected row (first column)
                                    var idText = selectedWorkItem.Substring(0, Math.Min(selectedWorkItem.Length, 6)).Trim();
                                    var m = Regex.Match(selectedWorkItem, @"^\s*(\d+)");
                                    if (!m.Success || !int.TryParse(m.Groups[1].Value, out var selectedId))
                                    {
                                        Program.ui.WriteLine("Could not determine selected work item ID.");
                                        return Command.Result.Failed;
                                    }

                                    var picked = results.FirstOrDefault(r => r.Id == selectedId);
                                    if (picked == null)
                                    {
                                        Program.ui.WriteLine("Selected work item not found.");
                                        return Command.Result.Failed;
                                    }

                                    // Build a details blob and summarize it (same pattern as misc_tools TextSummary)
                                    var blob = picked.ToDetailText();

                                    try
                                    {
                                        var prompt =
@"Summarize this Azure DevOps work item for a teammate.
Focus on: current state, priority, assignee, most recent changes, and key discussion points.
Be concise and include a short bullet list of actionable next steps if any.";
                                        var ctx = new Context(prompt);
                                        ctx.AddUserMessage(blob);
                                        var summary = await Engine.Provider!.PostChatAsync(ctx, 0.2f);

                                        Program.ui.WriteLine();
                                        Program.ui.WriteLine($"URL: {Program.SubsystemManager.Get<AdoClient>().GetOrganizationUrl()}/_workitems/edit/{picked.Id}");
                                        Program.ui.WriteLine("—— Work Item Summary ——");
                                        Program.ui.WriteLine(summary);
                                        Program.ui.WriteLine("—— End Summary ——");
                                    }
                                    catch (Exception ex)
                                    {
                                        Program.ui.WriteLine("Summarization failed; showing raw details instead.");
                                        Program.ui.WriteLine();
                                        Program.ui.WriteLine(blob);
                                        Program.ui.WriteLine();
                                        Program.ui.WriteLine($"[error: {ex.Message}]");
                                    }

                                    // Ask whether to show another item; default is Yes on empty input
                                    if (await Program.ui.ConfirmAsync("Look at another item?", true)) continue; else break;
                                }

                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "top",
                            Description = () => "Show top-N attention-worthy work items (ranked by signals)",
                            Action = async () =>
                            {
                                var savedQueries = Program.userManagedData.GetItems<UserSelectedQuery>();
                                if (savedQueries == null || savedQueries.Count == 0)
                                {
                                    Program.ui.WriteLine("No saved queries found. Use 'ADO queries browse' first.");
                                    return Command.Result.Cancelled;
                                }

                                // Pick query
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query for ranking:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var selected = Program.ui.RenderMenu(header, choices);
                                if (string.IsNullOrWhiteSpace(selected)) { return Command.Result.Cancelled; }
                                var q = savedQueries[choices.IndexOf(selected)];

                                // N prompt
                                var topForm = UiForm.Create("Top items", new TopNModel { N = 15 });
                                topForm.AddInt<TopNModel>("Count", m => m.N, (m,v)=> m.N = v).IntBounds(1,100).WithHelp("Number of items to rank (1-100).");
                                if (!await Program.ui.ShowFormAsync(topForm)) { return Command.Result.Cancelled; }
                                var topN = ((TopNModel)topForm.Model!).N;

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var items = await ado.GetWorkItemSummariesByQueryId(q.Id);
                                if (items == null || items.Count == 0)
                                {
                                    Program.ui.WriteLine("(no work items found)");
                                    return Command.Result.Success;
                                }

                                var (ranked, _, _, _) = AdoInsights.Analyze(items, Program.config.Ado.Insights);

                                topN = Math.Min(topN, ranked.Count);
                                Program.ui.WriteLine();
                                Program.ui.WriteLine($"—— Top {topN} attention-worthy items ——");
                                Program.ui.WriteLine($"{"ID",8}  {"Score",5}  {"State",-12} {"Pri",3} {"Due",10}  Title");
                                Program.ui.WriteLine(new string('─', Math.Max(60, Program.ui.Width - 1)));

                                foreach (var s in ranked.Take(topN))
                                {
                                    var due = s.Item.DueDate?.ToString("yyyy-MM-dd") ?? "—";
                                    var pri = string.IsNullOrWhiteSpace(s.Item.Priority) ? "-" : s.Item.Priority!;
                                    var title = Utilities.TruncatePlain(s.Item.Title, Math.Max(30, Program.ui.Width - 40));
                                    Program.ui.WriteLine($"{s.Item.Id,8}  {s.Score,5:0.0}  {s.Item.State,-12} {pri,3} {due,10}  {title}");
                                }

                                Program.ui.WriteLine();
                                Program.ui.WriteLine("Signals legend per item is available in the action-plan view; this list is score-only.");
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "summarize query",
                            Description = () => "Summarize a saved query: themes, risks, and a crisp manager briefing",
                            Action = async () =>
                            {
                                var savedQueries = Program.userManagedData.GetItems<UserSelectedQuery>();
                                if (savedQueries == null || savedQueries.Count == 0)
                                {
                                    Program.ui.WriteLine("No saved queries found. Use 'ADO queries browse' first.");
                                    return Command.Result.Cancelled;
                                }

                                // Pick query
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query to summarize:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var selected = Program.ui.RenderMenu(header, choices);
                                if (string.IsNullOrWhiteSpace(selected)) return Command.Result.Cancelled;
                                var q = savedQueries[choices.IndexOf(selected)];

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var items = await ado.GetWorkItemSummariesByQueryId(q.Id);
                                if (items == null || items.Count == 0)
                                {
                                    Program.ui.WriteLine("(no work items found)");
                                    return Command.Result.Success;
                                }

                                // Compute aggregates
                                var (_, byState, byTag, byArea) = AdoInsights.Analyze(items, Program.config.Ado.Insights);

                                // Print a quick numeric snapshot (useful even if LLM fails)
                                Program.ui.WriteLine();
                                Program.ui.WriteLine("—— Snapshot ——");
                                Program.ui.WriteLine("By State:");
                                foreach (var kv in byState.OrderByDescending(kv => kv.Value).Take(10))
                                {
                                    Program.ui.WriteLine($"  {kv.Key,-16} {kv.Value,5}");
                                }

                                Program.ui.WriteLine("Top Tags:");
                                foreach (var kv in byTag.OrderByDescending(kv => kv.Value).Take(10))
                                {
                                    Program.ui.WriteLine($"  {kv.Key,-16} {kv.Value,5}");
                                }

                                Program.ui.WriteLine("Top Areas:");
                                foreach (var kv in byArea.OrderByDescending(kv => kv.Value).Take(10))
                                {
                                    Program.ui.WriteLine($"  {kv.Key,-32} {kv.Value,5}");
                                }

                                // Ask LLM for a crisp briefing
                                try
                                {
                                    var prompt = AdoInsights.MakeManagerBriefingPrompt(byState, byTag, byArea, items);
                                    var ctx = new Context(prompt);
                                    var briefing = await Engine.Provider!.PostChatAsync(ctx, 0.2f);

                                    Program.ui.WriteLine();
                                    Program.ui.WriteLine("—— 30-second Briefing ——");
                                    Program.ui.WriteLine(briefing);
                                    Program.ui.WriteLine("—— End Briefing ——");
                                }
                                catch (Exception ex)
                                {
                                    Program.ui.WriteLine();
                                    Program.ui.WriteLine($"[briefing failed: {ex.Message}]");
                                }

                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "triage",
                            Description = () => "Pick a saved query → briefing, top items, and action plan",
                            Action = async () =>
                            {
                                var saved = Program.userManagedData.GetItems<UserSelectedQuery>();
                                if (saved.Count == 0)
                                {
                                    Program.ui.WriteLine("No saved queries. Run: ADO queries browse");
                                    return Command.Result.Cancelled;
                                }

                                var choices = saved.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query to triage:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var pickedStr = Program.ui.RenderMenu(header, choices);
                                if (string.IsNullOrWhiteSpace(pickedStr)) return Command.Result.Cancelled;
                                var idx = choices.IndexOf(pickedStr);
                                var picked = saved[idx];

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var items = await ado.GetWorkItemSummariesByQueryId(picked.Id);
                                if (items.Count == 0)
                                {
                                    Program.ui.WriteLine("(no work items found)");
                                    return Command.Result.Success;
                                }

                                // Insights + scoring
                                var (ranked, byState, byTag, byArea) = AdoInsights.Analyze(items, Program.config.Ado.Insights);

                                // 1) Manager’s 30-sec briefing
                                try
                                {
                                    var briefingPrompt = AdoInsights.MakeManagerBriefingPrompt(byState, byTag, byArea, items);
                                    var ctx1 = new Context(briefingPrompt);
                                    var briefing = await Engine.Provider!.PostChatAsync(ctx1, 0.2f);

                                    Program.ui.WriteLine();
                                    Program.ui.WriteLine("—— 30-second Briefing ——");
                                    Program.ui.WriteLine(briefing);
                                    Program.ui.WriteLine("—— End Briefing ——");
                                }
                                catch (Exception ex)
                                {
                                    Program.ui.WriteLine($"[briefing failed: {ex.Message}]");
                                }

                                // 2) Top “interesting” items
                                var topN = Math.Min(15, ranked.Count);
                                Program.ui.WriteLine();
                                Program.ui.WriteLine($"—— Top {topN} attention-worthy items ——");
                                Program.ui.WriteLine($"{"ID",8}  {"Score",5}  {"State",-12} {"Pri",3} {"Due",10}  Title");
                                foreach (var s in ranked.Take(topN))
                                {
                                    Program.ui.WriteLine($"{s.Item.Id,8}  {s.Score,5:0.0}  [{s.Item.State,8}]  P:{s.Item.Priority,-2}  {(s.Item.DueDate?.ToString("yyyy-MM-dd") ?? "—"),10}  {s.Item.Title}");
                                }
                                Program.ui.WriteLine("—— End Top Items ——");

                                // 3) Action plan (LLM on top subset)
                                try
                                {
                                    var planPrompt = AdoInsights.MakeActionPlanPrompt(ranked.Take(10));
                                    var ctx2 = new Context(planPrompt);
                                    var plan = await Engine.Provider!.PostChatAsync(ctx2, 0.2f);

                                    Program.ui.WriteLine();
                                    Program.ui.WriteLine("—— Action Plan ——");
                                    Program.ui.WriteLine(plan);
                                    Program.ui.WriteLine("—— End Action Plan ——");
                                }
                                catch (Exception ex)
                                {
                                    Program.ui.WriteLine($"[action plan failed: {ex.Message}]");
                                }

                                return Command.Result.Success;
                            }
                        }
                    }
                },
                new Command
                {
                    Name = "browse", Description = () => "Open a query picker (navigate folders; Enter on a query prints its GUID).",
                    Action = async () =>
                    {
                        var ado = Program.SubsystemManager.Get<AdoClient>();

                        var defaultProject = Program.config.Ado.ProjectName;
                        var projForm = UiForm.Create("ADO Project", new ProjectPickModel { Project = defaultProject });
                        projForm.AddString<ProjectPickModel>("Project", m => m.Project ?? "", (m,v)=> m.Project = v).MakeOptional();
                        await Program.ui.ShowFormAsync(projForm); // even if cancelled, fall back to default
                        var project = ((ProjectPickModel)projForm.Model!).Project ?? defaultProject;

                        while (true)
                        {
                            // Top-level options
                            var topChoices = new List<string>
                            {
                                "📁 My Queries",
                                "📁 Shared Queries"
                            };
                            var header = $"ADO Queries — {project}\nSelect a query to print its GUID (Press ESC to cancel)\n" +
                                        new string('─', Math.Max(60, Program.ui.Width - 1));

                            var pick = Program.ui.RenderMenu(header, topChoices);
                            if (string.IsNullOrWhiteSpace(pick)) return Command.Result.Cancelled;

                            // Folder navigation loop
                            var root = pick.Contains("My Queries") ? "My Queries" : "Shared Queries";
                            string current = root;
                            string parentOf(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

                            while (true)
                            {
                                var items = await ado.GetQueryChildrenAsync(project, current, depth: current == root ? 0 : 1);
                                var choices = new List<string>();

                                if (current != root) choices.Add(".. (Up)");
                                choices.AddRange(items.Select(i => (i.IsFolder ? "📁 " : "📄 ") + i.Name));

                                var head = $"{current}\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var selection = Program.ui.RenderMenu(head, choices);
                                if (string.IsNullOrWhiteSpace(selection)) break; // back to top-level

                                if (selection.StartsWith(".."))
                                {
                                    current = parentOf(current);
                                    if (string.IsNullOrEmpty(current)) break;
                                    continue;
                                }

                                var idx = choices.IndexOf(selection) - (current != root ? 1 : 0);
                                if (idx < 0 || idx >= items.Count) continue;

                                var picked = items[idx];
                                if (picked.IsFolder)
                                {
                                    current = picked.Path;
                                    continue; // dive in
                                }

                                // It's a query → print the GUID
                                if (picked.Id.HasValue)
                                {
                                    Program.userManagedData.AddItem(new UserSelectedQuery(picked.Id.Value, picked.Name, project, picked.Path));
                                    Program.ui.WriteLine($"Added query '{picked.Name}' (ID: {picked.Id.Value}) to User Selected Queries.");
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    return Command.Result.Success;
                                }

                                Program.ui.WriteLine("Selected item has no ID.");
                                return Command.Result.Failed;
                            }
                        }
                    }
                }
            }
        };
    }
}