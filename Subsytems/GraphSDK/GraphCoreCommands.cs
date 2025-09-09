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

                        // Build scopes to request (fall back to User.Read if none)
                        var reqScopes = (Program.config.GraphSettings.DefaultScopes?.Count > 0
                                        ? Program.config.GraphSettings.DefaultScopes
                                        : new List<string> { "User.Read" })
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToArray();

                        Console.WriteLine();
                        Console.WriteLine("=== Current token (for your selected auth mode) ===");

                        // Choose scopes for token acquisition (mirror GraphCore behavior)
                        string[] tokenScopes =
                            Program.config.GraphSettings.authMode == AuthMode.azcli
                                ? new[] { "https://graph.microsoft.com/.default" }
                                : (reqScopes.Length > 0 ? reqScopes : new[] { "User.Read" });

                        // Acquire token using the same credential selection as GraphCore
                        var cred = GraphCore.CreateCredential();
                        Azure.Core.AccessToken at;
                        try
                        {
                            at = cred.GetToken(new Azure.Core.TokenRequestContext(tokenScopes), CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Could not acquire a token with the current settings.");
                            Console.WriteLine(ex.Message);
                            return Command.Result.Failed;
                        }

                        // Decode JWT (base64url → JSON) and parse with TinyJson
                        static byte[] B64Url(string s)
                        {
                            s = s.Replace('-', '+').Replace('_', '/');
                            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
                            return System.Convert.FromBase64String(s);
                        }

                        var parts = at.Token.Split('.');
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Token did not look like a JWT.");
                            Console.WriteLine(at.Token.Substring(0, Math.Min(40, at.Token.Length)) + "...");
                            return Command.Result.Success;
                        }

                        var payloadJson = System.Text.Encoding.UTF8.GetString(B64Url(parts[1]));
                        var payload = payloadJson.FromJson<Dictionary<string, object>>() ?? new();

                        string GetStr(string key)
                            => payload.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

                        static DateTimeOffset FromUnix(object? o)
                        {
                            if (o == null) return default;
                            if (long.TryParse(o.ToString(), out var i))
                                return DateTimeOffset.FromUnixTimeSeconds(i);
                            return default;
                        }

                        var aud   = GetStr("aud");
                        var tid   = GetStr("tid");
                        var appId = GetStr("azp");               // sometimes 'azp' (authorized party) holds client id
                        if (string.IsNullOrEmpty(appId)) appId = GetStr("appid"); // or 'appid' in some tokens
                        var upn   = GetStr("upn");
                        var pref  = GetStr("preferred_username");
                        var name  = GetStr("name");
                        var scp   = GetStr("scp");               // space-separated delegated scopes
                        var roles = payload.TryGetValue("roles", out var rObj) && rObj is List<object> rl
                                    ? string.Join(' ', rl.Select(x => x.ToString()))
                                    : "";
                        var exp   = FromUnix(payload.TryGetValue("exp", out var expObj) ? expObj : null);

                        Console.WriteLine($"Auth mode           : {Program.config.GraphSettings.authMode}");
                        Console.WriteLine($"Requested scopes    : {string.Join(' ', reqScopes)}");
                        Console.WriteLine($"Token audience (aud): {aud}");
                        Console.WriteLine($"Tenant (tid)        : {tid}");
                        Console.WriteLine($"Client (appid/azp)  : {appId}");
                        Console.WriteLine($"User                : {(!string.IsNullOrEmpty(pref) ? pref : upn)} ({name})");
                        Console.WriteLine($"Delegated scopes    : {(string.IsNullOrWhiteSpace(scp) ? "(none)" : scp)}");
                        if (!string.IsNullOrWhiteSpace(roles))
                            Console.WriteLine($"App roles           : {roles}");
                        Console.WriteLine($"Expires             : {exp.LocalDateTime} (local)");

                        if (Program.config.GraphSettings.authMode == AuthMode.azcli)
                        {
                            Console.WriteLine();
                            Console.WriteLine("Note: This token is for the **Azure CLI** app, not CSChat. Scopes reflect what your tenant has granted to the CLI app.");
                        }

                        Console.WriteLine();
                        Console.WriteLine("If 'Delegated scopes' does not include Mail.* when using prompt/devicecode with your CSChat ClientId,");
                        Console.WriteLine("have a tenant admin grant those scopes to CSChat (Enterprise apps → CSChat → Permissions → Grant admin consent).");

                        return Command.Result.Success;
                    }
                }
            }
        };
    }
}
