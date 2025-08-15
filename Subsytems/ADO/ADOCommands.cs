using System.Linq;
using System.Text.RegularExpressions;

public static class ADOCommands
{
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
                                Console.Write("Enter work item ID: ");
                                if (int.TryParse(Console.ReadLine(), out var id))
                                {
                                    var workItem = await Program.SubsystemManager.Get<AdoClient>().GetWorkItemSummaryById(id);
                                    Console.WriteLine(workItem);
                                    return Command.Result.Success;
                                }
                                Console.WriteLine("Invalid ID.");
                                return Command.Result.Cancelled;
                            }
                        },
                        new Command
                        {
                            Name = "summarize item", Description = () => "Select items from a saved query and summarize it",
                            Action = async () =>
                            {
                                // Get saved queries from UserManagedData
                                var userManagedData = Program.SubsystemManager.Get<UserManagedData>();
                                var savedQueries = userManagedData.GetItems<UserSelectedQuery>();

                                if (savedQueries == null || savedQueries.Count == 0)
                                {
                                    Console.WriteLine("No saved queries found. Use 'ADO queries browse' to add queries first.");
                                    return Command.Result.Cancelled;
                                }

                                // Build menu choices from saved queries
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query:\n" + new string('─', Math.Max(60, Console.WindowWidth - 1));
                                var selected = User.RenderMenu(header, choices);

                                if (string.IsNullOrWhiteSpace(selected))
                                {
                                    return Command.Result.Cancelled;
                                }

                                // Find the selected query by matching the display text
                                var selectedIndex = choices.IndexOf(selected);
                                if (selectedIndex < 0 || selectedIndex >= savedQueries.Count)
                                {
                                    Console.WriteLine("Invalid selection.");
                                    return Command.Result.Failed;
                                }

                                var selectedQuery = savedQueries[selectedIndex];
                                var queryId = selectedQuery.Id;

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var results = await ado.GetWorkItemSummariesByQueryId(queryId);
                                if (results == null || results.Count == 0)
                                {
                                    Console.WriteLine("(no work items found)");
                                    return Command.Result.Success;
                                }

                                // Build aligned rows for the interactive menu
                                var workItemChoices = results.ToMenuRows();

                                // Header text with aligned columns
                                const int idW = 9, changedW = 10, assignedW = 25;
                                int consoleW = Console.WindowWidth;

                                var workItemHeader =
                                    $"{ "ID".PadLeft(idW) } {"Changed".PadLeft(changedW)} {"Assigned".PadRight(assignedW)} Title\n" +
                                    new string('─', Math.Max(60, consoleW - 1));

                                // Loop: show work-item menu, summarize chosen item, then ask to repeat
                                while (true)
                                {
                                    // Use the existing interactive menu infra
                                    var selectedWorkItem = User.RenderMenu(workItemHeader, workItemChoices); // returns selected row text or null
                                    if (string.IsNullOrWhiteSpace(selectedWorkItem))
                                    {
                                        return Command.Result.Cancelled;
                                    }

                                    // Parse the ID from the selected row (first column)
                                    var idText = selectedWorkItem.Substring(0, Math.Min(selectedWorkItem.Length, 6)).Trim();
                                    var m = Regex.Match(selectedWorkItem, @"^\s*(\d+)");
                                    if (!m.Success || !int.TryParse(m.Groups[1].Value, out var selectedId))
                                    {
                                        Console.WriteLine("Could not determine selected work item ID.");
                                        return Command.Result.Failed;
                                    }

                                    var picked = results.FirstOrDefault(r => r.Id == selectedId);
                                    if (picked == null)
                                    {
                                        Console.WriteLine("Selected work item not found.");
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

                                        Console.WriteLine();
                                        Console.WriteLine($"URL: {Program.SubsystemManager.Get<AdoClient>().GetOrganizationUrl()}/_workitems/edit/{picked.Id}");
                                        Console.WriteLine("—— Work Item Summary ——");
                                        Console.WriteLine(summary);
                                        Console.WriteLine("—— End Summary ——");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("Summarization failed; showing raw details instead.");
                                        Console.WriteLine();
                                        Console.WriteLine(blob);
                                        Console.WriteLine();
                                        Console.WriteLine($"[error: {ex.Message}]");
                                    }

                                    // Ask whether to show another item; default is Yes on empty input
                                    Console.Write("Look at another item? [Y/n]: ");
                                    var ans = Console.ReadLine();
                                    ans = ans?.Trim() ?? string.Empty;
                                    if (string.IsNullOrWhiteSpace(ans) || ans.Equals("y", StringComparison.OrdinalIgnoreCase) || ans.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // default Yes -> continue loop
                                        continue;
                                    }

                                    // anything else -> stop
                                    break;
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
                                var userManagedData = Program.SubsystemManager.Get<UserManagedData>();
                                var savedQueries = userManagedData.GetItems<UserSelectedQuery>();
                                if (savedQueries == null || savedQueries.Count == 0)
                                {
                                    Console.WriteLine("No saved queries found. Use 'ADO queries browse' first.");
                                    return Command.Result.Cancelled;
                                }

                                // Pick query
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query for ranking:\n" + new string('─', Math.Max(60, Console.WindowWidth - 1));
                                var selected = User.RenderMenu(header, choices);
                                if (string.IsNullOrWhiteSpace(selected)) return Command.Result.Cancelled;
                                var q = savedQueries[choices.IndexOf(selected)];

                                // N prompt
                                Console.Write("How many items (default 15): ");
                                var nText = Console.ReadLine();
                                int topN = 15;
                                if (!string.IsNullOrWhiteSpace(nText) && int.TryParse(nText, out var n) && n > 0) topN = n;

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var items = await ado.GetWorkItemSummariesByQueryId(q.Id);
                                if (items == null || items.Count == 0)
                                {
                                    Console.WriteLine("(no work items found)");
                                    return Command.Result.Success;
                                }

                                var (ranked, _, _, _) = AdoInsights.Analyze(items, Program.config.Ado.Insights);

                                topN = Math.Min(topN, ranked.Count);
                                Console.WriteLine();
                                Console.WriteLine($"—— Top {topN} attention-worthy items ——");
                                Console.WriteLine($"{"ID",6}  {"Score",5}  {"State",-12} {"Pri",3} {"Due",10}  Title");
                                Console.WriteLine(new string('─', Math.Max(60, Console.WindowWidth - 1)));

                                foreach (var s in ranked.Take(topN))
                                {
                                    var due = s.Item.DueDate?.ToString("yyyy-MM-dd") ?? "—";
                                    var pri = string.IsNullOrWhiteSpace(s.Item.Priority) ? "-" : s.Item.Priority!;
                                    var title = Utilities.TruncatePlain(s.Item.Title, Math.Max(30, Console.WindowWidth - 40));
                                    Console.WriteLine($"{s.Item.Id,6}  {s.Score,5:0.0}  {s.Item.State,-12} {pri,3} {due,10}  {title}");
                                }

                                Console.WriteLine();
                                Console.WriteLine("Signals legend per item is available in the action-plan view; this list is score-only.");
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "summarize query",
                            Description = () => "Summarize a saved query: themes, risks, and a crisp manager briefing",
                            Action = async () =>
                            {
                                var userManagedData = Program.SubsystemManager.Get<UserManagedData>();
                                var savedQueries = userManagedData.GetItems<UserSelectedQuery>();
                                if (savedQueries == null || savedQueries.Count == 0)
                                {
                                    Console.WriteLine("No saved queries found. Use 'ADO queries browse' first.");
                                    return Command.Result.Cancelled;
                                }

                                // Pick query
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query to summarize:\n" + new string('─', Math.Max(60, Console.WindowWidth - 1));
                                var selected = User.RenderMenu(header, choices);
                                if (string.IsNullOrWhiteSpace(selected)) return Command.Result.Cancelled;
                                var q = savedQueries[choices.IndexOf(selected)];

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var items = await ado.GetWorkItemSummariesByQueryId(q.Id);
                                if (items == null || items.Count == 0)
                                {
                                    Console.WriteLine("(no work items found)");
                                    return Command.Result.Success;
                                }

                                // Compute aggregates
                                var (_, byState, byTag, byArea) = AdoInsights.Analyze(items, Program.config.Ado.Insights);

                                // Print a quick numeric snapshot (useful even if LLM fails)
                                Console.WriteLine();
                                Console.WriteLine("—— Snapshot ——");
                                Console.WriteLine("By State:");
                                foreach (var kv in byState.OrderByDescending(kv => kv.Value).Take(10))
                                {
                                    Console.WriteLine($"  {kv.Key,-16} {kv.Value,5}");
                                }

                                Console.WriteLine("Top Tags:");
                                foreach (var kv in byTag.OrderByDescending(kv => kv.Value).Take(10))
                                {
                                    Console.WriteLine($"  {kv.Key,-16} {kv.Value,5}");
                                }

                                Console.WriteLine("Top Areas:");
                                foreach (var kv in byArea.OrderByDescending(kv => kv.Value).Take(10))
                                {
                                    Console.WriteLine($"  {kv.Key,-32} {kv.Value,5}");
                                }

                                // Ask LLM for a crisp briefing
                                try
                                {
                                    var prompt = AdoInsights.MakeManagerBriefingPrompt(byState, byTag, byArea, items);
                                    var ctx = new Context(prompt);
                                    var briefing = await Engine.Provider!.PostChatAsync(ctx, 0.2f);

                                    Console.WriteLine();
                                    Console.WriteLine("—— 30-second Briefing ——");
                                    Console.WriteLine(briefing);
                                    Console.WriteLine("—— End Briefing ——");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine();
                                    Console.WriteLine($"[briefing failed: {ex.Message}]");
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
                                var userData = Program.SubsystemManager.Get<UserManagedData>();
                                var saved = userData.GetItems<UserSelectedQuery>();
                                if (saved.Count == 0)
                                {
                                    Console.WriteLine("No saved queries. Run: ADO queries browse");
                                    return Command.Result.Cancelled;
                                }

                                var choices = saved.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query to triage:\n" + new string('─', Math.Max(60, Console.WindowWidth - 1));
                                var pickedStr = User.RenderMenu(header, choices);
                                if (string.IsNullOrWhiteSpace(pickedStr)) return Command.Result.Cancelled;
                                var idx = choices.IndexOf(pickedStr);
                                var picked = saved[idx];

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var items = await ado.GetWorkItemSummariesByQueryId(picked.Id);
                                if (items.Count == 0)
                                {
                                    Console.WriteLine("(no work items found)");
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

                                    Console.WriteLine();
                                    Console.WriteLine("—— 30-second Briefing ——");
                                    Console.WriteLine(briefing);
                                    Console.WriteLine("—— End Briefing ——");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[briefing failed: {ex.Message}]");
                                }

                                // 2) Top “interesting” items
                                var topN = Math.Min(15, ranked.Count);
                                Console.WriteLine();
                                Console.WriteLine($"—— Top {topN} attention-worthy items ——");
                                foreach (var s in ranked.Take(topN))
                                {
                                    Console.WriteLine($"{s.Item.Id,6}  {s.Score,5:0.0}  [{s.Item.State}]  P:{s.Item.Priority,-2}  {(s.Item.DueDate?.ToString("yyyy-MM-dd") ?? "—"),10}  {s.Item.Title}");
                                }
                                Console.WriteLine("—— End Top Items ——");

                                // 3) Action plan (LLM on top subset)
                                try
                                {
                                    var planPrompt = AdoInsights.MakeActionPlanPrompt(ranked.Take(10));
                                    var ctx2 = new Context(planPrompt);
                                    var plan = await Engine.Provider!.PostChatAsync(ctx2, 0.2f);

                                    Console.WriteLine();
                                    Console.WriteLine("—— Action Plan ——");
                                    Console.WriteLine(plan);
                                    Console.WriteLine("—— End Action Plan ——");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[action plan failed: {ex.Message}]");
                                }

                                return Command.Result.Success;
                            }
                        }                        
                    }
                },
                new Command
                {
                    Name = "queries",
                    Description = () => "Browse ADO queries and add to Data->User Selected Queries",
                    SubCommands = new List<Command>
                    {
                        new Command
                        {
                            Name = "browse",
                            Description = () => "Open a query picker (navigate folders; Enter on a query prints its GUID).",
                            Action = async () =>
                            {
                                var ado = Program.SubsystemManager.Get<AdoClient>();

                                var defaultProject = Program.config.Ado.ProjectName;
                                Console.Write($"Project [{defaultProject}]: ");
                                var p = Console.ReadLine();
                                var project = string.IsNullOrWhiteSpace(p) ? defaultProject : p.Trim();

                                while (true)
                                {
                                    // Top-level options
                                    var topChoices = new List<string>
                                    {
                                        "📁 My Queries",
                                        "📁 Shared Queries"
                                    };
                                    var header = $"ADO Queries — {project}\nSelect a query to print its GUID (Press ESC to cancel)\n" +
                                                new string('─', Math.Max(60, Console.WindowWidth - 1));

                                    var pick = User.RenderMenu(header, topChoices);
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

                                        var head = $"{current}\n" + new string('─', Math.Max(60, Console.WindowWidth - 1));
                                        var selection = User.RenderMenu(head, choices);
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
                                            var userManagedData = Program.SubsystemManager.Get<UserManagedData>();
                                            userManagedData.AddItem(new UserSelectedQuery(picked.Id.Value, picked.Name, project, picked.Path));
                                            Console.WriteLine($"Added query '{picked.Name}' (ID: {picked.Id.Value}) to User Selected Queries.");
                                            Config.Save(Program.config, Program.ConfigFilePath);
                                            return Command.Result.Success;
                                        }

                                        Console.WriteLine("Selected item has no ID.");
                                        return Command.Result.Failed;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}