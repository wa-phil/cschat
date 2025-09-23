using System;
using System.Collections.Generic;

public enum UiFieldKind { String, Number, Guid, Enum, Bool }

public interface IUiField
{
    string Label { get; }
    UiFieldKind Kind { get; }
    bool Required { get; }
    string? Help { get; }

    // formatter: produce a display string for the current field value from a model object
    Func<object?, string> Formatter { get; }

    IEnumerable<string>? EnumChoices();           // only for Kind == Enum
    int? MinInt();                                // only for Kind == Int
    int? MaxInt();                                // only for Kind == Int

    // Host → apply the user-entered value to the model clone.
    // 'value' is the raw string coming back from UI; TrySet parses & validates.
    // Return true when successfully set; else set error with message to display/log.
    bool TrySetFromString(object model, string? value, out string? error);

    // Fluent setters for constraints & help (return non-generic for ease of chaining when model type isn't required)
    IUiField WithHelp(string? help);
    IUiField IntBounds(int? min = null, int? max = null);
    IUiField FormatWith(Func<object?, string> format); // custom formatting
    IUiField MakeOptional(); // Fields are required by default. Call MakeOptional() to mark a field optional.
}

public sealed class UiField<TModel, TValue> : IUiField
{
    public string Label { get; }
    public UiFieldKind Kind { get; }
    public bool Required { get; set; } = true; // Fields are required by default
    public string? Help { get; set; }

    // constraints/choices
    public int? Min { get; set; }
    public int? Max { get; set; }
    public IReadOnlyList<string>? Choices { get; set; } // for enums

    // projection
    private readonly Func<TModel, TValue> _get;
    private readonly Action<TModel, TValue> _set;

    // parsing/formatting
    private readonly Func<string?, (bool ok, TValue? value, string? error)> _tryParse;
    private Func<TValue?, string> _format;

    public UiField(
        string label,
        UiFieldKind kind,
        Func<TModel, TValue> get,
        Action<TModel, TValue> set,
        Func<string?, (bool ok, TValue? value, string? error)> tryParse)
    {
        Label = label;
        Kind = kind;
        _get = get;
        _set = set;
        _tryParse = tryParse;
        _format = v => v?.ToString() ?? "";

        if (UiFieldKind.Enum == kind)
        {
            Choices = Enum.GetValues(typeof(TValue)) is TValue[] arr
                ? Array.ConvertAll(arr, v => v?.ToString() ?? "")
                : null;
        }
    }

    // formatter uses typed getter + typed formatter
    public Func<object?, string> Formatter => model => _format(_get((TModel)model!));

    public IEnumerable<string>? EnumChoices() => Choices;
    public int? MinInt() => Min;
    public int? MaxInt() => Max;

    public bool TrySetFromString(TModel model, string? value, out string? error)
    {
        var (result, err) = Log.Method(ctx =>
        {
            ctx.OnlyEmitOnFailure()
                .Append(Log.Data.Input, value ?? "<null>")
                .Append(Log.Data.TypeToParse, Kind.ToString());

            if (Required && string.IsNullOrWhiteSpace(value))
            {
                ctx.Append(Log.Data.Message, $"Field [{Label}] is required.");
                return (false, "This field is required.");
            }
            var (ok, parsed, err) = _tryParse(value);
            if (!ok)
            {
                ctx.Append(Log.Data.Message, err ?? "Invalid value.");
                return (false, err ?? "Invalid value.");
            }
            if (Kind == UiFieldKind.Number)
            {
                if (parsed is int n)
                {
                    if (Min is int min && n < min)
                    {
                        ctx.Append(Log.Data.Message, $"Int must be ≥ {min}.");
                        return (false, $"Int must be ≥ {min}.");
                    }
                    if (Max is int max && n > max)
                    {
                        ctx.Append(Log.Data.Message, $"Int must be ≤ {max}.");
                        return (false, $"Int must be ≤ {max}.");
                    }
                }
                else if (parsed is double d)
                {
                    if (Min is int min && d < min)
                    {
                        ctx.Append(Log.Data.Message, $"Double must be ≥ {min}.");
                        return (false, $"Double must be ≥ {min}.");
                    }
                    if (Max is int max && d > max)
                    {
                        ctx.Append(Log.Data.Message, $"Double must be ≤ {max}.");
                        return (false, $"Double must be ≤ {max}.");
                    }
                }
                else if (parsed is float f)
                {
                    if (Min is int min && f < min)
                    {
                        ctx.Append(Log.Data.Message, $"Float must be ≥ {min}.");
                        return (false, $"Float must be ≥ {min}.");
                    }
                    if (Max is int max && f > max)
                    {
                        ctx.Append(Log.Data.Message, $"Float must be ≤ {max}.");
                        return (false, $"Float must be ≤ {max}.");
                    }
                }
            }

            _set(model, parsed!);
            ctx.Succeeded();
            return (true, string.Empty);
        });
        error = string.IsNullOrEmpty(err) ? null : err;
        return result;
    }

