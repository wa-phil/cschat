using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using ModelContextProtocol.Protocol;

// field types for user input forms
public enum UiFieldKind
{
    String,     // tracks with a single line of text
    Text,       // tracks with multi-line text
    Number,     // integer
    Decimal,    // floating point
    Long,       // long integer
    Guid,       // GUID: globally unique identifier
    Enum,       // enumeration
    Bool,       // boolean value
    Date,       // date value
    Time,       // time value
    Path,       // file system path
    Password,   // password input
    Array       // list/array of values
}

public enum PathPickerMode
{
    OpenExisting,
    SaveFile
}

public interface IUiField
{
    string Key { get; } // stable identity for roundtrips
    string Label { get; }
    UiFieldKind Kind { get; }
    bool Required { get; }
    string? Help { get; }

    string? Placeholder { get; }
    string? Pattern { get; }
    string? PatternMessage { get; }

    PathPickerMode PathMode { get; }

    Func<object?, string> Formatter { get; }

    IEnumerable<string>? EnumChoices();
    int? MinInt(); int? MaxInt();

    // For Array fields (simple element types), the UI sends JSON text; for others a plain string
    bool TrySetFromString(object model, string? value, out string? error);

    IUiField WithHelp(string? help);
    IUiField IntBounds(int? min = null, int? max = null);
    IUiField FormatWith(Func<object?, string> format);
    IUiField MakeOptional();
    IUiField WithPlaceholder(string? placeholder);
    IUiField WithRegex(string pattern, string? message = null);
    IUiField MakeOptionalIf(bool cond);
    IUiField WithPathMode(PathPickerMode mode);
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class IsConfigurable : Attribute
{
    public string Name { get; }
    public IsConfigurable(string name) => Name = name;
}

// Attribute to provide example text for input types, used for generating input prompts
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ExampleText : Attribute
{
    public string Text { get; }
    public ExampleText(string text) => Text = text;
}

// Attribute to mark types for inclusion in UserManagedData subsystem
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class UserManagedAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public UserManagedAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

// Attribute to express subsystem dependencies. Apply on ISubsystem implementations.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class DependsOnAttribute : Attribute
{
    public string Name { get; }
    public DependsOnAttribute(string name) => Name = name;
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class UserFieldAttribute : Attribute
{
    public bool Required { get; }
    public bool Hidden { get; }
    public string? Display { get; set; }
    public string? Hint { get; set; }

    // Allow specifying the desired UI field kind and extra metadata.
    // These are optional named properties so existing attribute usages remain valid.
    public UiFieldKind FieldKind { get; set; } = UiFieldKind.Text; // default to Text to preserve prior behavior
    public string? Placeholder { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
    public string? Pattern { get; set; }
    public string[]? Choices { get; set; }

    public UserFieldAttribute(bool required = false, string? display = null, bool hidden = false, string? hint = null)
    {
        Required = required;
        Display = display;
        Hidden = hidden;
        Hint = hint;
    }
}

/// <summary>
/// Marks the property used as the logical key for updates.
/// If absent, a property named "Id" (case-insensitive) will be used if present.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class UserKeyAttribute : Attribute { }

public interface IChatProvider
{
    Task<List<string>> GetAvailableModelsAsync();
    Task<string> PostChatAsync(Context history, float temperature);
}

public interface IEmbeddingProvider
{
    Task<float[]> GetEmbeddingAsync(string text);
    Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct);
}

public record Reference(string Source, int? Start, int? End)
{
    public static Reference Full(string source) => new Reference(source, null, null);
    public static Reference Partial(string source, int start, int end) => new Reference(source, start, end);
    public override string ToString() => Start.HasValue && End.HasValue
        ? $"{Source} lines {Start.Value} to {End.Value}"
        : Source;
}

public record SearchResult
{
    public float Score;
    required public Reference Reference;
    required public string Content;
}

public interface IVectorStore
{
    void Add(List<(Reference Reference, string Chunk, float[] Embedding)> entries);
    void Clear();
    List<SearchResult> Search(float[] queryEmbedding, int topK = 3);
    List<SearchResult> SearchReferences(string reference);
    bool IsEmpty { get; }
    int Count { get; }

    List<(Reference Reference, string Content)> GetEntries(Func<Reference, string, bool>? filter = null);
}

public interface ITextChunker
{
    List<(Reference Reference, string Content)> ChunkText(string path, string text);
}

public record ToolResult(bool Succeeded, string Response, Context context, string? Error = null)
{
    public static ToolResult Success(string response, Context ctx) =>
        new(true, response, ctx, null);

    public static ToolResult Failure(string errorMessage, Context ctx) =>
        new(false, $"ERROR: {errorMessage}", ctx, Error: errorMessage);
}

public interface ITool
{
    string Description { get; }
    string Usage { get; } // Example: "Add(a, b)"
    Type InputType { get; } // The type expected for the input parameter
    string InputSchema { get; }
    Task<ToolResult> InvokeAsync(object input, Context Context); // Returns response text, and optionally modifies Context for context
}

public interface ISubsystem
{
    bool IsAvailable { get; }
    bool IsEnabled { get; set; }
}

