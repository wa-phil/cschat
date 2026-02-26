# Kusto Subsystem

**Registration name:** `"Kusto"`
**Default enabled:** `true`
**Files:** `Subsytems/Kusto/`

## Overview

Integrates CSChat with Azure Data Explorer (Kusto) clusters. Supports multiple named cluster connections, saved KQL queries, and LLM-assisted query tools.

## KustoConfig (UserManaged)

**File:** `Subsytems/Kusto/KustoConfig.cs`

`[UserManaged("Kusto Configuration", ...)]` — each entry represents one connection:

| Field | Description |
|-------|-------------|
| `Name` | Unique name for this connection (`[UserKey]`) |
| `ClusterUri` | Cluster endpoint (e.g. `https://cluster.region.kusto.windows.net`) |
| `Database` | Default database name |
| `AuthMode` | `devicecode`, `prompt`, `azcli`, or `managedIdentity` |
| `DefaultTimeoutSeconds` | Query timeout (default 60 s) |
| `Queries` | List of saved `KustoQuery` items (name, description, KQL, tags) |

Users manage connections via **data > Kusto Configuration** in the menu.

## KustoClient

**File:** `Subsytems/Kusto/KustoClient.cs`

`[IsConfigurable("Kusto")]` — implements `ISubsystem`.

### Connection Pool

Maintains a `ConcurrentDictionary<string, (ICslQueryProvider, ClientRequestProperties, KustoConfig)>` keyed by connection name. Connections are established on demand from `KustoConfig` entries stored in `UserManagedData`.

On enable: subscribes to `UserManagedData` changes for `KustoConfig` to auto-refresh connections when entries are added, updated, or deleted.

### Auth Modes

| Mode | Credential |
|------|-----------|
| `devicecode` | Interactive device code flow |
| `prompt` | Interactive username/password prompt |
| `azcli` | Azure CLI credential |
| `managedIdentity` | Managed identity (workload/pod identity) |

### Executing Queries

`KustoClient` provides methods to run KQL queries and return results as a list of `Dictionary<string, object>` rows. Results can be rendered as tables or added to the vector store.

## KustoTools

**File:** `Subsytems/Kusto/KustoTools.cs`

Registers Kusto-specific `ITool` implementations into `ToolRegistry` when the subsystem is enabled, making them available to the Planner. Tools include executing a named saved query and executing ad-hoc KQL.

## Commands

**File:** `Subsytems/Kusto/KustoCommands.cs`

Registers under the `kusto` command group:

| Command | Description |
|---------|-------------|
| `kusto > connections` | Manage KustoConfig entries (via UserManagedData CRUD) |
| `kusto > query` | Run a saved query or ad-hoc KQL |
| `kusto > results to rag` | Ingest the last query result into the vector store |

## Dependencies

None. Kusto has no `[DependsOn]` attributes, but PRs and S360 depend on it.
