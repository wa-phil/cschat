using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

[UserManaged("PRs Profile", "Settings for PR triage and reporting over Azure DevOps pull requests (via 1ES Kusto)")]
public sealed class PRsProfile
{
    [UserKey]
    public string Name { get; set; } = "";

    // Who is the manager (or managers) whose reports we care about?
    [UserField(required: true, display: "Manager Alias")]
    public string ManagerAlias { get; set; } = string.Empty;

    [UserField(display: "Exclude repositories (case-insensitive; names)")]
    public List<string> ExcludedRepos { get; set; } = new();

    [UserField(display: "Vendor prefixes to exclude (MailNickname startswith)")]
    public List<string> VendorPrefixes { get; set; } = new() { "v-" };


    // Windows
    [UserField(display: "Stale window: min age (days) default is 14")]
    public int StaleMinAgeDays { get; set; } = 14; // created before 14d

    [UserField(display: "Stale window: max age (days) default is 60")]
    public int StaleMaxAgeDays { get; set; } = 60; // but not older than 60d

    [UserField(display: "New window (days) default is 14")]
    public int NewWindowDays { get; set; } = 14; // created in last 14d

    [UserField(display: "Closed window (days) default is 30")]
    public int ClosedWindowDays { get; set; } = 30; // closed in last 30d

    // Kusto endpoints (override if needed)
    [UserField(display: "Cluster URI")]
    public string ClusterUri { get; set; } = string.Empty;

    [UserField(display: "AzureDevOps DB name")]
    public string AdoDatabase { get; set; } = string.Empty;

    [UserField(display: "AAD DB name")]
    public string AadDatabase { get; set; } = string.Empty;

    [UserField(display: "Timeout (seconds)")]
    public int TimeoutSeconds { get; set; } = 30;

    public override string ToString() => $"{Name} (manager:{ManagerAlias}) DB: {AdoDatabase} excluded repos: {string.Join(", ", ExcludedRepos)}";
}