using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager : Command
{
    public static Command CreateADOConfigCommands()
    {
        return new Command
        {
            Name = "Azure Dev Ops (ADO)", Description = () => "configuration settings",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "organization", Description = () => $"Set Azure DevOps organization [currently: {Program.config.Ado.Organization}]",
                    Action = () =>
                    {
                        Program.ui.Write("Enter new Azure DevOps organization: ");
                        var org = Program.ui.ReadLine();
                        if (!string.IsNullOrWhiteSpace(org))
                        {
                            Program.config.Ado.Organization = org;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Azure DevOps organization set to: {org}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }

                },
                new Command
                {
                    Name = "project", Description = () => $"Set Azure DevOps project [currently: {Program.config.Ado.ProjectName}]",
                    Action = () =>
                    {
                        Program.ui.Write("Enter new Azure DevOps project: ");
                        var project = Program.ui.ReadLine();
                        if (!string.IsNullOrWhiteSpace(project))
                        {
                            Program.config.Ado.ProjectName = project;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Azure DevOps project set to: {project}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                new Command
                {
                    Name = "repository", Description = () => $"Set Azure DevOps repository [currently: {Program.config.Ado.RepositoryName}]",
                    Action = () =>
                    {
                        Program.ui.Write("Enter new Azure DevOps repository: ");
                        var repo = Program.ui.ReadLine();
                        if (!string.IsNullOrWhiteSpace(repo))
                        {
                            Program.config.Ado.RepositoryName = repo;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Azure DevOps repository set to: {repo}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                new Command
                {
                    Name ="Use OAuth Scope", Description = () => $"Toggle using Azure DevOps OAuth scope for authentication [currently: {Program.config.Ado.UseOAuthScope}]",
                    Action = () =>
                    {
                        Program.config.Ado.UseOAuthScope = !Program.config.Ado.UseOAuthScope;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Program.ui.WriteLine($"Use Azure DevOps OAuth scope for authentication set to: {Program.config.Ado.UseOAuthScope}");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "AdoOAuthScope", Description = () => $"Set Azure DevOps OAuth scope [currently: {Program.config.Ado.AdoOauthScope}]",
                    Action = () =>
                    {
                        Program.ui.Write("Enter new Azure DevOps OAuth scope (GUID): ");
                        var scope = Program.ui.ReadLine();
                        if (!string.IsNullOrWhiteSpace(scope) && Guid.TryParse(scope, out var guid))
                        {
                            Program.config.Ado.AdoOauthScope = guid.ToString();
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Azure DevOps OAuth scope set to: {guid}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                new Command
                {
                    Name = "Insights", Description = () => "Configure Insights settings",
                    SubCommands = new List<Command>
                    {
                        new Command
                        {
                            Name = "fresh days", Description = () => $"Set the number of days that counts as being 'fresh' [currently: {Program.config.Ado.Insights.FreshDays}]",
                            Action = () =>
                            {
                                Program.ui.Write("Enter new number of fresh days: ");
                                if (int.TryParse(Program.ui.ReadLine(), out var freshDays))
                                {
                                    Program.config.Ado.Insights.FreshDays = freshDays;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Fresh days set to: {freshDays}");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                return Task.FromResult(Command.Result.Cancelled);
                            }
                        },
                        new Command
                        {
                            Name = "soon days", Description = () => $"Set the number of days that counts for 'soon' [currently: {Program.config.Ado.Insights.SoonDays}]",
                            Action = () =>
                            {
                                Program.ui.Write("Enter new number of soon days: ");
                                if (int.TryParse(Program.ui.ReadLine(), out var soonDays))
                                {
                                    Program.config.Ado.Insights.SoonDays = soonDays;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Soon days set to: {soonDays}");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                return Task.FromResult(Command.Result.Cancelled);
                            }
                        },
                        new Command
                        {
                            Name = "freshness weight", Description = () => $"Set how much to weigh freshness [currently: {Program.config.Ado.Insights.W_Fresh}]",
                            Action = () =>
                            {
                                Program.ui.Write("Enter new weight for fresh insights: ");
                                if (int.TryParse(Program.ui.ReadLine(), out var weight))
                                {
                                    Program.config.Ado.Insights.W_Fresh = weight;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Weight for fresh insights set to: {weight}");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                return Task.FromResult(Command.Result.Cancelled);
                            }
                        },
                        new Command
                        {
                            Name = "recent change weight", Description = () => $"Set how much to weigh recent changes [currently: {Program.config.Ado.Insights.W_RecentChange}]",
                            Action = () =>
                            {
                                Program.ui.Write("Enter new weight for recent changes: ");
                                if (int.TryParse(Program.ui.ReadLine(), out var weight))
                                {
                                    Program.config.Ado.Insights.W_RecentChange = weight;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Weight for recent changes set to: {weight}");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                return Task.FromResult(Command.Result.Cancelled);
                            }
                        },
                        new Command
                        {
                            Name ="unassigned weight", Description = () => $"Set how much to weigh unassigned changes [currently: {Program.config.Ado.Insights.W_Unassigned}]",
                            Action = () =>
                            {
                                Program.ui.Write("Enter new weight for unassigned changes: ");
                                if (int.TryParse(Program.ui.ReadLine(), out var weight))
                                {
                                    Program.config.Ado.Insights.W_Unassigned = weight;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Weight for unassigned changes set to: {weight}");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                return Task.FromResult(Command.Result.Cancelled);
                            }
                        },
                        new Command
                        {
                            Name ="high priority weight", Description = () => $"Set how much to weigh high priority (P1/P2) items [currently: {Program.config.Ado.Insights.W_PriorityHigh}]",
                            Action = () =>
                            {
                                Program.ui.Write("Enter new weight for high priority changes: ");
                                if (int.TryParse(Program.ui.ReadLine(), out var weight))
                                {
                                    Program.config.Ado.Insights.W_PriorityHigh = weight;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Weight for high priority changes set to: {weight}");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                return Task.FromResult(Command.Result.Cancelled);
                            }
                        },
                        new Command
                        {
                            Name ="critical tag weight", Description = () => $"Set how much to weigh critical tags [currently: {Program.config.Ado.Insights.W_CriticalTag}]",
                            Action = () =>
                            {
                                Program.ui.Write("Enter new weight for critical tags: ");
                                if (int.TryParse(Program.ui.ReadLine(), out var weight))
                                {
                                    Program.config.Ado.Insights.W_CriticalTag = weight;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Weight for critical tags set to: {weight}");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                return Task.FromResult(Command.Result.Cancelled);
                            }
                        },
                        new Command
                        {
                            Name = "Due soon weight", Description = () => $"Set how much to weigh items due soon [currently: {Program.config.Ado.Insights.W_DueSoon}]",
                            Action = () =>
                            {
                                Program.ui.Write("Enter new weight for items due soon: ");
                                if (int.TryParse(Program.ui.ReadLine(), out var weight))
                                {
                                    Program.config.Ado.Insights.W_DueSoon = weight;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Weight for items due soon set to: {weight}");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                return Task.FromResult(Command.Result.Cancelled);
                            }
                        },
                        // CRUD commands for Critical Tags
                        new Command
                        {
                            Name = "Critical Tags", Description = () => "add/remove/edit 'critical tags'",
                            SubCommands = new List<Command>
                            {
                                new Command
                                {
                                    Name = "list", Description = () => "List current critical tags",
                                    Action = () =>
                                    {
                                        Program.ui.WriteLine("Current critical tags:");
                                        foreach (var tag in Program.config.Ado.Insights.CriticalTags)
                                        {
                                            Program.ui.WriteLine($" - {tag}");
                                        }
                                        return Task.FromResult(Command.Result.Success);
                                    }
                                },
                                new Command
                                {
                                    Name = "add", Description = () => "Add a new critical tag",
                                    Action = () =>
                                    {
                                        while (true)
                                        {
                                            Program.ui.Write("Enter new critical tag: ");
                                            var tag = Program.ui.ReadLine();
                                            if (!string.IsNullOrWhiteSpace(tag))
                                            {
                                                Program.config.Ado.Insights.CriticalTags.Add(tag);
                                                Config.Save(Program.config, Program.ConfigFilePath);
                                                Program.ui.WriteLine($"Critical tag added: {tag}");
                                                Program.ui.WriteLine("Add another? (y/n)");
                                                var response = Program.ui.ReadLine();
                                                response = response?.Trim().ToLower();
                                                if (string.IsNullOrWhiteSpace(response) || response.StartsWith("y"))
                                                {
                                                    continue;
                                                }

                                                return Task.FromResult(Command.Result.Success);
                                            }
                                            return Task.FromResult(Command.Result.Cancelled);
                                        }
                                    }
                                },
                                new Command
                                {
                                    Name = "remove", Description = () => "Remove a critical tag",
                                    Action = () =>
                                    {
                                        while (true)
                                        {
                                            var choices = Program.config.Ado.Insights.CriticalTags.ToList();
                                            var header = "Select a critical tag to remove:\n" + new string('─', Math.Max(60, Program.ui.Width - 1));
                                            var selected = Program.ui.RenderMenu(header, choices);

                                            if (string.IsNullOrWhiteSpace(selected))
                                            {
                                                return Task.FromResult(Command.Result.Cancelled);
                                            }

                                            if (Program.config.Ado.Insights.CriticalTags.Remove(selected))
                                            {
                                                Config.Save(Program.config, Program.ConfigFilePath);
                                                Program.ui.WriteLine($"Critical tag removed: {selected}");
                                                Program.ui.WriteLine("Remove another? (y/n)");
                                                var response = Program.ui.ReadLine();
                                                response = response?.Trim().ToLower();
                                                if (string.IsNullOrWhiteSpace(response) || response.StartsWith("y"))
                                                {
                                                    continue;
                                                }
                                                return Task.FromResult(Command.Result.Success);
                                            }
                                        }
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