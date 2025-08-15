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
                        Console.Write("Enter new Azure DevOps organization: ");
                        var org = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(org))
                        {
                            Program.config.Ado.Organization = org;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Console.WriteLine($"Azure DevOps organization set to: {org}");
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
                        Console.Write("Enter new Azure DevOps project: ");
                        var project = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(project))
                        {
                            Program.config.Ado.ProjectName = project;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Console.WriteLine($"Azure DevOps project set to: {project}");
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
                        Console.Write("Enter new Azure DevOps repository: ");
                        var repo = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(repo))
                        {
                            Program.config.Ado.RepositoryName = repo;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Console.WriteLine($"Azure DevOps repository set to: {repo}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                new Command
                {
                    Name= "AdoOauthScope", Description = () => $"Set Azure DevOps OAuth scope [currently: {Program.config.Ado.AdoOauthScope}]",
                    Action = () =>
                    {
                        Console.Write("Enter new Azure DevOps OAuth scope (GUID): ");
                        var scope = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(scope) && Guid.TryParse(scope, out var guid))
                        {
                            Program.config.Ado.AdoOauthScope = guid.ToString();
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Console.WriteLine($"Azure DevOps OAuth scope set to: {guid}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                }
            }
        };
    }
}