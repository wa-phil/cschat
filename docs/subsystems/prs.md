# Pull Requests Subsystem

**Registration name:** `"PRs"`
**Default enabled:** not in default `config.Subsystems`
**Files:** `Subsytems/PRs/`

## Overview

Provides pull request analytics by combining Azure DevOps PR data (via the Kusto ADO mirror) with team hierarchy information. Surfaces stale, new, and recently closed PRs for a configured set of repositories and team members.

## PRsClient

**File:** `Subsytems/PRs/PRsClient.cs`

`[IsConfigurable("PRs")]`
`[DependsOn("Kusto")]`
`[DependsOn("Ado")]`

Implements `ISubsystem`. On enable: calls `Register()` to add the `PRs` command group. On disable: calls `Unregister()`.

### PRsProfile (UserManaged)

**File:** `Subsytems/PRs/PRsProfile.cs`

`[UserManaged]` type for configuring a PR analytics profile:
- `ClusterUri` — Kusto cluster hosting the ADO mirror data
- `AdoDatabase` — database name
- `TimeoutSeconds` — query timeout

PRsClient builds a `KustoConfig` on-the-fly from the profile when executing queries (it does not require a separately registered Kusto connection entry).

### KQL Construction

`BuildActionItemsKql(serviceIds)` and similar methods construct KQL queries against the Kusto ADO mirror (tables like `PullRequests`, `PullRequestPolicies`, `Repositories`). Results include PR category (`stale`, `new`, `closed`), author, title, description, repository, and links.

### PRRow

Internal record for a single PR result with fields: `Category`, `CreatedByDisplayName`, `CreatedByUniqueName`, `Title`, `Description`, `RepositoryName`, `RepositoryProjectName`, `OrganizationName`, `Link`, `CreationDate`, `ClosedDate`.

## PRsTools

**File:** `Subsytems/PRs/PRsTools.cs`

Registers `ITool` implementations for PR analytics (query by author, by repository, by date range) so the Planner can invoke them autonomously.

## Commands

**File:** `Subsytems/PRs/PRsCommands.cs`

Registers under the `PRs` command group:

| Command | Description |
|---------|-------------|
| `PRs > my PRs` | List open PRs for the current user |
| `PRs > team PRs` | List open PRs for a team |
| `PRs > stale PRs` | List PRs that have been open longer than a threshold |
| `PRs > profile` | Edit the PRsProfile configuration |

## Dependencies

Requires both `Kusto` and `Ado` to be enabled. `SubsystemManager` auto-enables them when PRs is enabled.