    // non-generic wrapper
    bool IUiField.TrySetFromString(object model, string? value, out string? error)
        => TrySetFromString((TModel)model!, value, out error);
    public UiField<TModel, TValue> WithHelp(string? help) { Help = help; return this; }
    public UiField<TModel, TValue> MakeOptional() { Required = false; return this; } // Switches default required behavior; fields are required unless made optional.
    public UiField<TModel, TValue> IntBounds(int? min = null, int? max = null) { Min = min; Max = max; return this; }
    public UiField<TModel, TValue> FormatWith(Func<object?, string> format) { _format = v => format(v); return this; }

    // non-generic fluent wrappers
    IUiField IUiField.WithHelp(string? help) { WithHelp(help); return this; }
    IUiField IUiField.MakeOptional() { MakeOptional(); return this; }
    IUiField IUiField.IntBounds(int? min, int? max) { IntBounds(min, max); return this; }
    IUiField IUiField.FormatWith(Func<object?, string> format) { FormatWith(format); return this; }
}
public sealed class UiForm
{
    public string Title { get; init; } = "Input";
    public object? Model { get; set; } // the form holds a cloned model as object; fields' formatters and TrySetFromString use this model
    public List<IUiField> Fields { get; } = new();

    private UiForm(object? clone) { Model = clone; }

    // Factory: Super important to clone the original value, don't trust the caller to get it right!
    public static UiForm Create<TModel>(string title, TModel original)
        => new(original!.ToJson()!.FromJson<TModel>()!) { Title = title };

    private IUiField Add(IUiField field) { Fields.Add(field); return field; }
    private IUiField Add<TModel, TValue>(UiField<TModel, TValue> field) { Fields.Add(field); return field; }

    public IUiField AddString<TModel>(string label, Func<TModel, string> get, Action<TModel, string> set)
        => Add(new UiField<TModel, string>(label, UiFieldKind.String, get, set,
               s => (true, s ?? "", null)));

    // Convenience overload when the form's Model is the same as the field type
    public IUiField AddString(string label)
        => AddString<string>(label,
            m => Model is string sv ? sv : (Model as object)?.ToString() ?? string.Empty,
            (m, v) => Model = v);

    public IUiField AddInt<TModel>(string label, Func<TModel, int> get, Action<TModel, int> set)
        => Add(new UiField<TModel, int>(label, UiFieldKind.Number, get, set,
               s => int.TryParse(s, out var v)
                        ? (true, v, null)
                        : (false, default, "Enter a whole number.")));

    // Convenience overload when the form's Model is int
    public IUiField AddInt(string label)
        => AddInt<int>(label,
            m => Model is int iv ? iv : throw new InvalidOperationException($"UiForm.AddInt() can only be used when Model is int."),
            (m, v) => Model = v);

    public IUiField AddFloat<TModel>(string label, Func<TModel, float> get, Action<TModel, float> set)
        => Add(new UiField<TModel, float>(label, UiFieldKind.Number, get, set,
               s => float.TryParse(s, out var v)
                        ? (true, v, null)
                        : (false, default, "Enter a valid floating-point number.")));

    // Convenience overload when Model is float
    public IUiField AddFloat(string label)
        => AddFloat<float>(label,
            m => Model is float fv ? fv : throw new InvalidOperationException($"UiForm.AddFloat() can only be used when Model is float."),
            (m, v) => Model = v);

