using Azure;
using System;
using System.Linq;
using System.Text;
using Azure.Identity;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Core.Diagnostics;
using System.Diagnostics.Tracing;
using System.Collections.Generic;
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
        _tools.ForEach(tool=>ToolRegistry.RegisterTool(tool.Key, tool.Value()));
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
        ctx.Append(Log.Data.Input, id);
        var workItem = await _witClient.GetWorkItemAsync(id, SummaryFields);
        ctx.Succeeded();
        return WorkItemSummary.FromWorkItem(workItem);
    });

    public async Task<List<WorkItemSummary>> GetWorkItemSummariesByQueryId(Guid queryId) => await Log.MethodAsync(async ctx =>
    {
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
