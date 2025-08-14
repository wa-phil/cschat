using Azure;
using System;
using System.Linq;
using System.Text;
using Azure.Identity;
using System.Net.Http;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Diagnostics;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;


// TODO: Move to Config.cs
public class AdoConfig
{
    public string Organization { get; set; } = "yourorganization";
    public string ProjectName { get; set; } = "YourProjectName";
    public string RepositoryName { get; set; } = "YourRepositoryName";
    public string AdoOauthScope { get; set; } = "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF";
}

[IsConfigurable("Ado")]
public class AdoClient : ISubsystem
{
    public string GetOrganizationUrl() => $"https://dev.azure.com/{Program.config.Ado.Organization}";
    public string GetRepositoryUrl() => $"https://dev.azure.com/{Program.config.Ado.Organization}/{Program.config.Ado.ProjectName}/_git/{Program.config.Ado.RepositoryName}";

    private HttpClient _http = null!;
    private VssConnection _connection = null!;
    private WorkItemTrackingHttpClient _witClient = null!;
    private GitHttpClient _gitClient = null!;
    private bool _connected = false;

    public Type ConfigType => typeof(AdoConfig);
    public bool IsAvailable { get; } = true;
    public bool IsEnabled
    {
        get => _connected;
        set
        {
            if (value && !_connected)
            {
                Connect();
                Register();
                _connected = true;
            }
            else if (!value && _connected)
            {
                Unregister();
                _connection?.Dispose();
                _witClient?.Dispose();
                _gitClient?.Dispose();
                _connected = false;
            }
        }
    }

    // TODO: add ADO related tools here as these are not yet implemented/defined.
    private Dictionary<string, Func<ITool>> _tools = new Dictionary<string, Func<ITool>>(StringComparer.OrdinalIgnoreCase)
    {
        // { "get_work_item", () => WorkItemTool.Get },
        // { "query_work_items", () => QueryItemsTool.Get },
    };

    public void Register()
    {
        _tools.ForEach(tool => ToolRegistry.RegisterTool(tool.Key, tool.Value()));
        Program.commandManager.SubCommands.Add(ADOCommands.Commands());
    }

    public void Unregister()
    {
        _tools.ForEach(tool => ToolRegistry.UnregisterTool(tool.Key));
        Program.commandManager.SubCommands.RemoveAll(cmd => cmd.Name.Equals("ADO", StringComparison.OrdinalIgnoreCase));
    }

