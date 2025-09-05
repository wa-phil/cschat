using System.Net;
using Azure.Core;
using System.Linq;
using Azure.Identity;
using Microsoft.Graph;
using System.Diagnostics;

public sealed class GraphCore
{
    private readonly GraphSettings _settings;

    // Keep a basic cache of clients by joined scopes to avoid re-instantiating
    private readonly Dictionary<string, GraphServiceClient> _clientCache = new();

    public GraphCore()
    {
        _settings = Program.config.GraphSettings ?? throw new ArgumentNullException(nameof(Program.config.GraphSettings));
    }

    public GraphServiceClient GetClient(params string[] scopes) => Log.Method(ctx =>
    {
        var effectiveScopes = (scopes is { Length: > 0 } ?
            scopes :
            _settings.DefaultScopes?.ToArray() ?? Array.Empty<string>());

        var cacheKey = $"{_settings.AuthMode}|{string.Join(' ', effectiveScopes)}";
        if (_clientCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var credential = CreateCredential();
        var client = CreateGraphClient(credential, effectiveScopes);
        _clientCache[cacheKey] = client;
        ctx.Succeeded();
        return client;
    });

    private static string? TryRunAz(string args) => Log.Method(ctx =>
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

    private TokenCredential CreateCredential() => Log.Method<TokenCredential>(ctx =>
    {
        var options = new TokenCredentialOptions
        {
            AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
        };

        var TenantId = TryRunAz(@"az account show --query tenantId -o tsv");
        var ClientId = TryRunAz(@"az ad sp show --id https://graph.microsoft.com --query appId -o tsv");

        // Choose credential based on the enum value
        switch (_settings.AuthMode)
        {
            case AuthMode.azcli:
            {
                var cred = new AzureCliCredential(new AzureCliCredentialOptions { TenantId = TenantId });
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
                if (string.IsNullOrWhiteSpace(ClientId))
                {
                    // no client id, use CLI token instead
                    var result = new AzureCliCredential(new AzureCliCredentialOptions { TenantId = TenantId });
                    ctx.Succeeded();
                    return result;
                }

                var interactive = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
                {
                    TenantId = TenantId,
                    ClientId = ClientId,
                    AuthorityHost = options.AuthorityHost
                });
                ctx.Succeeded();
                return interactive;
            }

            case AuthMode.devicecode:
            default:
            {
                if (string.IsNullOrWhiteSpace(ClientId))
                {
                    // no client id, use CLI token instead
                    var result = new AzureCliCredential(new AzureCliCredentialOptions { TenantId = TenantId });
                    ctx.Succeeded();
                    return result;
                }

                var deviceOptions = new DeviceCodeCredentialOptions
                {
                    TenantId = TenantId,
                    ClientId = ClientId,
                    AuthorityHost = options.AuthorityHost,
                    DeviceCodeCallback = (info, ct) => { Console.WriteLine(info.Message); return Task.CompletedTask; }
                };
                var device = new DeviceCodeCredential(deviceOptions);
                ctx.Succeeded();
                return device;
            }
        }
    });

    private GraphServiceClient CreateGraphClient(TokenCredential credential, string[] scopes) => Log.Method(ctx =>
    {
        // If we are using Azure CLI, prefer the resource-based scope which CLI handles best
        var useDefault = credential is AzureCliCredential;

        var actualScopes = useDefault
            ? new[] { "https://graph.microsoft.com/.default" }
            : (scopes.Length == 0 ? new[] { "https://graph.microsoft.com/.default" } : scopes);

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

/// <summary>Simple 429/503/504 handler honoring Retry-After + backoff.</summary>
public sealed class GraphRetryHandler : DelegatingHandler
{
    private readonly int _maxRetries;
    private readonly int _baseDelayMs;
    private readonly int _maxJitterMs;

    public GraphRetryHandler(int maxRetries, int baseDelayMs, int maxJitterMs)
        => (_maxRetries, _baseDelayMs, _maxJitterMs) = (maxRetries, baseDelayMs, maxJitterMs);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) => await Log.MethodAsync(async ctx =>
    {
        for (var attempt = 0; ; attempt++)
        {
            var res = await base.SendAsync(req, ct);
            if (IsTransient(res) && attempt < _maxRetries)
            {
                var delay = ComputeDelay(res, attempt);
                await Task.Delay(delay, ct);
                continue;
            }
            ctx.Succeeded();
            return res;
        }
    });

    private static bool IsTransient(HttpResponseMessage r)
        => r.StatusCode is (HttpStatusCode)429 or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private TimeSpan ComputeDelay(HttpResponseMessage r, int attempt)
    {
        if (r.Headers.TryGetValues("Retry-After", out var vals) && int.TryParse(vals.FirstOrDefault(), out var s))
            return TimeSpan.FromSeconds(s);
        var jitter = Random.Shared.Next(0, _maxJitterMs + 1);
        var expo = _baseDelayMs * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(expo + jitter);
    }
}
