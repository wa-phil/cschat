using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;

public sealed class UiField<TModel, TValue> : IUiField
{
    public string Key { get; }     // stable id for roundtrips
    public string Label { get; }
    public UiFieldKind Kind { get; }
    public bool Required { get; set; } = true;
    public string? Help { get; set; }
    public string? Placeholder { get; private set; }
    public string? Pattern { get; private set; }
    public string? PatternMessage { get; private set; }
    public PathPickerMode PathMode { get; private set; } = PathPickerMode.OpenExisting;

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
        string key,
        string label,
        UiFieldKind kind,
        Func<TModel, TValue> get,
        Action<TModel, TValue> set,
        Func<string?, (bool ok, TValue? value, string? error)> tryParse)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Label = label;
        Kind = kind;
        _get = get;
        _set = set;
        _tryParse = tryParse;
        _format = v => v?.ToString() ?? "";

        var t = typeof(TValue);
        if (UiFieldKind.Enum == kind && t.IsEnum)
        {
            // Only populate automatic enum choices when TValue is actually an enum type.
            // AddChoice<TModel>() uses UiFieldKind.Enum but without this, TValue=string would otherwise cause an exception.
            try
            {
                var values = Enum.GetValues(t);
                var list = new List<string>();
                foreach (var v in values) list.Add(v?.ToString() ?? "");
                Choices = list;
            }
            catch { /* swallow: non-critical */ }
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

            var (ok, parsed, perr) = _tryParse(value);
            if (!ok)
            {
                ctx.Append(Log.Data.Message, perr ?? "Invalid value.");
                return (false, perr ?? "Invalid value.");
            }

            if (Kind == UiFieldKind.Number)
            {
                if (parsed is int n)
                {
                    if (Min is int min && n < min) return (false, $"Int must be ≥ {min}.");
                    if (Max is int max && n > max) return (false, $"Int must be ≤ {max}.");
                }
                else if (parsed is double d)
                {
                    if (Min is int min && d < min) return (false, $"Double must be ≥ {min}.");
                    if (Max is int max && d > max) return (false, $"Double must be ≤ {max}.");
                }
                else if (parsed is float f)
                {
                    if (Min is int min && f < min) return (false, $"Float must be ≥ {min}.");
                    if (Max is int max && f > max) return (false, $"Float must be ≤ {max}.");
                }
            }

            _set(model, parsed!);
            ctx.Succeeded();
            return (true, string.Empty);
        });

        error = string.IsNullOrEmpty(err) ? null : err;
        return result;
    }

    bool IUiField.TrySetFromString(object model, string? value, out string? error)
        => TrySetFromString((TModel)model!, value, out error);

    // generic fluent sugar
    IUiField IUiField.WithHelp(string? help) { Help = help; return this; }
    IUiField IUiField.MakeOptional() { Required = false; return this; }
    IUiField IUiField.IntBounds(int? min, int? max) { Min = min; Max = max; return this; }
    IUiField IUiField.FormatWith(Func<object?, string> format) { _format = v => format(v); return this; }
    IUiField IUiField.WithPlaceholder(string? ph) { Placeholder = ph; return this; }
    IUiField IUiField.WithRegex(string p, string? m) { Pattern = p; PatternMessage = m; return this; }
    IUiField IUiField.MakeOptionalIf(bool cond) { if (cond) {Required = false;} return this; }
    IUiField IUiField.WithPathMode(PathPickerMode mode) { PathMode = mode; return this; }
}

// Array field for lists of simple element types (string/int/long/decimal/double/bool/guid/date/time)
public sealed class UiArrayField<TModel, TItem> : IUiField
{
    public string Key { get; }
    public string Label { get; }
    public UiFieldKind Kind => UiFieldKind.Array;
    public bool Required { get; private set; } = true;
    public string? Help { get; private set; }
    public string? Placeholder { get; private set; }
    public string? Pattern => null;
    public string? PatternMessage => null;
    public PathPickerMode PathMode { get; private set; } = PathPickerMode.OpenExisting;

    private readonly Func<TModel, IList<TItem>> _get;
    private readonly Action<TModel, IList<TItem>> _set;

    public UiArrayField(string key, string label, Func<TModel, IList<TItem>> get, Action<TModel, IList<TItem>> set)
    {
        Key = key; Label = label; _get = get; _set = set;
    }

    public Func<object?, string> Formatter => model =>
    {
        var list = _get((TModel)model!);
        try { return (list ?? new List<TItem>()).ToJson(); } catch { return "[]"; }
    };

    public IEnumerable<string>? EnumChoices() => null;
    public int? MinInt() => null;
    public int? MaxInt() => null;

