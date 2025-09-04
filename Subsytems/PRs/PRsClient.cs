using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

[IsConfigurable("PRs")]
[DependsOn("Kusto")]
[DependsOn("Ado")]
public class PRsClient : ISubsystem
{
    public bool IsAvailable => true;
    private bool _active;

    public bool IsEnabled
    {
        get => _active;
        set
        {
            if (value && !_active)
            {
                _active = true;
                Register();
            }
            else if (!value && _active)
            {
                Unregister();
                _active = false;
            }
        }
    }

    public Type ConfigType => typeof(PRsProfile);

    public void Register() => Program.commandManager.SubCommands.Add(PRsCommands.Commands(this));
    public void Unregister() => Program.commandManager.SubCommands.RemoveAll(c => c.Name.Equals("PRs", StringComparison.OrdinalIgnoreCase));

    private static KustoConfig BuiltInKustoConfig(PRsProfile p) => new KustoConfig
    {
        Name = $"PRs ({p.ClusterUri}/{p.AdoDatabase})",
        ClusterUri = p.ClusterUri,
        Database = p.AdoDatabase,
        AuthMode = KustoAuthMode.devicecode,
        DefaultTimeoutSeconds = Math.Max(5, p.TimeoutSeconds)
    };

    public sealed class PRRow
    {
        public string Category = "";        // stale | new | closed
        public string CreatedByDisplayName = "";
        public string CreatedByUniqueName = "";
        public string Title = "";
        public string Description = "";
        public string RepositoryName = "";
        public string RepositoryProjectName = "";
        public string OrganizationName = "";
        public string Link = "";
        public string CreationDate = "";
        public string ClosedDate = "";
        public string Status = "";
        public string PullRequestId = "";
        public string AgeDays = "";        // computed for convenience
    }

    // Build a single KQL that emits Category for the three slices using shared, materialized authors set
    public static string BuildKql(PRsProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.ManagerAlias)) throw new ArgumentException("Profile must specify at least one Manager Alias");
        string emList = $"\"{p.ManagerAlias}\"";

        // Build vendor regex like ^(v-|x-). If none provided, VendorRx = "" (disabled).
        var vendorPrefixes = (p.VendorPrefixes ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => Regex.Escape(s.Trim()))
            .ToList();
        var vendorRx = vendorPrefixes.Count == 0 ? "" : "^(" + string.Join("|", vendorPrefixes) + ")";

        // Other knobs
        string excludedRepos = string.Join(",", p.ExcludedRepos.Select(r => $"\"{r}\""));
        int staleMin  = Math.Max(1, p.StaleMinAgeDays);
        int staleMax  = Math.Max(staleMin + 1, p.StaleMaxAgeDays);
        int newWin    = Math.Max(1, p.NewWindowDays);
        int closedWin = Math.Max(1, p.ClosedWindowDays);
        string cherry = "cherry-pick";

        // Interpolated verbatim string — no raw-triple quotes, no doubled braces.
        var kql = $@"
let ReportsTo = (aliases: dynamic) {{
    cluster('1es').database('{p.AadDatabase}').AADUser
    | where ReportsToEmailName in~ (aliases)
    | summarize make_set(MailNickname)
}};
let EM = dynamic([{emList}]);
let VendorRx = '{vendorRx}';
let ICs = ReportsTo(toscalar(EM));

// Materialize authors; exclude vendors via regex if VendorRx provided.
let authors = materialize(
    cluster('1es').database('{p.AadDatabase}').AADUser
    | where MailNickname in~ (ICs)
    | extend IsVendor = iif(isempty(VendorRx), false, tostring(MailNickname) matches regex VendorRx)
    | where IsVendor == false
    | project AuthorUniqueName = strcat(MailNickname, '@microsoft.com')
);

// Recently closed (for left-anti join when computing 'new')
let ClosedLast{newWin} = materialize(
    cluster('1es').database('{p.AdoDatabase}').PullRequest
    | where RepositoryName !in~ (dynamic([{excludedRepos}]))
    | where ClosedDate > ago({newWin}d)
    | where CreatedByUniqueName in~ (authors)
    | where isempty(Description) or not(tolower(Description) has '{cherry.ToLowerInvariant()}')
    | project PullRequestId, OrganizationName, RepositoryProjectName, RepositoryName
);

// Base slice used by all categories
let base = cluster('1es').database('{p.AdoDatabase}').PullRequest
    | where RepositoryName !in~ (dynamic([{excludedRepos}]))
    | where CreatedByUniqueName in~ (authors)
    | where isempty(Description) or not(tolower(Description) has '{cherry.ToLowerInvariant()}')
    | extend Link = strcat('https://', OrganizationName, '.visualstudio.com/', RepositoryProjectName, '/_git/', RepositoryName, '/pullrequest/', PullRequestId)
    | project CreatedByDisplayName, CreatedByUniqueName, CreationDate, ClosedDate, Title, Description, Status, OrganizationName, RepositoryProjectName, RepositoryName, PullRequestId, Link;

let stale = base
    | where CreationDate < ago({staleMin}d) and CreationDate > ago({staleMax}d)
    | where isempty(ClosedDate) and Status != 'abandoned'
    | extend Category = 'stale';

let new_open = base
    | where CreationDate > ago({newWin}d)
    | join kind=leftanti ClosedLast{newWin} on PullRequestId
    | extend Category = 'new';

let closed = base
    | where ClosedDate > ago({closedWin}d)
    | where Status <> 'abandoned'
    | extend Category = 'closed';

union stale, new_open, closed
| extend AgeDays = iif(isempty(ClosedDate),
                       datetime_diff('day', now(), CreationDate) * -1,
                       datetime_diff('day', ClosedDate, CreationDate))
| project Category, CreatedByDisplayName, CreatedByUniqueName, CreationDate, ClosedDate, Title, Description, Link, Status, OrganizationName, RepositoryProjectName, RepositoryName, PullRequestId, AgeDays
| order by CreatedByDisplayName asc, CreationDate asc, ClosedDate asc
";
        return kql;
    }

    public async Task<Table> FetchAsync(PRsProfile profile) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var kusto = Program.SubsystemManager.Get<KustoClient>();
        var kql = BuildKql(profile);
        ctx.Append(Log.Data.Kql, kql);

        // Prefer a connected UMD config that matches the 1ES endpoint if present, else use ephemeral built-in
        var umd = Program.userManagedData.GetItems<KustoConfig>()
            .FirstOrDefault(c => c.ClusterUri.TrimEnd('/')
                .Equals(profile.ClusterUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                && c.Database.Equals(profile.AdoDatabase, StringComparison.OrdinalIgnoreCase));

        Table result = null!;
        if (umd is not null && kusto.IsConnected(umd.Name))
        {
            result = await kusto.QueryAsync(umd, kql);
            ctx.Succeeded();
            return result;
        }

        var built = BuiltInKustoConfig(profile);
        await kusto.EnsureConnectedAsync(built);
        result = await kusto.QueryAsync(built, kql);
        ctx.Succeeded();
        return result;
    });
}