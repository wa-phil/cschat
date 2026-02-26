# Tools

Located in `/Tools/`. Tools are autonomous capabilities the LLM can invoke during planning.

## ITool Interface

```csharp
public interface ITool
{
    string Description { get; }
    string Usage { get; }
    Type InputType { get; }
    string InputSchema { get; }
    Task<ToolResult> InvokeAsync(object input, Context Context);
}
```

- `InputType` — the C# type the tool expects (e.g. `PathInput`, `string`, `NoInput`)
- `InvokeAsync` receives the deserialized input object and a `Context` it may modify
- `ToolResult` carries `Succeeded`, `Response` string, updated `Context`, and optional `Error`

Tools are marked with `[IsConfigurable("tool_name")]` and registered in DI.

## ToolRegistry

**File:** `Tools/ToolRegistry.cs`

Static registry for all tool instances. Key operations:

| Method | Description |
|--------|-------------|
| `Initialize()` | Resolves all `ITool` implementations from the DI service provider |
| `RegisterTool(name, tool)` | Registers a tool at runtime (used by MCP) |
| `UnregisterTool(name)` | Removes a tool (used when MCP server disconnects) |
| `GetRegisteredTools()` | Returns name/description/usage tuples |
| `GetToolDescriptions()` | Returns full schema descriptions for the Planner prompt |
| `InvokeToolAsync(name, input, ctx?)` | Invokes a tool by name; adds result to `ContextManager` |
| `InvokeInternalAsync(...)` | Full internal invocation with error handling and context update |

After invocation, the tool result is also added to the `ContextManager` under the key `"toolName(input JSON)"`.

## Built-in Tools

### file_system tools (`Tools/file_system.cs`)

| Name | Input | Description |
|------|-------|-------------|
| `file_list` | `PathInput` | Lists files in a directory recursively, filtered by `RagFileType` |
| `file_metadata` | `PathInput` | Returns size, line count, word count, char count, last modified |
| `summarize_file` | `PathInput` | Reads file content (up to 16 000 chars), adds to vector store, returns content |
| `grep_files` | `PathAndRegexInput` | Searches for a .NET regex pattern across all supported files; returns matching lines with context |
| `find_file` | `PathAndRegexInput` | Lists files whose relative path matches a .NET regex |

`PathInput` has a single `Path` property. `PathAndRegexInput` has `Path` and `Pattern`.

### misc_tools (`Tools/misc_tools.cs`)

| Name | Input | Description |
|------|-------|-------------|
| `explain_program_to_user` | `NoInput` | Asks the LLM to explain CSChat using the current command tree and config |
| `summarize_text` | `SummarizeText` | Summarizes provided text with an optional guiding prompt |
| `search_knowledge_base` | `string` | Semantic search against the local vector store |
| `Calculator` | `string` | Evaluates a math expression using `DataTable.Compute` |
| `datetime_current` | `NoInput` | Returns the current date/time in UTC format |

`SummarizeText` has `Text` and optional `Prompt` fields.

`NoInput` is a sentinel type requiring no input.

## Planner

**File:** `Tools/Planner.cs`

The `Planner` implements an agentic loop: it converts a user message into an objective, selects tools, generates typed inputs, executes them, and evaluates progress — repeating until the goal is achieved or limits are hit.

### Flow

```
PostChatAsync(context)
  → GetObjective(context)          // LLM: does this need tool use?
  if TakeAction = true:
    while not done and steps < MaxSteps:
      → GetToolSelection(goal, ctx) // LLM: which tool next?
      → GetToolInput(toolName, ...)  // LLM: generate typed input JSON
      → ToolRegistry.InvokeInternalAsync(tool, input, ctx)
      → summarize tool output       // small LLM call at temp 0.2
      → GetPlanProgress(ctx, goal)  // LLM: is goal achieved?
  → if achieved: return tool output
  → else: final LLM summarization of steps taken
```

### Internal Types

| Type | Description |
|------|-------------|
| `PlanObjective` | `TakeAction: bool`, `Goal: string` |
| `ToolSelection` | `ToolName: string`, `Reasoning: string` |
| `PlanProgress` | `GoalAchieved: bool` |
| `PlanStep` | `Done: bool`, `ToolName`, `ToolInput`, `Reason` |

These types are parsed from LLM responses via `TypeParser.GetAsync()` (see [user-managed-data.md](user-managed-data.md)).

### Guardrails

- **Max steps:** `config.MaxSteps` (default 25). Exceeding it forces a summarization exit.
- **Duplicate steps:** the same `(toolName, inputJSON)` pair is tracked in `actionsTaken`. After 3 duplicates, planning exits with a failure summary.
- **Planning failure:** if `GetNextStep` throws or returns an empty tool name, `onPlanningFailure` is called and the loop exits.
- **Retry:** `GetNextStep` retries up to 2 times on `CsChatException` with `EmptyResponse`.

### Early Exit

If `GetObjective` returns `TakeAction = false` (the question doesn't need tool use), the Planner bypasses the loop and calls the provider directly — skipping all tool overhead.
