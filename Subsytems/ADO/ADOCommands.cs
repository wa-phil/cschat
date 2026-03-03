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
                            Name = "summarize item", Description = () => "Select items from a saved query and summarize it",
                            Action = async () =>
                            {
                                // Get saved queries from UserManagedData
                                var savedQueries = Program.userManagedData.GetItems<UserSelectedQuery>();

                                if (savedQueries == null || savedQueries.Count == 0)
                                {
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "No saved queries found. Use 'ADO queries browse' first."));
                                    return Command.Result.Cancelled;
                                }

                                // Build menu choices from saved queries
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var selected = await Program.ui.RenderMenuAsync(header, choices);

                                if (string.IsNullOrWhiteSpace(selected))
                                {
                                    return Command.Result.Cancelled;
                                }

                                // Find the selected query by matching the display text
                                var selectedIndex = choices.IndexOf(selected);
                                if (selectedIndex < 0 || selectedIndex >= savedQueries.Count)
                                {
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "Invalid selection."));
                                    return Command.Result.Failed;
                                }

                                var selectedQuery = savedQueries[selectedIndex];
                                var queryId = selectedQuery.Id;

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var results = await ado.GetWorkItemSummariesByQueryId(queryId);
                                if (results == null || results.Count == 0)
                                {
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "(no work items found)"));
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
                                    var selectedWorkItem = await Program.ui.RenderMenuAsync(workItemHeader, workItemChoices); // returns selected row text or null
                                    if (string.IsNullOrWhiteSpace(selectedWorkItem))
                                    {
                                        return Command.Result.Cancelled;
                                    }

                                    // Parse the ID from the selected row (first column)
                                    var idText = selectedWorkItem.Substring(0, Math.Min(selectedWorkItem.Length, 6)).Trim();
                                    var m = Regex.Match(selectedWorkItem, @"^\s*(\d+)");
                                    if (!m.Success || !int.TryParse(m.Groups[1].Value, out var selectedId))
                                    {
                                        Log.Method(ctx=> ctx.Append(Log.Data.Message, "Could not determine selected work item ID."));
                                        return Command.Result.Failed;
                                    }

                                    var picked = results.FirstOrDefault(r => r.Id == selectedId);
                                    if (picked == null)
                                    {
                                        Log.Method(ctx=> ctx.Append(Log.Data.Message, "Selected work item not found."));
                                        return Command.Result.Failed;
                                    }

                                    // Build a details blob and summarize it (same pattern as misc_tools TextSummary)
                                    var blob = picked.ToDetailText();

                                    try
                                    {
                                        using var output = Program.ui.BeginRealtime($"Summarizing workitem #{picked.Id}...");
                                        var prompt =
@"Summarize this Azure DevOps work item for a teammate.
Focus on: current state, priority, assignee, most recent changes, and key discussion points.
Be concise and include a short bullet list of actionable next steps if any.";
                                        var ctx = new Context(prompt);
                                        ctx.AddUserMessage(blob);
                                        var summary = await Engine.Provider!.PostChatAsync(ctx, 0.2f);

                                        output.WriteLine($"URL: {Program.SubsystemManager.Get<AdoClient>().GetOrganizationUrl()}/_workitems/edit/{picked.Id}");
                                        output.WriteLine("—— Work Item Summary ——");
                                        output.WriteLine(summary);
                                        output.WriteLine("—— End Summary ——");
                                    }
                                    catch (Exception ex)
                                    {
                                        using var output = Program.ui.BeginRealtime($"Error summarizing workitem #{picked.Id}");
                                        output.WriteLine("Summarization failed; showing raw details instead.");
                                        output.WriteLine();
                                        output.WriteLine(blob);
                                        output.WriteLine();
                                        output.WriteLine($"[error: {ex.Message}]");
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
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "No saved queries found. Use 'ADO queries browse' first."));
                                    return Command.Result.Cancelled;
                                }

                                // Pick query
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query for ranking:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var selected = await Program.ui.RenderMenuAsync(header, choices);
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
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "(no work items found)"));
                                    return Command.Result.Success;
                                }

                                var (ranked, _, _, _) = AdoInsights.Analyze(items, Program.config.Ado.Insights);

                                var report = Report.Create($"ADO Top Attention Items: {q.Name}")
                                    .Paragraph($"Project: {q.Project} | Path: {q.Path} | Items analyzed: {items.Count}");

                                if (ranked.Count == 0)
                                {
                                    report.Paragraph("No items produced a ranking score.");
                                    Program.ui.RenderReport(report);
                                    return Command.Result.Success;
                                }

                                topN = Math.Min(topN, ranked.Count);

                                var rows = ranked
                                    .Take(topN)
                                    .Select((s, idx) => new[]
                                    {
                                        (idx + 1).ToString(),
                                        s.Item.Id.ToString(),
                                        s.Score.ToString("0.0"),
                                        s.Item.State ?? string.Empty,
                                        string.IsNullOrWhiteSpace(s.Item.Priority) ? "-" : s.Item.Priority!,
                                        s.Item.DueDate?.ToString("yyyy-MM-dd") ?? "—",
                                        Utilities.TruncatePlain(s.Item.Title, 80)
                                    })
                                    .ToList();

                                var table = new Table(
                                    new[] { "Rank", "ID", "Score", "State", "Pri", "Due", "Title" },
                                    rows);

                                report.Section($"Top {topN} Attention-worthy Items", sec => sec.TableBlock(table));

                                report.Section("Notes", sec =>
                                {
                                    sec.Paragraph("Signals legend per item is available in the action-plan view; this list is score-only.");
                                });

                                Program.ui.RenderReport(report);
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
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "No saved queries found. Use 'ADO queries browse' first."));
                                    return Command.Result.Cancelled;
                                }

                                // Pick query
                                var choices = savedQueries.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query to summarize:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var selected = await Program.ui.RenderMenuAsync(header, choices);
                                if (string.IsNullOrWhiteSpace(selected)) return Command.Result.Cancelled;
                                var q = savedQueries[choices.IndexOf(selected)];

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var items = await ado.GetWorkItemSummariesByQueryId(q.Id);
                                if (items == null || items.Count == 0)
                                {
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "(no work items found)"));
                                    return Command.Result.Success;
                                }

                                // Compute aggregates
                                var (_, byState, byTag, byArea) = AdoInsights.Analyze(items, Program.config.Ado.Insights);

                                var report = Report.Create($"ADO Query Summary: {q.Name}")
                                    .Paragraph($"Project: {q.Project} | Path: {q.Path} | Items: {items.Count}");

                                // Snapshot section with three small tables
                                report.Section("Snapshot", snap =>
                                {
                                    var stateRows = byState.OrderByDescending(k => k.Value).Take(10)
                                        .Select(kv => new[] { kv.Key, kv.Value.ToString() }).ToList();
                                    if (stateRows.Any()) snap.Section("By State", s => s.TableBlock(new Table(new[] {"State","Count"}, stateRows)));

                                    var tagRows = byTag.OrderByDescending(k => k.Value).Take(10)
                                        .Select(kv => new[] { kv.Key, kv.Value.ToString() }).ToList();
                                    if (tagRows.Any()) snap.Section("Top Tags", s => s.TableBlock(new Table(new[] {"Tag","Count"}, tagRows)));

                                    var areaRows = byArea.OrderByDescending(k => k.Value).Take(10)
                                        .Select(kv => new[] { kv.Key, kv.Value.ToString() }).ToList();
                                    if (areaRows.Any()) snap.Section("Top Areas", s => s.TableBlock(new Table(new[] {"Area","Count"}, areaRows)));
                                });

                                // 30-second briefing via LLM
                                string? briefing = null;
                                try
                                {
                                    var prompt = AdoInsights.MakeManagerBriefingPrompt(byState, byTag, byArea, items);
                                    var ctx = new Context(prompt);
                                    briefing = await Engine.Provider!.PostChatAsync(ctx, 0.2f);
                                }
                                catch (Exception ex)
                                {
                                    briefing = $"[briefing failed: {ex.Message}]";
                                }

                                if (!string.IsNullOrWhiteSpace(briefing))
                                {
                                    report.Section("30-second Briefing", sec =>
                                    {
                                        var parts = briefing!.Split(new[] {"\r\n\r\n","\n\n"}, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length == 1) sec.Paragraph(briefing!); else foreach (var p in parts) sec.Paragraph(p.Trim());
                                    });
                                }

                                Program.ui.RenderReport(report);
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
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "No saved queries. Run: ADO queries browse"));
                                    return Command.Result.Cancelled;
                                }

                                var choices = saved.Select(q => $"{q.Name} ({q.Project}) - {q.Path}").ToList();
                                var header = "Select a saved query to triage:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                var pickedStr = await Program.ui.RenderMenuAsync(header, choices);
                                if (string.IsNullOrWhiteSpace(pickedStr)) return Command.Result.Cancelled;
                                var idx = choices.IndexOf(pickedStr);
                                var picked = saved[idx];

                                var ado = Program.SubsystemManager.Get<AdoClient>();
                                var items = await ado.GetWorkItemSummariesByQueryId(picked.Id);
                                if (items.Count == 0)
                                {
                                    Log.Method(ctx=> ctx.Append(Log.Data.Message, "(no work items found)"));
                                    return Command.Result.Success;
                                }

                                // Insights + scoring
                                var (ranked, byState, byTag, byArea) = AdoInsights.Analyze(items, Program.config.Ado.Insights);

                                // Build a structured report instead of raw console writes
                                var report = Report.Create($"ADO Triage: {picked.Name}")
                                    .Paragraph($"Project: {picked.Project} | Query Path: {picked.Path} | Items: {items.Count}");

                                string? briefingText = null;
                                try
                                {
                                    var briefingPrompt = AdoInsights.MakeManagerBriefingPrompt(byState, byTag, byArea, items);
                                    var ctx1 = new Context(briefingPrompt);
                                    briefingText = await Engine.Provider!.PostChatAsync(ctx1, 0.2f);
                                }
                                catch (Exception ex)
                                {
                                    briefingText = $"[briefing failed: {ex.Message}]";
                                }

                                if (!string.IsNullOrWhiteSpace(briefingText))
                                {
                                    report.Section("30-second Briefing", r =>
                                    {
                                        // Split briefing into paragraphs if it has blank lines
                                        var parts = briefingText!.Split(new[] {"\r\n\r\n","\n\n"}, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length == 1)
                                        {
                                            r.Paragraph(briefingText!);
                                        }
                                        else
                                        {
                                            foreach (var p in parts) r.Paragraph(p.Trim());
                                        }
                                    });
                                }

                                // Top items table
                                var topN = Math.Min(15, ranked.Count);
                                var topRows = new List<string[]>();
                                topRows.AddRange(ranked.Take(topN).Select(s => new[]
                                {
                                    s.Item.Id.ToString(),
                                    s.Score.ToString("0.0"),
                                    s.Item.State ?? "", 
                                    s.Item.Priority ?? "", 
                                    s.Item.DueDate?.ToString("yyyy-MM-dd") ?? "—",
                                    Utilities.TruncatePlain(s.Item.Title, 80)
                                }));
                                var topTable = new Table(new [] {"ID","Score","State","Pri","Due","Title"}, topRows);
                                report.Section($"Top {topN} Attention-worthy Items", r => r.TableBlock(topTable));

                                // Action Plan
                                string? planText = null;
                                try
                                {
                                    var planPrompt = AdoInsights.MakeActionPlanPrompt(ranked.Take(10));
                                    var ctx2 = new Context(planPrompt);
                                    planText = await Engine.Provider!.PostChatAsync(ctx2, 0.2f);
                                }
                                catch (Exception ex)
                                {
                                    planText = $"[action plan failed: {ex.Message}]";
                                }

                                if (!string.IsNullOrWhiteSpace(planText))
                                {
                                    report.Section("Action Plan", r =>
                                    {
                                        var parts = planText!.Split(new[] {"\r\n\r\n","\n\n"}, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length == 1) r.Paragraph(planText!); else foreach (var p in parts) r.Paragraph(p.Trim());
                                    });
                                }

                                Program.ui.RenderReport(report);
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

                            var pick = await Program.ui.RenderMenuAsync(header, topChoices);
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
                                var selection = await Program.ui.RenderMenuAsync(head, choices);
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
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    return Command.Result.Success;
                                }

                                Log.Method(ctx=> ctx.Append(Log.Data.Message, "Selected item has no ID."));
                                return Command.Result.Failed;
                            }
                        }
                    }
                }
            }
        };
    }
}