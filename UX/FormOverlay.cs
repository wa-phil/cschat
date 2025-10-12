using System;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;

/// <summary>
/// Kinds of fields supported in a form
/// </summary>
/// <typeparam name="TModel">type of the model backing the form</typeparam>
/// <typeparam name="TValue">type of the field value</typeparam>
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
    IUiField IUiField.MakeOptionalIf(bool cond) { if (cond) { Required = false; } return this; }
    IUiField IUiField.WithPathMode(PathPickerMode mode) { PathMode = mode; return this; }
}

/// <summary>
/// Array field for lists of simple element types (string/int/long/decimal/double/bool/guid/date/time)
/// </summary>
/// <typeparam name="TModel">Type of the model backing the array in the form</typeparam>
/// <typeparam name="TItem">Type of the items in the array</typeparam>
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

/// <summary>
/// FormOverlay renders an existing UiForm model as a modal overlay on top of the frame.
/// Adapts UiForm.Fields into UiNode inputs, collects values, runs TrySetFromString + Validate.
/// </summary>
public static class FormOverlay
{
    /// <summary>
    /// Renders an existing UiForm model as a modal overlay.
    /// The implementation adapts UiForm.Fields into UiNode inputs, collects values, runs TrySetFromString + Validate.
    /// </summary>
    public static UiNode Create(UiForm form)
    {
        if (form == null)
            throw new ArgumentNullException(nameof(form));

        var children = new List<UiNode>();

        // Title
        children.Add(new UiNode(
            "overlay-form-title",
            UiKind.Label,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.Text] = form.Title
            },
            Array.Empty<UiNode>(), UiStyles.Of((UiStyleKey.Bold, true), (UiStyleKey.Align, "center"))));

        // Iterate through fields and create row with label (left) + control (right)
        foreach (var field in form.Fields)
        {
            var fieldKey = $"overlay-form-field-{field.Key}";
            var currentText = field.Formatter(form.Model);

            var labelNode = new UiNode(
                $"{fieldKey}-label",
                UiKind.Label,
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = field.Label + (field.Required ? " *" : ""),
                },
                Array.Empty<UiNode>()
            );

            var inputNode = CreateFieldInput(fieldKey, field, currentText);

            // Compose the field line as a split row (50/50)
            children.Add(new UiNode(
                $"{fieldKey}-row",
                UiKind.Row,
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Layout] = "split-50-50"
                },
                new[] { labelNode, inputNode }
            ));

            // Help text if present
            if (!string.IsNullOrEmpty(field.Help))
            {
                children.Add(new UiNode(
                    $"{fieldKey}-help",
                    UiKind.Label,
                    new Dictionary<UiProperty, object?>
                    {
                        [UiProperty.Text] = field.Help
                    },
                    Array.Empty<UiNode>(), UiStyles.Of((UiStyleKey.Style, "dim"))));
            }

            // Error placeholder (initially empty)
            children.Add(new UiNode(
                $"{fieldKey}-error",
                UiKind.Label,
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = ""
                },
                Array.Empty<UiNode>(), UiStyles.Of((UiStyleKey.ForegroundColor, "Red")))
            );
        }

        // Button row
        children.Add(new UiNode(
            "overlay-form-buttons",
            UiKind.Row,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.Layout] = "split-50-50"
            },
            new[]
            {
                new UiNode(
                    "overlay-form-submit",
                    UiKind.Button,
                    new Dictionary<UiProperty, object?>
                    {
                        [UiProperty.Text] = "Submit",
                        [UiProperty.Focusable] = true
                    },
                    Array.Empty<UiNode>()
                ),
                new UiNode(
                    "overlay-form-cancel",
                    UiKind.Button,
                    new Dictionary<UiProperty, object?>
                    {
                        [UiProperty.Text] = "Cancel",
                        [UiProperty.Focusable] = true
                    },
                    Array.Empty<UiNode>()
                )
            }
        ));

        return new UiNode(
            "overlay-form",
            UiKind.Column,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.Modal] = true,
                [UiProperty.Role] = "overlay",
                [UiProperty.ZIndex] = 3000, // ensure forms sit above menu overlays
                [UiProperty.Width] = "80%",
                [UiProperty.Padding] = "2"
            },
            children
        );
    }

    /// <summary>
    /// Shows the form overlay and returns true if submitted successfully, false if cancelled
    /// </summary>
    public static async Task<bool> ShowAsync(IUi ui, UiForm form)
    {
        if (ui == null)
            throw new ArgumentNullException(nameof(ui));

        if (form == null)
            throw new ArgumentNullException(nameof(form));

        // Create the form overlay
        var formNode = Create(form);

        // Push overlay onto the frame
        await ui.PatchAsync(UiFrameBuilder.PushOverlay(formNode));

        // Build focus order: inputs in field order, then submit, then cancel
        var focusOrder = new List<string>();
        foreach (var f in form.Fields)
        {
            focusOrder.Add($"overlay-form-field-{f.Key}");
        }

        focusOrder.Add("overlay-form-submit");
        focusOrder.Add("overlay-form-cancel");

        int focusIndex = focusOrder.Count > 0 ? 0 : -1;
        if (focusIndex >= 0)
            await ui.FocusAsync(focusOrder[focusIndex]);

        // Helper: update visual focus state on each field row
        async Task UpdateRowFocusAsync()
        {
            var ops = new List<UiOp>();
            for (int i = 0; i < form.Fields.Count; i++)
            {
                var f = form.Fields[i];
                var rowKey = $"overlay-form-field-{f.Key}-row";
                bool focused = (i == focusIndex);
                ops.Add(new UpdatePropsOp(rowKey, new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Layout] = "split-50-50",
                    // 'focused' is a view concern; keep as string-key for now if needed
                    [UiProperty.State] = focused
                }));
            }
            if (ops.Count > 0)
                await ui.PatchAsync(new UiPatch(ops.ToArray()));
        }

        // Initial row highlight
        await UpdateRowFocusAsync();

        // Local state mirrors UI props as user types/navigates
        var values = new Dictionary<string, object?>(); // key -> value (string/bool/int)
        foreach (var f in form.Fields)
        {
            var key = $"overlay-form-field-{f.Key}";
            var text = f.Formatter(form.Model);
            switch (f.Kind)
            {
                case UiFieldKind.Bool:
                    // interpret non-empty/non-false as true
                    values[key] = bool.TryParse(text, out var b) ? b : (!string.IsNullOrEmpty(text) && text != "0" && !text.Equals("false", StringComparison.OrdinalIgnoreCase));
                    break;
                case UiFieldKind.Enum:
                    values[key] = text ?? string.Empty;
                    break;
                default:
                    values[key] = text ?? string.Empty;
                    break;
            }
        }

        // Helper: set error text for a field key
        async Task SetErrorAsync(string fieldKey, string? message)
        {
            var op = new UpdatePropsOp($"overlay-form-field-{fieldKey}-error", new Dictionary<UiProperty, object?>
            {
                [UiProperty.Text] = message ?? ""
            });
            await ui.PatchAsync(new UiPatch(op));
        }

        // Clear all errors initially
        var clearOps = new List<UiOp>();
        foreach (var f in form.Fields)
        {
            clearOps.Add(new UpdatePropsOp($"overlay-form-field-{f.Key}-error", new Dictionary<UiProperty, object?> { [UiProperty.Text] = "" }));
        }
        if (clearOps.Count > 0)
            await ui.PatchAsync(new UiPatch(clearOps.ToArray()));

        // Helper: update a field node's UI props from local 'values'
        async Task RefreshFieldAsync(IUiField f)
        {
            var fk = $"overlay-form-field-{f.Key}";
            var val = values.TryGetValue(fk, out var v) ? v : null;
            var props = new Dictionary<UiProperty, object?>();
            switch (f.Kind)
            {
                case UiFieldKind.Bool:
                    props[UiProperty.Checked] = (val as bool?) ?? false;
                    break;
                case UiFieldKind.Enum:
                    // keep items from Create; only update selectedIndex via value lookup
                    var items = f.EnumChoices()?.ToList() ?? new List<string>();
                    var s = (val as string) ?? "";
                    var idx = Math.Max(0, items.FindIndex(i => string.Equals(i, s, StringComparison.OrdinalIgnoreCase)));
                    props[UiProperty.SelectedIndex] = items.Count == 0 ? -1 : idx;
                    break;
                default:
                    props[UiProperty.Text] = val?.ToString() ?? "";
                    break;
            }
            await ui.PatchAsync(new UiPatch(new UpdatePropsOp(fk, props)));
        }

        // Initial refresh for accuracy (selectedIndex/checked)
        foreach (var f in form.Fields)
            await RefreshFieldAsync(f);

        // Input loop
        var router = ui.GetInputRouter();
        bool submitted = false;

        // Helper: move focus respecting Tab/Shift+Tab
        async Task MoveFocusAsync(int delta)
        {
            if (focusOrder.Count == 0) return;
            focusIndex = (focusIndex + delta) % focusOrder.Count;
            if (focusIndex < 0) focusIndex += focusOrder.Count;
            await ui.FocusAsync(focusOrder[focusIndex]);
            // Only highlight actual field rows; non-field items (buttons) won't match any row and will leave all rows unhighlighted
            await UpdateRowFocusAsync();
        }

        while (true)
        {
            var maybeKey = router.TryReadKey();
            if (maybeKey is null)
            {
                await Task.Delay(10);
                continue;
            }

            var key = maybeKey.Value;

            if (key.Key == ConsoleKey.Escape)
            {
                // cancel
                break;
            }

            // Tab navigation
            if (key.Key == ConsoleKey.Tab)
            {
                var isShift = (key.Modifiers & ConsoleModifiers.Shift) == ConsoleModifiers.Shift;
                await MoveFocusAsync(isShift ? -1 : 1);
                continue;
            }

            // Determine focused element
            var currentKey = (focusIndex >= 0 && focusIndex < focusOrder.Count) ? focusOrder[focusIndex] : null;
            if (string.IsNullOrEmpty(currentKey)) continue;

            // Buttons
            if (currentKey == "overlay-form-submit")
            {
                if (key.Key == ConsoleKey.Enter)
                {
                    submitted = true;
                    break;
                }
                if (key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow)
                {
                    // allow horizontal between buttons
                    await MoveFocusAsync(key.Key == ConsoleKey.RightArrow ? 1 : -1);
                    continue;
                }
            }
            if (currentKey == "overlay-form-cancel")
            {
                if (key.Key == ConsoleKey.Enter)
                {
                    // cancel via Enter on Cancel button
                    submitted = false; // explicit
                    break;
                }
                if (key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow)
                {
                    await MoveFocusAsync(key.Key == ConsoleKey.RightArrow ? 1 : -1);
                    continue;
                }
            }

            // Inputs
            var matchedField = form.Fields.FirstOrDefault(f => $"overlay-form-field-{f.Key}" == currentKey);
            if (matchedField != null)
            {
                var fk = currentKey;
                switch (matchedField.Kind)
                {
                    case UiFieldKind.Bool:
                        if (key.Key == ConsoleKey.Spacebar)
                        {
                            var cur = (values.TryGetValue(fk, out var v) && v is bool b && b);
                            values[fk] = !cur;
                            await RefreshFieldAsync(matchedField);
                        }
                        break;
                    case UiFieldKind.Enum:
                        var items = matchedField.EnumChoices()?.ToList() ?? new List<string>();
                        var s = (values.TryGetValue(fk, out var vv) ? vv?.ToString() : "") ?? "";
                        var idx = Math.Max(0, items.FindIndex(i => string.Equals(i, s, StringComparison.OrdinalIgnoreCase)));
                        int page = Math.Max(1, items.Count / 10);
                        if (key.Key == ConsoleKey.UpArrow) { idx = Math.Max(0, idx - 1); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshFieldAsync(matchedField); }
                        else if (key.Key == ConsoleKey.DownArrow) { idx = Math.Min(Math.Max(0, items.Count - 1), idx + 1); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshFieldAsync(matchedField); }
                        else if (key.Key == ConsoleKey.PageUp) { idx = Math.Max(0, idx - page); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshFieldAsync(matchedField); }
                        else if (key.Key == ConsoleKey.PageDown) { idx = Math.Min(Math.Max(0, items.Count - 1), idx + page); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshFieldAsync(matchedField); }
                        else if (key.Key == ConsoleKey.Home) { idx = 0; values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshFieldAsync(matchedField); }
                        else if (key.Key == ConsoleKey.End) { idx = Math.Max(0, items.Count - 1); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshFieldAsync(matchedField); }
                        else if (key.Key == ConsoleKey.Enter) { /* keep selection; move to next */ await MoveFocusAsync(1); }
                        break;
                    default:
                        // text-like inputs
                        if (!char.IsControl(key.KeyChar))
                        {
                            var curText = (values.TryGetValue(fk, out var tv) ? tv?.ToString() : "") ?? "";
                            curText += key.KeyChar;
                            values[fk] = curText;
                            await ui.PatchAsync(new UiPatch(new UpdatePropsOp(fk, new Dictionary<UiProperty, object?> { [UiProperty.Text] = curText })));
                        }
                        else if (key.Key == ConsoleKey.Backspace)
                        {
                            var curText = (values.TryGetValue(fk, out var tv) ? tv?.ToString() : "") ?? "";
                            if (curText.Length > 0)
                            {
                                curText = curText.Substring(0, curText.Length - 1);
                                values[fk] = curText;
                                await ui.PatchAsync(new UiPatch(new UpdatePropsOp(fk, new Dictionary<UiProperty, object?> { [UiProperty.Text] = curText })));
                            }
                        }
                        else if (key.Key == ConsoleKey.Enter)
                        {
                            // move to next control on Enter
                            await MoveFocusAsync(1);
                        }
                        break;
                }
                continue;
            }
        }

        bool result;
        if (submitted)
        {
            // Clear errors
            var ops = new List<UiOp>();
            foreach (var f in form.Fields)
                ops.Add(new UpdatePropsOp($"overlay-form-field-{f.Key}-error", new Dictionary<UiProperty, object?> { [UiProperty.Text] = "" }));
            if (ops.Count > 0) await ui.PatchAsync(new UiPatch(ops.ToArray()));

            bool allOk = true;
            // Apply per-field
            foreach (var f in form.Fields)
            {
                var fk = $"{f.Key}"; // key used for SetErrorAsync suffix
                var uiKey = $"overlay-form-field-{f.Key}";
                values.TryGetValue(uiKey, out var rawVal);
                string? rawString;
                if (f.Kind == UiFieldKind.Bool)
                    rawString = ((rawVal as bool?) ?? false) ? "true" : "false";
                else
                    rawString = rawVal?.ToString();

                if (!f.TrySetFromString(form.Model!, rawString, out var err))
                {
                    allOk = false;
                    await SetErrorAsync(fk, err);
                }
            }

            // Form-level validation
            if (allOk && form.Validate is not null)
            {
                foreach (var (k, message) in form.Validate(form.Model!) ?? Array.Empty<(string, string)>())
                {
                    allOk = false;
                    await SetErrorAsync(k, message);
                }
            }

            result = allOk;
        }
        else
        {
            result = false; // cancelled
        }

        // Pop overlay
        await ui.PatchAsync(UiFrameBuilder.PopOverlay("overlay-form"));
        return result;
    }

    private static UiNode CreateFieldInput(string fieldKey, IUiField field, string currentText)
    {
        var props = new Dictionary<UiProperty, object?>
        {
            [UiProperty.Focusable] = true
        };

        // Add placeholder if present
        if (!string.IsNullOrEmpty(field.Placeholder))
            props[UiProperty.Placeholder] = field.Placeholder;

        // Set initial value from model (if available)
        // Note: We'd need the actual model instance here; for now, leave empty
        // In a real implementation, FormOverlay.Create would need access to the model
        // or we'd set values after mounting through event handlers

        UiKind kind = field.Kind switch
        {
            UiFieldKind.String => UiKind.TextBox,
            UiFieldKind.Text => UiKind.TextArea,
            UiFieldKind.Path => UiKind.TextBox, // With file picker button
            UiFieldKind.Enum => UiKind.ListView, // Dropdown/select
            UiFieldKind.Bool => UiKind.CheckBox,
            UiFieldKind.Number => UiKind.TextBox, // With number constraints
            UiFieldKind.Password => UiKind.TextBox, // Password field
            _ => UiKind.TextBox
        };

        // Add choices for enum fields
        if (field.Kind == UiFieldKind.Enum)
        {
            var choices = field.EnumChoices();
            if (choices != null)
            {
                var list = choices.ToList();
                props[UiProperty.Items] = list;
                var sel = Math.Max(0, list.FindIndex(i => string.Equals(i, currentText ?? "", StringComparison.OrdinalIgnoreCase)));
                props[UiProperty.SelectedIndex] = list.Count == 0 ? -1 : sel;
            }
        }

        // Add constraints for number fields
        if (field.Kind == UiFieldKind.Number)
        {
            var min = field.MinInt();
            var max = field.MaxInt();
            if (min.HasValue)
                props[UiProperty.Min] = min.Value;
            if (max.HasValue)
                props[UiProperty.Max] = max.Value;
        }

        // Initialize value props based on kind
        switch (field.Kind)
        {
            case UiFieldKind.Bool:
                props[UiProperty.Checked] = bool.TryParse(currentText, out var b) ? b : (!string.IsNullOrEmpty(currentText) && currentText != "0" && !currentText.Equals("false", StringComparison.OrdinalIgnoreCase));
                break;
            case UiFieldKind.Enum:
                // handled above
                break;
            default:
                props[UiProperty.Text] = currentText;
                break;
        }

        return new UiNode(
            fieldKey,
            kind,
            props,
            Array.Empty<UiNode>()
        );
    }
}