using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager
{
    public static Command CreateGraphCoreCommands()
    {
        return new Command
        {
            Name = "graph",
            Description = () => "Microsoft Graph configuration",
            SubCommands = new List<Command>
            {
                new Command {
                    Name = "show",
                    Description = () => "Show Graph settings",
                    Action = () => {
                        var g = Program.config.GraphSettings;
                        Console.WriteLine($"AuthMode      : {g.AuthMode}");
                        Console.WriteLine($"DefaultScopes : {string.Join(' ', g.DefaultScopes)}");
                        Console.WriteLine($"Retries       : {g.MaxRetries} (base {g.BaseDelayMs}ms, jitter {g.MaxJitterMs}ms)");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name = "set authmode",
                    Description = () => $"Select AuthMode [currently: {Program.config.GraphSettings.AuthMode}]",
                    Action = () => {
                        var opts = new List<string>{ "devicecode", "prompt", "azcli", "managedIdentity" };
                        var cur = opts.IndexOf(Program.config.GraphSettings.AuthMode.ToString().ToLowerInvariant() ?? "devicecode");
                        var sel = User.RenderMenu("Select auth mode:", opts, cur);
                        if (!string.IsNullOrWhiteSpace(sel)) Program.config.GraphSettings.AuthMode = Enum.Parse<AuthMode>(sel, true);
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name = "set scopes",
                    Description = () => $"Set DefaultScopes [currently: {string.Join(' ', Program.config.GraphSettings.DefaultScopes)}]",
                    Action = () => {
                        Console.Write("Space-separated scopes (e.g. 'User.Read Mail.Read Mail.Send'): ");
                        var v = User.ReadLineWithHistory() ?? "";
                        Program.config.GraphSettings.DefaultScopes = v.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name = "test",
                    Description = () => "Acquire token and call /me",
                    Action = async () => {
                        await Task.Yield();
                        try
                        {
                            var client = new GraphCore().GetClient(); // respects AuthMode + our new fallback
                            var me = await client.Me.GetAsync();
                            Console.WriteLine($"Hello, {me?.DisplayName} ({me?.UserPrincipalName})");
                            return Command.Result.Success;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Graph auth failed.");
                            Console.WriteLine($"Mode: {Program.config.GraphSettings.AuthMode}");
                            Console.WriteLine(ex.Message);
                            return Command.Result.Failed;
                        }
                    }
                }
            }
        };
    }
}
