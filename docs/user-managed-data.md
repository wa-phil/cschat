# User-Managed Data

Located in `/UserManagedData/` and `/Parsing/`. Provides an attribute-driven system for persisting user-defined data collections in `config.json` with automatic CRUD commands and pub/sub change notifications.

## Concept

Any C# class decorated with `[UserManaged]` and `[UserField]` / `[UserKey]` attributes is:

1. **Discovered** at startup via reflection
2. **Stored** in `config.UserManagedData.TypedData[TypeName]` as a list of `Dictionary<string, object>`
3. **Exposed** through CRUD commands under the **data** menu
4. **Observable** via `userManagedData.Subscribe<T>(handler)`

## Attributes

### `[UserManaged(name, description)]`

Applied to a class. Marks it for inclusion in the `UserManagedData` subsystem.

- `name` — the human-readable collection name shown in menus
- `description` — tooltip or help text

### `[UserField(...)]`

Applied to a property. Marks it as a form field.

| Parameter | Description |
|-----------|-------------|
| `required` | Whether the field is required in forms |
| `display` | Override label text |
| `hidden` | If true, the field is not shown in add/edit forms |
| `hint` | Help text shown in the form |
| `FieldKind` | `UiFieldKind` for the field type (default: `Text`) |
| `Placeholder` | Placeholder text |
| `Min`, `Max` | Integer bounds |
| `Pattern` | Regex validation pattern |
| `Choices` | String array for dropdown options |

### `[UserKey]`

Applied to one property per class. Identifies the property used as the identity for update and delete predicates. If absent, a property named `"Id"` (case-insensitive) is used if present.

### `[ExampleText(text)]`

Applied to a class. Provides a JSON example string shown by `TypeParser` when asking the LLM to generate a value of this type.

## UserManagedData Class

**File:** `UserManagedData/UserManagedData.cs`

### Lifecycle

- `Connect()` — discovers all `[UserManaged]` types via `AppDomain.CurrentDomain.GetAssemblies()`, registers them, and adds the `DataCommands` command group.
- `Dispose()` — removes the `Data` command group.

### CRUD Operations

| Method | Description |
|--------|-------------|
| `GetItems<T>()` | Deserializes all items of type `T` from config; returns `List<T>` |
| `AddItem<T>(item)` | Serializes item to dictionary and appends to the list; notifies subscribers |
| `UpdateItem<T>(item, predicate)` | Finds the first item matching `predicate` and replaces it; notifies subscribers |
| `DeleteItem<T>(predicate)` | Removes all matching items; notifies each deleted item to subscribers |

### Pub/Sub

```csharp
// Subscribe to changes for a specific type
IDisposable sub = userManagedData.Subscribe<ChatThread>((type, change, item) => {
    if (change == UserManagedData.ChangeType.Deleted) { /* handle delete */ }
});

// Unsubscribe
sub.Dispose();
```

`ChangeType` values: `Added`, `Updated`, `Deleted`.

## Built-in UserManaged Types

| Type | Collection name | Key |
|------|-----------------|-----|
| `ChatThread` | `"Chat threads"` | `Name` |
| `RagFileType` | `"RAG File Type"` | `Extension` |
| `McpServerDefinition` | `"McpServers"` | `Name` |
| `KustoConfig` | `"Kusto Configuration"` | `Name` |

## Defining a New UserManaged Type

```csharp
[UserManaged("My Items", "A collection of my items.")]
public class MyItem
{
    [UserKey]
    [UserField(required: true, display: "Name", FieldKind = UiFieldKind.String)]
    public string Name { get; set; } = "";

    [UserField(display: "Description", FieldKind = UiFieldKind.Text)]
    public string Description { get; set; } = "";

    [UserField(required: true, display: "Count", FieldKind = UiFieldKind.Number)]
    public int Count { get; set; } = 0;
}
```

No registration code is required. `UserManagedData.Connect()` discovers the type at startup and creates the full CRUD menu automatically.

## TypeParser

**File:** `Parsing/TypeParser.cs`

Uses the LLM to parse a structured object from a natural-language context. Used by the `Planner` to generate tool inputs and evaluate progress.

### `TypeParser.GetAsync(Context, Type)`

1. Looks up an `[ExampleText]` attribute on the target type and appends it to the system message.
2. Uses reflection to call `PostChatAndParseAsync<T>(context)` generically.
3. Returns the deserialized object.

### `PostChatAndParseAsync<T>(Context)`

1. Calls `Engine.Provider.PostChatAsync(context, temperature = 0.2f)`.
2. Extracts the first valid JSON object from the response (using a regex to handle markdown code fences and prose before/after the JSON).
3. Calls `JSONParser.ParseValue(typeof(T), json)` to deserialize.
4. Retries up to 2 times on `EmptyResponse` or `FailedToParseResponse` errors.

`ParsedTypes.cs` contains the type definitions used by the Planner: `PlanObjective`, `ToolSelection`, `PlanProgress`, and `NoInput`.
