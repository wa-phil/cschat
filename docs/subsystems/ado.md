# Azure DevOps Subsystem

**Registration name:** `"Ado"`
**Default enabled:** `false`
**Files:** `Subsytems/ADO/`

## Overview

Integrates CSChat with Azure DevOps (ADO) to query work items, browse pull requests, and compute ADO Insights scores. Uses the `Microsoft.TeamFoundation` and `Microsoft.VisualStudio.Services` NuGet packages for API access.

## Configuration

`AdoConfig` is stored directly in `Config.Ado` (not as a UserManaged type):

| Field | Description |
|-------|-------------|
| `Organization` | ADO organization name (e.g. `"yourorganization"`) |
| `ProjectName` | ADO project name |
| `RepositoryName` | Git repository name in ADO |
| `UseOAuthScope` | Whether to use a custom OAuth scope |
| `AdoOauthScope` | OAuth scope GUID (when `UseOAuthScope = true`) |
| `Insights` | `AdoInsightsConfig` — scoring thresholds and weights |

## AdoClient

**File:** `Subsytems/ADO/ADOClient.cs`

`[IsConfigurable("Ado")]` — implements `ISubsystem`.

On enable:
1. Creates a `VssConnection` using `DefaultAzureCredential` (or OAuth scope when configured).
2. Obtains `WorkItemTrackingHttpClient` and `GitHttpClient` from the connection.
3. Calls `Register()` to add the ADO command group to the command manager.

On disable: disposes the connection and removes the ADO commands.

Key operations (exposed via commands and tools):
- Query work items by area path, iteration, or custom WIQL
- List and filter pull requests in the configured repository
- Fetch work item details and comments

## ADO Insights

**File:** `Subsytems/ADO/ADOInsights.cs`

Computes a composite quality/health score for work items based on configurable weights and thresholds from `AdoInsightsConfig`. Used to surface high-risk or stale items.

## Commands

**File:** `Subsytems/ADO/ADOCommands.cs`, `ADOConfigCommands.cs`

Registers under the `ado` command group when the subsystem is enabled:

| Command | Description |
|---------|-------------|
| `ado > work items` | Query and list work items |
| `ado > pull requests` | Browse open pull requests |
| `ado > insights` | Show ADO Insights score summary |
| `ado > config` | Edit `AdoConfig` fields |

## User-Selected Queries

**File:** `Subsytems/ADO/UserSelectedQuery.cs`

Allows saving named WIQL queries as `[UserManaged]` items for quick re-use.

## Dependencies

None. ADO has no `[DependsOn]` attributes.
