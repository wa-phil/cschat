# JSON

Located in `/Json/`. A custom, zero-dependency JSON parser and writer used throughout CSChat for config persistence, UserManagedData storage, LLM message serialization, and tool input/output.

## Motivation

CSChat avoids `System.Text.Json` and `Newtonsoft.Json` as external runtime dependencies for its internal data formats. The custom implementation is lightweight, reflection-based, and handles the types used by the application gracefully rather than throwing on malformed input.

## JSONParser (`Json/JSONParser.cs`)

### `FromJson<T>(this string json)`

Extension method on `string`. Thread-safe via `[ThreadStatic]` caches for `FieldInfo`, `PropertyInfo`, and a string-split pool.

### `ParseValue(Type type, string json)`

Recursive type-aware parser:

| Input type | Behavior |
|------------|----------|
| `string` | Unquotes and unescapes. Returns `""` for malformed input. |
| `int`, `float`, `double`, `bool` | `TryParse` with fallback to zero/false. |
| `Guid` | Parses quoted string; returns `Guid.Empty` on failure. |
| `Enum` | Tries name-based parse (case-insensitive), then numeric; falls back to default. |
| `T[]` | Splits bracketed content; creates typed array. |
| `List<T>` | Splits bracketed content; creates `List<T>`. |
| `Dictionary<string, V>` | Splits braced content; parses key-value pairs. |
| `object` (dynamic) | `ParseDynamic` — returns `Dictionary<string, object?>`, `List<object?>`, `string`, `int`, `double`, `bool`, or `null`. |
| Any class | `Activator.CreateInstance(type)`, then maps JSON keys to public writable properties and public fields. Respects `[DataMember(Name=...)]` and `[IgnoreDataMember]`. |

Malformed inputs return empty collections, zero values, or default instances rather than throwing.

### Caches

`[ThreadStatic]` dictionaries cache `FieldInfo` and `PropertyInfo` lookups per type per thread, avoiding repeated reflection.

## JSONWriter (`Json/JSONWriter.cs`)

### `ToJson(this object? obj)`

Extension method on `object?`. Recursively serializes:

- `null` → `"null"`
- `string` → quoted and escaped
- Numeric types → culture-invariant `ToString()`
- `bool` → `"true"` / `"false"`
- `Enum` → quoted name string
- `Guid` → quoted lowercase string
- `IEnumerable` (non-string, non-dictionary) → JSON array
- `IDictionary` → JSON object with string keys
- Any other object → JSON object using public readable properties (respects `[DataMember]` and `[IgnoreDataMember]`)

All numbers are serialized culture-invariantly (`CultureInfo.InvariantCulture`) to avoid locale-specific decimal separators.

## Usage in the Application

| Usage | Location |
|-------|----------|
| Config load/save | `Config.Load()` / `Config.Save()` |
| UserManagedData items | `GetItems<T>()` → `FromJson<T>()` / `ToJson()` |
| Context serialization | `Context.Save()` / `Context.Load()` |
| LLM response parsing | `TypeParser.PostChatAndParseAsync<T>()` |
| Tool input parsing | `CommandManager.ParseToolInput()` |
| Dynamic JSON from providers | `respJson.FromJson<dynamic>()` in Ollama |
| Log entry serialization | `Log.GetOutput()` returns `item.ToJson()` |
