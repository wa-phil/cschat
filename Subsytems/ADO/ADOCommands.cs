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
                            Name = "get", Description = () => "Get work item by ID",
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
                            Name = "get by query id", Description = () => "Query work items by saved query",
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