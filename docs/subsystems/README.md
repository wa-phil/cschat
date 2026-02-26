# Subsystems

Located in `/Subsytems/`. Optional plugin subsystems that extend CSChat with external service integrations.

## ISubsystem Interface

```csharp
public interface ISubsystem
{
    bool IsAvailable { get; }
    bool IsEnabled { get; set; }
}
```

- `IsAvailable` — whether the subsystem can run in the current environment (e.g. Windows-only subsystems return false on Linux)
- `IsEnabled` (setter) — when set to `true`, the subsystem connects to its service and registers its command group; when set to `false`, it disconnects and unregisters

## SubsystemManager

**File:** `Subsytems/SubsystemManager.cs`

Manages the lifecycle of all discovered subsystems.

| Method | Description |
|--------|-------------|
| `Register(dict)` | Stores the name→type mapping discovered via DI |
| `Connect()` | Iterates `config.Subsystems` and calls `SetEnabled(name, value)` for each |
| `SetEnabled(name, bool)` | Enables/disables a subsystem; auto-enables dependencies; cascades disables to dependents |
| `IsEnabled(name)` | Returns current enabled state |
| `Get<T>()` | Resolves the subsystem instance from the DI service provider |
| `All(filter?)` | Iterates all registered subsystems with an optional predicate |

Enabled state is persisted to `config.Subsystems[name]` as a `bool`.

## Attributes

### `[IsConfigurable("name")]`

Applied to the subsystem class. The `name` value is the key used in `config.Subsystems` and in all subsystem commands. Required for auto-discovery.

### `[DependsOn("OtherSubsystemName")]`

Applied to a subsystem class (repeatable). Declares that enabling this subsystem first requires the named subsystem to be enabled. Disabling a subsystem cascades to all dependents.

## Enabling and Disabling

**Via config.json:**
```json
"Subsystems": {
  "Ado": false,
  "Mcp": true,
  "Kusto": true
}
```

**Via the menu:**
The `SubsystemCommands` command group (under the root menu) provides a toggle command for each registered subsystem. The command label shows the current enabled state.

## Adding a New Subsystem

1. Create a new class in `/Subsytems/YourName/` implementing `ISubsystem`.
2. Decorate it with `[IsConfigurable("YourName")]`.
3. Implement `IsEnabled` setter: call `Register()` when enabling, `Unregister()` when disabling.
4. `Register()` should call `Program.commandManager.SubCommands.Add(YourCommands.Commands(this))`.
5. `Unregister()` should remove those commands by name.
6. Add `[DependsOn("Other")]` attributes if your subsystem requires another.
7. No manual registration is required — the type is discovered automatically by `Program.Startup()`.

## Available Subsystems

| Name | Default | Description |
|------|---------|-------------|
| [`Ado`](ado.md) | Disabled | Azure DevOps work items and pull requests |
| [`Kusto`](kusto.md) | Enabled | Azure Data Explorer / Kusto queries |
| [`Mcp`](mcp.md) | Enabled | Model Context Protocol server management |
| [`Mail`](mail.md) | (not in default config) | MAPI / Outlook email |
| [`PRs`](prs.md) | (not in default config) | Pull request analytics (requires Kusto + ADO) |
| [`S360`](s360.md) | (not in default config) | Service 360 health data (requires Kusto) |