    public bool TrySetFromString(object model, string? value, out string? error)
    {
        // value is JSON array text from the page
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (Required) { error = "At least one item is required."; return false; }
                _set((TModel)model!, new List<TItem>());
                error = null; return true;
            }
            var parsed = (value!).FromJson<List<TItem>>() ?? new List<TItem>();
            _set((TModel)model!, parsed);
            error = null; return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid list: {ex.Message}";
            return false;
        }
    }

    // no-ops for array
    public IUiField WithHelp(string? help) { Help = help; return this; }
    public IUiField IntBounds(int? min = null, int? max = null) => this;
    public IUiField FormatWith(Func<object?, string> format) => this;
    public IUiField MakeOptional() { Required = false; return this; }
    public IUiField WithPlaceholder(string? placeholder) { Placeholder = placeholder; return this; }
    public IUiField WithRegex(string pattern, string? message = null) => this;
    public IUiField MakeOptionalIf(bool cond) => cond ? MakeOptional() : this;
    public IUiField WithPathMode(PathPickerMode mode) { PathMode = mode; return this; }
}

public sealed class UiForm
{
    public string Title { get; init; } = "Input";
    public object? Model { get; set; }
    public List<IUiField> Fields { get; } = new();

    // OPTIONAL: submit-time validation hook (emit (key,message) for per-field errors)
    public Func<object, IEnumerable<(string key, string message)>>? Validate { get; set; }

    private UiForm(object? clone) { Model = clone; }

    public static UiForm Create<TModel>(string title, TModel original)
        => new(original!.ToJson()!.FromJson<TModel>()!) { Title = title };  // :contentReference[oaicite:0]{index=0}

    private IUiField Add(IUiField field) { Fields.Add(field); return field; }
    private IUiField Add<TModel, TValue>(
        string label, UiFieldKind kind,
        Func<TModel, TValue> get, Action<TModel, TValue> set,
        Func<string?, (bool ok, TValue? value, string? error)> tryParse, string? key = null)
        => Add(new UiField<TModel, TValue>(key ?? Fields.Count.ToString(), label, kind, get, set, tryParse));

    public IUiField AddString<TModel>(string label, Func<TModel, string> get, Action<TModel, string> set, string? key = null)
        => Add(new UiField<TModel, string>(key ?? Fields.Count.ToString(), label, UiFieldKind.String, get, set, s => (true, s ?? "", null)));
    public IUiField AddString(string label, string? key = null)
        => AddString<string>(label, m => Model is string sv ? sv : (Model as object)?.ToString() ?? string.Empty, (m, v) => Model = v, key);

    public IUiField AddPassword<TModel>(string label, Func<TModel, string> get, Action<TModel, string> set, string? key = null) // NEW
        => Add(new UiField<TModel, string>(key ?? Fields.Count.ToString(), label, UiFieldKind.Password, get, set, s => (true, s ?? "", null)));

    public IUiField AddInt<TModel>(string label, Func<TModel, int> get, Action<TModel, int> set, string? key = null)
        => Add(new UiField<TModel, int>(key ?? Fields.Count.ToString(), label, UiFieldKind.Number, get, set,
               s => int.TryParse(s, out var v) ? (true, v, null) : (false, default, "Enter a whole number.")));
    public IUiField AddInt(string label, string? key = null)
        => AddInt<int>(label, m => Model is int iv ? iv : throw new InvalidOperationException("UiForm.AddInt requires int Model."), (m, v) => Model = v, key);

    public IUiField AddFloat<TModel>(string label, Func<TModel, float> get, Action<TModel, float> set, string? key = null)
        => Add(new UiField<TModel, float>(key ?? Fields.Count.ToString(), label, UiFieldKind.Decimal, get, set,
               s => float.TryParse(s, out var v) ? (true, v, null) : (false, default, "Enter a valid floating-point number.")));
    public IUiField AddFloat(string label, string? key = null)
        => AddFloat<float>(label, m => Model is float fv ? fv : throw new InvalidOperationException("UiForm.AddFloat requires float Model."), (m, v) => Model = v, key);

    public IUiField AddDouble<TModel>(string label, Func<TModel, double> get, Action<TModel, double> set, string? key = null)
        => Add(new UiField<TModel, double>(key ?? Fields.Count.ToString(), label, UiFieldKind.Decimal, get, set,
               s => double.TryParse(s, out var v) ? (true, v, null) : (false, default, "Enter a valid double precision number.")));
    public IUiField AddDouble(string label, string? key = null)
        => AddDouble<double>(label, m => Model is double dv ? dv : throw new InvalidOperationException("UiForm.AddDouble requires double Model."), (m, v) => Model = v, key);

    public IUiField AddBool<TModel>(string label, Func<TModel, bool> get, Action<TModel, bool> set, string? key = null)
        => Add(new UiField<TModel, bool>(key ?? Fields.Count.ToString(), label, UiFieldKind.Bool, get, set,
               s => bool.TryParse(s, out var v) ? (true, v, null) : (false, default, "Enter true or false.")));
    public IUiField AddBool(string label, string? key = null)
        => AddBool<bool>(label, m => Model is bool bv ? bv : throw new InvalidOperationException("UiForm.AddBool requires bool Model."), (m, v) => Model = v, key);

