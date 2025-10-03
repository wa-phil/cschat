using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class CommandManager : Command
{
    public static Command CreateADOConfigCommands()
    {
        return new Command
        {
            Name = "Azure Dev Ops (ADO)",
            Description = () => "configuration settings",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "Config",
                    Description = () => "Edit organization, project, repository, and authentication",
                    Action = async () =>
                    {
                        // Build a typed form on a cloned ADO config object
                        var form = UiForm.Create("Azure DevOps Config", Program.config.Ado);

                        form.AddString<dynamic>("Organization",
                            m => (string)(m.Organization ?? ""),
                            (m, v) => m.Organization = v, "Organization")
                            .WithHelp("Your ADO org name (e.g., 'myorg').")
                            .MakeOptional();

                        form.AddString<dynamic>("Project",
                            m => (string)(m.ProjectName ?? ""),
                            (m, v) => m.ProjectName = v, "ProjectName")
                            .WithHelp("Default ADO project.")
                            .MakeOptional();

                        form.AddString<dynamic>("Repository",
                            m => (string)(m.RepositoryName ?? ""),
                            (m, v) => m.RepositoryName = v, "RepositoryName")
                            .WithHelp("Default ADO repo.")
                            .MakeOptional();

                        form.AddBool<dynamic>("Use OAuth Scope",
                            m => (bool)(m.UseOAuthScope ?? false),
                            (m, v) => m.UseOAuthScope = v, "UseOAuthScope")
                            .WithHelp("Toggle using the ADO OAuth scope for authentication.");

                        // AdoOauthScope is stored as string; validate as GUID text with a regex.
                        form.AddString<dynamic>("OAuth Scope (GUID)",
                            m => (string)(m.AdoOauthScope ?? ""),
                            (m, v) => m.AdoOauthScope = (v ?? "").Trim(), "AdoOauthScope")
                            .WithRegex(@"^\s*$|^[A-Fa-f0-9]{8}\-[A-Fa-f0-9]{4}\-[A-Fa-f0-9]{4}\-[A-Fa-f0-9]{4}\-[A-Fa-f0-9]{12}\s*$",
                                       "Must be blank or a GUID like 00000000-0000-0000-0000-000000000000.")
                            .WithHelp("Leave blank to unset.");

                        if (!await Program.ui.ShowFormAsync(form))
                            return Command.Result.Cancelled;

                        // Copy edited clone back into the live config object
                        Program.config.Ado = (AdoConfig)form.Model!;

                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "Insights",
                    Description = () => "Edit Insights scoring + critical tags (form)",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Azure DevOps – Insights", Program.config.Ado.Insights);

                        form.AddInt<dynamic>("Fresh Days",
                            m => (int)(m.FreshDays),
                            (m, v) => m.FreshDays = v, "FreshDays")
                            .WithHelp("How many days counts as 'fresh' items.")
                            .IntBounds(0, 365);

                        form.AddInt<dynamic>("Soon Days",
                            m => (int)(m.SoonDays),
                            (m, v) => m.SoonDays = v, "SoonDays")
                            .WithHelp("How many days ahead counts as 'due soon'.")
                            .IntBounds(0, 365);

                        form.AddFloat<dynamic>("Weight: Fresh",
                            m => (float)(m.W_Fresh),
                            (m, v) => m.W_Fresh = v, "W_Fresh")
                            .WithHelp("Score weight for freshness.")
                            .IntBounds(0, 10);

                        form.AddFloat<dynamic>("Weight: Recent Change",
                            m => (float)(m.W_RecentChange),
                            (m, v) => m.W_RecentChange = v, "W_RecentChange")
                            .WithHelp("Score weight for items changed recently.")
                            .IntBounds(0, 10);

                        form.AddFloat<dynamic>("Weight: Unassigned",
                            m => (float)(m.W_Unassigned),
                            (m, v) => m.W_Unassigned = v, "W_Unassigned")
                            .WithHelp("Score weight for unassigned items.")
                            .IntBounds(0, 10);

                        form.AddFloat<dynamic>("Weight: Priority High (P1/P2)",
                            m => (float)(m.W_PriorityHigh),
                            (m, v) => m.W_PriorityHigh = v, "W_PriorityHigh")
                            .WithHelp("Score weight for high-priority items.")
                            .IntBounds(0, 10);

                        form.AddFloat<dynamic>("Weight: Critical Tag",
                            m => (float)(m.W_CriticalTag),
                            (m, v) => m.W_CriticalTag = v, "W_CriticalTag")
                            .WithHelp("Score weight for presence of critical tags.")
                            .IntBounds(0, 10);

                        form.AddFloat<dynamic>("Weight: Due Soon",
                            m => (float)(m.W_DueSoon),
                            (m, v) => m.W_DueSoon = v, "W_DueSoon")
                            .WithHelp("Score weight for approaching due dates.")
                            .IntBounds(0, 10);

                        // Inline editor for CriticalTags (List<string>) using the Array field
                        form.AddList<dynamic, string>("Critical Tags",
                            m => (IList<string>)(m.CriticalTags ?? new List<string>()),
                            (m, v) => m.CriticalTags = v?.ToList() ?? new List<string>(),
                            "CriticalTags")
                            .WithHelp("Tags treated as 'critical' (add/remove/edit; JSON-backed).")
                            .MakeOptional();

                        if (!await Program.ui.ShowFormAsync(form))
                            return Command.Result.Cancelled;

                        Program.config.Ado.Insights = (AdoInsightsConfig)form.Model!;

                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                }
            }
        };
    }
}