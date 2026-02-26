# Mail Subsystem

**Registration name:** `"Mail"`
**Default enabled:** not in default `config.Subsystems`
**Files:** `Subsytems/MAPI/`
**Platform:** Windows only (uses COM Automation via late-bound P/Invoke)

## Overview

Provides read access to a local Outlook/MAPI mail profile. Allows CSChat to search and summarize emails, list folders, and read message content via the MAPI COM API.

## MapiMailClient

**File:** `Subsytems/MAPI/MapiClient.cs`

`[IsConfigurable("Mail")]` — implements `ISubsystem` and `IMailProvider`.

On enable: calls `Connect()` (no-op — COM is late-bound on first use) and `Register()` (adds the `Mail` command group).

On disable: calls `Unregister()` to remove Mail commands.

### IMailProvider

The `IMailProvider` interface is defined in `Subsytems/MailInterfaces.cs` and provides:

| Method | Description |
|--------|-------------|
| `GetMessageAsync(id, ct)` | Retrieve a single message by ID |
| `GetFolderByIdOrNameAsync(idOrName, ct)` | Get a folder by ID or name |
| `ListFoldersAsync(folderIdOrName?, top, ct)` | List folders (default: top 50) |
| `ListMessagesSinceAsync(folder?, window, top, ct)` | List up to `top` messages received within `window` from `folder` |
| `DraftMessage(folderIdOrName, subject, body, to, cc, bcc)` | Create a draft message in the specified folder |
| `DraftReplyAsync(message, body, ct)` | Create a draft reply to a message |
| `DraftReplyAllAsync(message, body, ct)` | Create a draft reply-all to a message |
| `DraftForwardAsync(message, to, cc, bcc, body, ct)` | Create a draft forward of a message |
| `SendAsync(message, ct)` | Send a drafted message |
| `MoveAsync(message, folder, ct)` | Move a message to another folder |
| `DeleteAsync(message, ct)` | Delete a message |

All methods run on a background thread (`Task.Run`) to avoid blocking the STA UI thread.

### MAPI Access

`MapiMailClient` uses late-bound COM calls to the `Microsoft.Office.Interop.Outlook` type library via `Marshal.GetActiveObject` or `Activator.CreateInstance`. This avoids requiring Outlook to be installed at build time.

`MailSettings` in `Config` controls:
- `MaxEmailsToProcess` (default 25) — max emails per operation
- `LookbackWindow` (default 30 days) — how far back to search
- `LookbackCount` (default 100) — max emails fetched in one call

## MailUserData

**File:** `Subsytems/MAPI/MailUserData.cs`

Defines two `[UserManaged]` types:

**`FavoriteMailFolder`** — `[UserManaged("Favorite Mail Folders", ...)]`
- `IdOrName` (key) — folder ID or display name
- `DisplayName` — friendly label

**`MailTopic`** — `[UserManaged("Mail Topics", ...)]`
- `Name` (key) — topic name
- `Description` — free-text description
- `Keywords` — comma-separated search keywords

## Commands

**File:** `Subsytems/MAPI/MailCommands.cs`

Registers under the `Mail` command group:

| Command | Description |
|---------|-------------|
| `Mail > list folders` | List available mail folders |
| `Mail > search` | Search email with a query |
| `Mail > read` | Read the latest emails from a folder |
| `Mail > summarize` | Summarize recent emails using the LLM |
| `Mail > topics` | Manage saved mail topics |

## Dependencies

None. Mail has no `[DependsOn]` attributes.