    public IUiField AddGuid<TModel>(string label, Func<TModel, Guid> get, Action<TModel, Guid> set, string? key = null)
        => Add(new UiField<TModel, Guid>(key ?? Fields.Count.ToString(), label, UiFieldKind.Guid, get, set,
               s => Guid.TryParse(s, out var v) ? (true, v, null) : (false, default, "Enter a valid GUID.")));
    public IUiField AddGuid(string label, string? key = null)
        => AddGuid<Guid>(label, m => Model is Guid gv ? gv : throw new InvalidOperationException("UiForm.AddGuid requires Guid Model."), (m, v) => Model = v, key);

    public IUiField AddEnum<TModel>(string label, Type enumType, Func<TModel, string> get, Action<TModel, string> set, string? key = null)
        => Add(new UiField<TModel, string>(key ?? Fields.Count.ToString(), label, UiFieldKind.Enum, get, set,
               s => (Enum.IsDefined(enumType, s ?? "") ? (true, s, null) : (false, default, $"Must be one of: {string.Join(", ", Enum.GetNames(enumType))}"))));
    public IUiField AddEnum<TModel, TEnum>(string label, Func<TModel, TEnum> get, Action<TModel, TEnum> set, string? key = null)
        where TEnum : struct, Enum
        => Add(label, UiFieldKind.Enum, get, set, s => Enum.TryParse<TEnum>(s, true, out var v) ? (true, v, null) : (false, default, $"Choose one of {string.Join(", ", Enum.GetNames(typeof(TEnum)))}."), key);

    public IUiField AddText<TModel>(string label, Func<TModel, string> get, Action<TModel, string> set, string? key = null)
        => Add(label, UiFieldKind.Text, get, set, s => (true, s ?? "", null), key);

    // Add a dropdown/choice field for string values
    public IUiField AddChoice<TModel>(string label, IReadOnlyList<string> choices, Func<TModel, string> get, Action<TModel, string> set, string? key = null)
    {
        var f = new UiField<TModel, string>(key ?? Fields.Count.ToString(), label, UiFieldKind.Enum, get, set,
            s => (choices.Contains(s ?? "") ? (true, s ?? "", null) : (false, default, $"Choose one of: {string.Join(", ", choices)}")));
        // Ensure choices are available to the UI for non-enum (string) dropdowns
        f.Choices = choices.ToList();
        return Add(f);
    }

    public IUiField AddChoice(string label, IReadOnlyList<string> choices, string? key = null)
        => AddChoice<string>(label, choices, m => Model is string sv ? sv : (Model as object)?.ToString() ?? string.Empty, (m, v) => Model = v, key);

    public IUiField AddDecimal<TModel>(string label, Func<TModel, decimal> get, Action<TModel, decimal> set, string? key = null)
        => Add(label, UiFieldKind.Decimal, get, set, s => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? (true, v, null) : (false, default, "Enter a decimal number."), key);

    public IUiField AddLong<TModel>(string label, Func<TModel, long> get, Action<TModel, long> set, string? key = null)
        => Add(label, UiFieldKind.Long, get, set, s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? (true, v, null) : (false, default, "Enter a whole number."), key);

    public IUiField AddDate<TModel>(string label, Func<TModel, DateTime> get, Action<TModel, DateTime> set, string? key = null)
        => Add(label, UiFieldKind.Date, get, set, s => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt) ? (true, dt.Date, null) : (false, default, "Enter a date (YYYY-MM-DD)."), key);

    public IUiField AddTime<TModel>(string label, Func<TModel, TimeSpan> get, Action<TModel, TimeSpan> set, string? key = null)
        => Add(label, UiFieldKind.Time, get, set, s => TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts) ? (true, ts, null) : (false, default, "Enter a time (HH:MM)."), key);

    public IUiField AddPath<TModel>(string label, Func<TModel, string> get, Action<TModel, string> set, string? key = null)
        => Add(label, UiFieldKind.Path, get, set, s => (true, s ?? "", null), key);

    // simple-list array field
    public IUiField AddList<TModel, TItem>(string label, Func<TModel, IList<TItem>> get, Action<TModel, IList<TItem>> set, string? key = null)
        => Add(new UiArrayField<TModel, TItem>(key ?? label, label, get, set));

}

public interface IUi
{
    // high-level I/O
    Task<bool> ShowFormAsync(UiForm form);
    // simple yes/no confirmation (true=yes, false=no). Blank input chooses defaultAnswer.
    Task<bool> ConfirmAsync(string question, bool defaultAnswer = false);
    // launches current platform file picker with given options, returns empty list if cancelled
    Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt);

    // progress reporting
    string StartProgress(string title, CancellationTokenSource cts);
    void UpdateProgress(string id, ProgressSnapshot snapshot);
    void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown);

    // input
    Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory);
    Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager);
    string? RenderMenu(string header, List<string> choices, int selected = 0);
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