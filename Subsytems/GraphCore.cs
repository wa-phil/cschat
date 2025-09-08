using System.Net;
using Azure.Core;
using System.Linq;
using Azure.Identity;
using Microsoft.Graph;
using System.Diagnostics;
using Kusto.Data.Common;

public sealed class GraphCore
{
    // Keep a basic cache of clients by joined scopes to avoid re-instantiating
    private readonly Dictionary<string, GraphServiceClient> _clientCache = new();

    public GraphCore() { }

    public GraphServiceClient GetClient(params string[] scopes) => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.Input, string.Join(',', scopes));
        var effectiveScopes = (scopes is { Length: > 0 } ?
            scopes :
            Program.config.GraphSettings.DefaultScopes?.ToArray() ?? Array.Empty<string>());
        ctx.Append(Log.Data.Scopes, string.Join(',', effectiveScopes));
        var cacheKey = $"{Program.config.GraphSettings.authMode}|{string.Join(' ', effectiveScopes)}";
        if (_clientCache.TryGetValue(cacheKey, out var cached))
        {
            ctx.Append(Log.Data.Message, "Using cached Graph client");
            ctx.Succeeded();
            return cached;
        }

        var credential = CreateCredential();
        var client = CreateGraphClient(credential, effectiveScopes);
        _clientCache[cacheKey] = client;
        ctx.Succeeded();
        return client;
    });

    internal static string? TryRunAz(string args) => Log.Method(ctx =>
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(8000);
            var result = (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)) ? output : null;
            ctx.Succeeded(!string.IsNullOrEmpty(result));
            return result;
        }
        catch (Exception ex)
        {
            ctx.Failed($"Error occurred while running Az", ex);
            return null;
        }
    });

    internal static TokenCredential CreateCredential() => Log.Method<TokenCredential>(ctx =>
    {
        var options = new TokenCredentialOptions
        {
            AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
        };

        var tenantId = TryRunAz(@"az account show --query tenantId -o tsv");
        var clientGuid = Program.config.GraphSettings.ClientId;
        bool hasClientId = clientGuid != Guid.Empty;

        // Choose credential based on the enum value
        switch (Program.config.GraphSettings.authMode)
        {
            case AuthMode.azcli:
            {
                var cred = new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId});
                ctx.Succeeded();
                return cred;
            }

            case AuthMode.managedIdentity:
            {
                var msi = new ManagedIdentityCredential();
                ctx.Succeeded();
                return msi;
            }

            case AuthMode.prompt:
            {
                if (!hasClientId) {
                    // fall back to CLI if no app is configured
                    return new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId });
                }
                return new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = clientGuid.ToString(),
                    AuthorityHost = options.AuthorityHost,
                    RedirectUri = new Uri("http://localhost")
                });
            }

            case AuthMode.devicecode:
            default:
            {
                if (!hasClientId) {
                    return new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId });
                }
                return new DeviceCodeCredential(new DeviceCodeCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = clientGuid.ToString(),
                    AuthorityHost = options.AuthorityHost,
                    DeviceCodeCallback = (info, ct) => { Console.WriteLine(info.Message); return Task.CompletedTask; }
                });
            }
        }
    });

    private GraphServiceClient CreateGraphClient(TokenCredential credential, string[] scopes) => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.Input, string.Join(',', scopes));
        // If we are using Azure CLI, prefer the resource-based scope which CLI handles best
        var useDefault = credential is AzureCliCredential;
        ctx.Append(Log.Data.Message, useDefault ? "Using .default scope for Azure CLI" : "Using provided scopes");

        var actualScopes = useDefault
            ? new[] { "https://graph.microsoft.com/.default" }
            : (scopes.Length == 0 ? new[] { "https://graph.microsoft.com/.default" } : scopes);

        ctx.Append(Log.Data.Scopes, string.Join(',', actualScopes));

        var result = new GraphServiceClient(credential, actualScopes);
        ctx.Succeeded();
        return result;
    });

    private static string ResolveMaybeEnv(string value) => Log.Method(ctx =>
    {
        // Allow "env:NAME" indirection in config
        if (value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var name = value.Substring(4);
            var fromEnv = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(fromEnv))
                throw new InvalidOperationException($"Environment variable '{name}' not set.");
            ctx.Succeeded();
            return fromEnv;
        }
        ctx.Succeeded();
        return value;
    });
}