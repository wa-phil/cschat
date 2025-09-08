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
                        Console.WriteLine($"authMode      : {g.authMode}");
                        Console.WriteLine($"ClientId      : {g.ClientId.ToString()}");
                        Console.WriteLine($"DefaultScopes : {string.Join(' ', g.DefaultScopes)}");
                        Console.WriteLine($"Retries       : {g.MaxRetries} (base {g.BaseDelayMs}ms, jitter {g.MaxJitterMs}ms)");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name = "set authmode",
                    Description = () => $"Select authMode [currently: {Program.config.GraphSettings.authMode}]",
                    Action = () => {
                        var opts = Enum.GetNames(typeof(AuthMode)).ToList();
                        var cur = Math.Max(opts.IndexOf(Program.config.GraphSettings.authMode.ToString()), 0);
                        var sel = User.RenderMenu("Select auth mode:", opts, cur);
                        if (!string.IsNullOrWhiteSpace(sel)) Program.config.GraphSettings.authMode = Enum.Parse<AuthMode>(sel, true);
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name = "set clientid",
                    Description = () => $"Set ClientId [currently: {Program.config.GraphSettings.ClientId}]",
                    Action = () => {
                        Console.Write("ClientId (GUID or blank to clear): ");
                        var v = User.ReadLineWithHistory() ?? "";
                        if (Guid.TryParse(v, out var g)) Program.config.GraphSettings.ClientId = g;
                        else if (string.IsNullOrWhiteSpace(v)) Program.config.GraphSettings.ClientId = Guid.Empty;
                        else Console.WriteLine("Invalid GUID format.");
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
                            Console.WriteLine($"Mode: {Program.config.GraphSettings.authMode}");
                            Console.WriteLine(ex.Message);
                            return Command.Result.Failed;
                        }
                    }
                },
                new Command {
                    Name = "diagnose consent",
                    Description = () => "Show delegated grants for CSChat and report missing scopes",
                    Action = async () =>
                    {
                        await Task.Yield();

                        var clientId = Program.config.GraphSettings.ClientId;
                        if (Guid.Empty == clientId)
                        {
                            Console.WriteLine("ClientId is empty. Set it first (graph set client) and sign-in once.");
                            return Command.Result.Failed;
                        }

                        // Scopes your app actually needs:
                        // - whatever the config says (DefaultScopes)
                        // - plus Mail.* used by the Mail subsystem (read/send/move)
                        var required = new HashSet<string>(
                            (Program.config.GraphSettings.DefaultScopes ?? new List<string>()).Select(s => s.Trim()),
                            StringComparer.OrdinalIgnoreCase);

                        required.Add("Mail.Read");
                        required.Add("Mail.Send");
                        required.Add("Mail.ReadWrite"); // used by move/triage

                        // Resolve service principal objectId for your app registration
                        var spId = GraphCore.TryRunAz($@"az ad sp show --id {clientId} --query id -o tsv");
                        if (string.IsNullOrWhiteSpace(spId))
                        {
                            Console.WriteLine("Could not resolve service principal for the app. Do you have directory read access?");
                            return Command.Result.Failed;
                        }

                        // Pull oauth2PermissionGrants for this SP
                        // NOTE: backtick before $ is needed in PowerShell to avoid interpolation.
                        var url = $"https://graph.microsoft.com/v1.0/oauth2PermissionGrants?$filter=clientId eq '{spId}'";
                        var raw = GraphCore.TryRunAz($@"az rest --method get --url ""{url}""");
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            Console.WriteLine("Failed to query oauth2PermissionGrants via az rest (see logs).");
                            return Command.Result.Failed;
                        }

                        var root = raw.FromJson<Dictionary<string, object>>() ?? new();
                        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (root.TryGetValue("value", out var arr) && arr is List<object> list)
                        {
                            foreach (var item in list.OfType<Dictionary<string, object>>())
                            {
                                if (item.TryGetValue("scope", out var scopeObj) && scopeObj is string scopeStr)
                                {
                                    var scopes = scopeStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var s in scopes) granted.Add(s);
                                }
                            }
                        }

                        // Compare
                        var missing = required.Where(s => !granted.Contains(s)).OrderBy(s => s).ToList();
                        Console.WriteLine($"App display (clientId): {clientId}");
                        Console.WriteLine($"Service principal id  : {spId}");
                        Console.WriteLine($"Configured defaults   : {string.Join(' ', Program.config.GraphSettings.DefaultScopes ?? new List<string>())}");
                        Console.WriteLine($"Grants found          : {(granted.Count==0 ? "(none)" : string.Join(' ', granted.OrderBy(s => s)))}");
                        Console.WriteLine($"Missing for CSChat    : {(missing.Count==0 ? "(none)" : string.Join(' ', missing))}");

                        if (missing.Count > 0)
                        {
                            Console.WriteLine();
                            Console.WriteLine("Ask a tenant admin to open either of these and click **Grant admin consent**:");
                            Console.WriteLine("- App registrations → CSChat → API permissions");
                            Console.WriteLine("- Enterprise applications → CSChat → Permissions");
                        }

                        return Command.Result.Success;
                    }
                }
            }
        };
    }
}