    void Connect() => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.Message, "Attempting to acquire Azure DevOps PAT...");
        ctx.Append(Log.Data.Host, GetOrganizationUrl());
        var pat = AdoCredentialHelper.GetPersonalAccessToken();
        if (string.IsNullOrWhiteSpace(pat))
        {
            ctx.Failed("Failed to acquire a valid Azure DevOps PAT.", Error.InvalidInput);
            return;
        }

        var creds = new VssBasicCredential(string.Empty, pat);
        _connection = new VssConnection(new Uri(GetOrganizationUrl()), creds);

        _witClient = _connection.GetClient<WorkItemTrackingHttpClient>();
        _gitClient = _connection.GetClient<GitHttpClient>();

        _http = new HttpClient { BaseAddress = new Uri(GetOrganizationUrl()), Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}")));

        ctx.Append(Log.Data.Message, "Azure DevOps clients initialized successfully.");
        ctx.Succeeded();
    });

    private static readonly string[] SummaryFields = new[]
    {
        "System.Id", "System.Title", "System.State", "Microsoft.VSTS.Common.Priority",
        "System.AssignedTo", "System.AreaPath", "System.IterationPath", "System.ChangedDate",
        "System.Description", "System.Tags", "System.History"
    };

    public async Task<WorkItemSummary> GetWorkItemSummaryById(int id) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Input, id);
        var workItem = await _witClient.GetWorkItemAsync(id, SummaryFields);
        ctx.Succeeded();
        return WorkItemSummary.FromWorkItem(workItem);
    });

    public async Task<List<WorkItemSummary>> GetWorkItemSummariesByQueryId(Guid queryId) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Input, queryId.ToString());
        var project = Program.config.Ado.ProjectName;
        var queryResult = await _witClient.QueryByIdAsync(queryId);
        if (queryResult.WorkItems == null || !queryResult.WorkItems.Any())
        {
            ctx.Append(Log.Data.Message, "No work items found for the given query ID.");
            ctx.Succeeded();
            return new List<WorkItemSummary>();
        }

        var all = new List<WorkItem>();
        var ids = queryResult.WorkItems.Select(w => w.Id).ToArray();
        const int page = 200;
        for (int i = 0; i < ids.Length; i += page)
        {
            var slice = ids.Skip(i).Take(page).ToArray();
            // Do NOT pass expand when fields are specified
            var batch = await _witClient.GetWorkItemsAsync(slice, SummaryFields);
            if (batch != null) all.AddRange(batch);
        }
        var summaries = all.ToSummaries();

        ctx.Append(Log.Data.Count, summaries.Count);
        ctx.Succeeded();
        return summaries;
    });

    private async Task<string> GetProjectIdByNameAsync(string projectName, CancellationToken ct = default)
    {
        var url = $"{GetOrganizationUrl()}/_apis/projects/{Uri.EscapeDataString(projectName)}?api-version=7.1";
        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        var root = json.FromJson<Dictionary<string, object>>() ?? new();
        if (root.TryGetValue("id", out var v) && v is string id && Guid.TryParse(id, out _)) return id;
        throw new InvalidOperationException($"Could not resolve project id for '{projectName}'.");
    }

    /// <summary>
    /// Get the full query hierarchy (My Queries and Shared Queries) for a project,
    /// then flatten to rows that are easy to render in a menu.
    /// </summary>
    public async Task<List<AdoQueryRow>> GetQueryRowsAsync(string? project = null)
        => await Log.MethodAsync(async ctx =>
    {
        project ??= Program.config.Ado.ProjectName;
        ctx.Append(Log.Data.Name, project);

        // Bounded parallelism: at most this many concurrent folder expansions.
        const int MAX_CONCURRENCY = 6;

        // Overall guard so we never hang forever (tune as you like).
        using var allCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = allCts.Token;

        var rows = new ConcurrentBag<AdoQueryRow>();
        var seenPaths = new ConcurrentDictionary<string, byte>(); // de-dupe by full path
        var sem = new SemaphoreSlim(MAX_CONCURRENCY);
        var tasks = new List<Task>();

        // Expand a single node (folder or root “My Queries” / “Shared Queries”).
        async Task ExpandAsync(string path, int depth)
        {
            await sem.WaitAsync(token);
            try
            {
                // Per-call timeout; keeps a slow branch from stalling the rest.
                using var callCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                callCts.CancelAfter(TimeSpan.FromSeconds(8));

                // SDK rule: depth must be 0..2. We use 2 to get the next hop of children.
                var branch = await _witClient.GetQueryAsync(
                    project, path, QueryExpand.All, depth: 2, cancellationToken: callCts.Token);

                if (branch?.Children == null) return;

                foreach (var child in branch.Children)
                {
                    var childPath = string.IsNullOrWhiteSpace(child.Path)
                        ? $"{path}/{child.Name}"
                        : child.Path;

                    // Record the node exactly once.
                    if (seenPaths.TryAdd(childPath, 0))
                    {
                        rows.Add(new AdoQueryRow(
                            child.Id,                      // Guid? (null for some folders)
                            child.Name ?? "",
                            childPath,
                            child.IsFolder == true,
                            depth + 1
                        ));

                        // If it’s a folder, queue it for further expansion.
                        if (child.IsFolder == true && depth < 50)
                        {
                            // Optional hard ceiling on traversal depth to prevent runaway
                            tasks.Add(ExpandAsync(childPath, depth + 1));
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* timed out or cancelled; skip this branch */ }
            catch (Exception ex)
            {
                // Permission or path issues; don’t fail the whole browse.
                ctx.Append(Log.Data.Message, $"Skip '{path}': {ex.GetType().Name}");
            }
            finally
            {
                sem.Release();
            }
        }

        // Seed the traversal with the two standard roots. Missing roots are harmless.
        tasks.Add(ExpandAsync("My Queries", 0));

        await Task.WhenAll(tasks);

        // Make a deterministic, readable order for the menu.
        var list = rows
            .OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ctx.Append(Log.Data.Count, list.Count);
        ctx.Succeeded();
        return list;
    });

    public async Task<HashSet<Guid>> TryGetFavoriteQueryIdsAsync(string? project = null)
    {
        var favs = await GetFavoriteQueriesAsync();
        if (!string.IsNullOrWhiteSpace(project))
        {
            favs = favs
                .Where(f => string.Equals(f.Project, project, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return favs.Select(f => f.Id).ToHashSet();
    }

    public async Task<List<AdoQueryRow>> GetQueryChildrenAsync(string project, string path, int depth)
    {
        var item = await _witClient.GetQueryAsync(project, path, QueryExpand.All, depth: 2);
        var list = new List<AdoQueryRow>();
        if (item?.Children == null) return list;

        foreach (var c in item.Children)
        {
            var p = string.IsNullOrWhiteSpace(c.Path) ? $"{path}/{c.Name}" : c.Path!;
            list.Add(new AdoQueryRow(c.Id, c.Name ?? "", p, c.IsFolder == true, depth + 1));
        }
        return list.OrderBy(x => x.IsFolder ? 0 : 1).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public sealed record QueryFavorite(Guid Id, string Name, string? Project, string? Url);

    // Replace your existing GetFavoriteQueriesAsync with this:
    public async Task<List<QueryFavorite>> GetFavoriteQueriesAsync(string? projectName = null, CancellationToken ct = default)
        => await Log.MethodAsync(async ctx =>
    {
        projectName ??= Program.config.Ado.ProjectName;
        ctx.Append(Log.Data.Name, projectName);

        // Get all favorites for the current identity (cheap)
        var url = $"{GetOrganizationUrl()}/_apis/favorite/favorites?includeExtendedDetails=true&api-version=7.1-preview.1";
        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync(ct);
        var root = json.FromJson<Dictionary<string, object>>() ?? new();
        var list = (root.TryGetValue("value", out var vv) && vv is List<object> lo) ? lo : new();

        var guidRe = new System.Text.RegularExpressions.Regex(
            @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        static Dictionary<string, object>? D(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var x) && x is Dictionary<string, object> dd ? dd : null;
        static string? S(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var x) ? x as string : null;

        bool IsQueryFavorite(Dictionary<string, object> it)
            => string.Equals(S(it, "artifactType"), "Microsoft.TeamFoundation.WorkItemTracking.WorkItemQuery", StringComparison.OrdinalIgnoreCase)
            || (S(it, "artifactId")?.Contains("/WorkItemTracking/Query/", StringComparison.OrdinalIgnoreCase) ?? false);

        bool BelongsToProject(Dictionary<string, object> it)
        {
            var scopeName = D(it, "artifactScope") is { } scope ? S(scope, "name") : null;
            var href = D(D(it, "_links") ?? new(), "page") is { } page ? S(page, "href") : null;

            // Accept if the scope name matches OR the page URL contains "/{projectName}/"
            if (!string.IsNullOrWhiteSpace(scopeName) &&
                string.Equals(scopeName, projectName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(href) &&
                href!.IndexOf($"/{projectName}/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        var results = new List<QueryFavorite>();
        foreach (var o in list)
        {
            if (o is not Dictionary<string, object> it) continue;
            if (!IsQueryFavorite(it)) continue;
            if (!BelongsToProject(it)) continue;

            var artifactId = S(it, "artifactId");
            var href = D(D(it, "_links") ?? new(), "page") is { } page ? S(page, "href") : null;

            var m = !string.IsNullOrWhiteSpace(artifactId) ? guidRe.Match(artifactId!) : System.Text.RegularExpressions.Match.Empty;
            if (!m.Success && !string.IsNullOrWhiteSpace(href)) m = guidRe.Match(href!);
            if (!m.Success) continue;

            var qid = Guid.Parse(m.Value);
            var name = S(it, "artifactName") ?? "(unnamed query)";
            var proj = D(it, "artifactScope") is { } s ? S(s, "name") : projectName;
            results.Add(new QueryFavorite(qid, name, proj, href));
        }

        results = results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        ctx.Append(Log.Data.Count, results.Count);
        ctx.Succeeded();
        return results;
    });

}

public static class AdoCredentialHelper
{
    public static string? GetPersonalAccessToken() => Log.Method(ctx =>
    {
        // 1. Try environment variable
        var envPat = Environment.GetEnvironmentVariable("ADO_PAT");
        if (!string.IsNullOrWhiteSpace(envPat))
        {
            ctx.Append(Log.Data.Message, "ADO PAT found in environment variable.");
            ctx.Succeeded();
            return envPat;
        }

        // 2. Try reading from Azure CLI cache (if az devops login used)
        var azPat = TryGetTokenFromAzCli();
        if (!string.IsNullOrWhiteSpace(azPat))
        {
            ctx.Append(Log.Data.Message, "ADO PAT found in Azure CLI.");
            ctx.Succeeded();
            return azPat;
        }

        // 3. Prompt user interactively
        Console.Write("Enter your Azure DevOps Personal Access Token (PAT): ");
        var pat = ReadPasswordMasked();
        if (string.IsNullOrWhiteSpace(pat))
        {
            ctx.Failed("No PAT provided.", Error.InvalidInput);
            return null;
        }
        ctx.Append(Log.Data.Message, "ADO PAT provided by user.");
        ctx.Succeeded();
        return pat;
    });

    private static string? TryGetTokenFromAzCli() => Log.Method(ctx =>
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $@"/c az.cmd account get-access-token --resource={Program.config.Ado.AdoOauthScope} --query=accessToken --output tsv",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            ctx.Failed("Failed to start Azure CLI process.", Error.ToolFailed);
            return null;
        }

        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            ctx.Failed($"Azure CLI command failed with exit code {proc.ExitCode}. Output: {output}", Error.ToolFailed);
            return null;
        }

        ctx.Succeeded();
        return output.Trim();
    });

    private static string ReadPasswordMasked()
    {
        Console.Write("Enter your Azure DevOps Personal Access Token (PAT)\nInput will be masked, press Enter when done, ESC to cancel.");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                Console.Write("\b \b");
                password.Length--;
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine("\nInput cancelled.");
                return string.Empty; // Allow cancellation
            }
            else if (!char.IsControl(key.KeyChar))
            {
                Console.Write("*");
                password.Append(key.KeyChar);
            }

        }
        return password.ToString();
    }
}