    public IUiField AddDouble<TModel>(string label, Func<TModel, double> get, Action<TModel, double> set)
        => Add(new UiField<TModel, double>(label, UiFieldKind.Number, get, set,
               s => double.TryParse(s, out var v)
                        ? (true, v, null)
                        : (false, default, "Enter a valid double precision number.")));

    // Convenience overload when Model is double
    public IUiField AddDouble(string label)
        => AddDouble<double>(label,
            m => Model is double dv ? dv : throw new InvalidOperationException($"UiForm.AddDouble() can only be used when Model is double."),
            (m, v) => Model = v);

    public IUiField AddBool<TModel>(string label, Func<TModel, bool> get, Action<TModel, bool> set)
        => Add(new UiField<TModel, bool>(label, UiFieldKind.Bool, get, set,
               s => bool.TryParse(s, out var v)
                        ? (true, v, null)
                        : (false, default, "Enter true or false.")));

    // Convenience overload when Model is bool
    public IUiField AddBool(string label)
        => AddBool<bool>(label,
            m => Model is bool bv ? bv : throw new InvalidOperationException($"UiForm.AddBool() can only be used when Model is bool."),
            (m, v) => Model = v);

    public IUiField AddGuid<TModel>(string label, Func<TModel, Guid> get, Action<TModel, Guid> set)
        => Add(new UiField<TModel, Guid>(label, UiFieldKind.Guid, get, set,
               s => System.Guid.TryParse(s, out var v)
                        ? (true, v, null)
                        : (false, default, "Enter a valid GUID.")));

    // Convenience overload when Model is Guid
    public IUiField AddGuid(string label)
        => AddGuid<Guid>(label,
            m => Model is Guid gv ? gv : throw new InvalidOperationException($"UiForm.AddGuid() can only be used when Model is Guid."),
            (m, v) => Model = v);

    public IUiField AddEnum<TModel>(string label, Type enumType, Func<TModel, string> get, Action<TModel, string> set)
        => Add(new UiField<TModel, string>(label, UiFieldKind.Enum, get, set,
               s => (Enum.IsDefined(enumType, s ?? "") ? (true, s, null) : (false, default, $"Must be one of: {string.Join(", ", Enum.GetNames(enumType))}"))));

    // Convenience overload when Model is the enum type itself or a string to hold the enum name
    public IUiField AddEnum(string label, Type enumType)
        => AddEnum<string>(label, enumType,
            m => Model is string sv ? sv : Model is Enum e ? e.ToString() : throw new InvalidOperationException($"UiForm.AddEnum() requires Model to be either string or an enum type."),
            (m, v) =>
            {
                if (Model is string) Model = v;
                else if (Model is Enum)
                {
                    var enumValue = Enum.Parse(enumType, v ?? "");
                    Model = enumValue;
                }
                else throw new InvalidOperationException($"UiForm.AddEnum() requires Model to be either string or an enum type.");
            });
}

public interface IUi
{
    // high-level I/O
    Task<bool> ShowFormAsync(UiForm form);

    // input
    Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory);
    Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager);
    string? RenderMenu(string header, List<string> choices, int selected = 0);
    string? ReadLineWithHistory();
    string ReadLine();
    ConsoleKeyInfo ReadKey(bool intercept);

    // output
    void RenderChatMessage(ChatMessage message);
    void RenderChatHistory(IEnumerable<ChatMessage> messages);

    // low-level console-like I/O (to be removed)
    int CursorTop { get; }
    int CursorLeft { get; }
    int Width { get; }
    int Height { get; }
    bool CursorVisible { set; }
    bool KeyAvailable { get; }
    bool IsOutputRedirected { get; }
    void SetCursorPosition(int left, int top);
    ConsoleColor ForegroundColor { get; set; }
    ConsoleColor BackgroundColor { get; set; }
    void ResetColor();

    void Write(string text);
    void WriteLine(string? text = null);
    void Clear();

    // lets each UI decide how to run/pump itself
    Task RunAsync(Func<Task> appMain);
}