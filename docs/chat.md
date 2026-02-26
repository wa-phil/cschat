# Chat Threads

Located in `/Chat/`. Provides persistent, named conversation threads with LLM-assisted auto-naming.

## ChatThread

**File:** `Chat/ChatThread.cs`
**UserManaged name:** `"Chat threads"`

```csharp
[UserManaged("Chat threads", "History of previous chats.")]
public sealed class ChatThread
{
    [UserKey] public string Name { get; set; } = "";
    [UserField(required: false)] public string Description { get; set; } = "";
    [UserField(required: false, hidden: true)] public string LastUsedUtc { get; set; } = ...;
}
```

- `Name` is the user-visible identity and the `[UserKey]` for updates.
- `Description` is optional free-text set by the user or the LLM.
- `LastUsedUtc` is hidden in forms; used for sorting threads by recency.

Each thread's conversation is stored as `chat.json` under `config.ChatThreadSettings.RootDirectory/<thread-name>/`.

## ChatManager

**File:** `Chat/ChatManager.cs`

Static class managing the active thread lifecycle.

### Initialization

`ChatManager.Initialize(userManagedData)` subscribes to `UserManagedData` changes for `ChatThread`. When a thread is deleted, `OnThreadDeleted` removes the thread directory from disk and switches to a new blank thread if the deleted thread was active.

### Thread Operations

| Method | Description |
|--------|-------------|
| `LoadThread(t)` | Clears the current `Context` and loads `chat.json` from the thread directory. If no file exists, adds only the system prompt. |
| `SaveActiveThread(t)` | Saves the current `Context` to the thread's `chat.json`. If the thread has no name, calls `NameAndDescribeThread` first. |
| `SwitchTo(t)` | Saves the current thread (implicitly via `SaveActiveThread` if needed), clears `Context`, loads the target thread. |
| `CreateNewThread()` | Saves and names the current thread, then creates a fresh blank thread and switches to it. |
| `NameAndDescribeThread(ctx)` | Calls `TypeParser.GetAsync<ChatThread>()` to generate a name and description from the conversation. Avoids collisions with existing thread names. |

### Auto-naming

When a thread is saved or a new one is created, `NameAndDescribeThread` builds a prompt instructing the LLM to generate a concise, unique name and optional description based on the recent conversation. Existing thread names are provided to avoid duplicates.

### Storage

```
<RootDirectory>/
  <thread-name>/
    chat.json       ← serialized Context (system message + messages + context chunks)
```

`RootDirectory` defaults to `<executable-dir>/.threads`. It is configurable in `config.ChatThreadSettings.RootDirectory`.

## ChatCommands

**File:** `Chat/ChatCommands.cs`

Creates the `chat` command group with sub-commands for:
- Creating a new thread
- Switching between threads (rendered as a menu from `userManagedData.GetItems<ChatThread>()`)
- Deleting a thread
- Managing thread names/descriptions via the standard UserManagedData CRUD interface
