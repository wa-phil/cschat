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
        children.Add(
            Ui.Text("overlay-form-title", form.Title)
              .WithStyles(Style.Combine(Style.Bold, Style.AlignCenter))
        );

        var inputItems = new List<UiNode> { };

        // Iterate through fields and create row with label (left) + control (right)
        foreach (var field in form.Fields)
        {
            var fieldKey = $"overlay-form-field-{field.Key}";
            var currentText = field.Formatter(form.Model);

            var labelNode = Ui.Text($"{fieldKey}-label", field.Label + (field.Required ? " *" : ""));

            // Create input node for the current field.
            UiNode inputNode = CreateFieldInput(fieldKey, field, currentText);

            if (field.Kind == UiFieldKind.Array)
            {
                // For arrays, render a label row and then a separate content row aligned under the right column.
                inputItems.Add(
                    Ui.Row($"{fieldKey}-labelrow",
                        labelNode,
                        Ui.Text($"{fieldKey}-labelrow-placeholder", "")
                    ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)) })
                );

                // Array content row: left spacer, right the multi-line control
                inputItems.Add(
                    Ui.Row($"{fieldKey}-arrayrow",
                        Ui.Spacer($"{fieldKey}-arrayrow-spacer"),
                        inputNode
                    ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)) })
                );
            }
            else
            {
                // Compose the field line as a single grid row (20% / 80%)
                inputItems.Add(
                    Ui.Row($"{fieldKey}-row", labelNode, inputNode)
                      .WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)) })
                );
            }

            // Help text if present
            if (!string.IsNullOrEmpty(field.Help))
            {
                if (field.Kind == UiFieldKind.Array)
                {
                    // Align help under right column for arrays
                    inputItems.Add(
                        Ui.Row($"{fieldKey}-help-row",
                            Ui.Spacer($"{fieldKey}-help-row-spacer"),
                            Ui.Text($"{fieldKey}-help", field.Help).WithStyles(Style.Tag("dim"))
                        ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)) })
                    );
                }
                else
                {
                    inputItems.Add(
                        Ui.Text($"{fieldKey}-help", field.Help).WithStyles(Style.Tag("dim"))
                    );
                }
            }

            // Error placeholder (initially empty)
            if (field.Kind == UiFieldKind.Array)
            {
                inputItems.Add(
                    Ui.Row($"{fieldKey}-error-row",
                        Ui.Spacer($"{fieldKey}-error-row-spacer"),
                        Ui.Text($"{fieldKey}-error", "").WithStyles(Style.Color(ConsoleColor.Red))
                    ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)) })
                );
            }
            else
            {
                inputItems.Add(
                    Ui.Text($"{fieldKey}-error", "").WithStyles(Style.Color(ConsoleColor.Red))
                );
            }
        }

        // stack input items in a scrollable column (sticky header/footer is achieved by making only this body scrollable)
        var inputItemColumn = Ui.Column("overlay-form-input-items", inputItems.ToArray())
            .WithProps(new { AutoScroll = true, Scrollable = true });
        children.Add(inputItemColumn);

        // Button row (grid 1fr 1fr)
        children.Add(
            Ui.Row("overlay-form-buttons",
                Ui.Button("overlay-form-submit", "Submit").WithProps(new { Focusable = true }),
                Ui.Button("overlay-form-cancel", "Cancel").WithProps(new { Focusable = true })
            ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Fr(1), GridColumnSpec.Fr(1)) })
        );

        return Ui.Column("overlay-form", children.ToArray())
            .WithProps(new { Modal = true, Role = "overlay", ZIndex = 3000, Width = "80%", Padding = "2" });
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

        // Local state mirrors UI props as user types/navigates
        var values = new Dictionary<string, object?>(); // key -> value (string/bool/json)
        var errors = new Dictionary<string, string?>(); // fieldKey -> error text

        // Build focus order dynamically based on current values (for arrays)
        var focusOrder = new List<string>();
        void RebuildFocusOrder(Dictionary<String, object?> currentValues)
        {
            focusOrder.Clear();
            foreach (var f in form.Fields)
            {
                var baseKey = $"overlay-form-field-{f.Key}";
                if (f.Kind == UiFieldKind.Array)
                {
                    var json = (currentValues.TryGetValue(baseKey, out var v) ? v?.ToString() : "[]") ?? "[]";
                    List<string> arr;
                    try { arr = json.FromJson<List<string>>() ?? new List<string>(); } catch { arr = new List<string>(); }
                    for (int i = 0; i < arr.Count; i++)
                    {
                        focusOrder.Add($"{baseKey}-item-{i}-edit");
                        focusOrder.Add($"{baseKey}-item-{i}-delete");
                    }
                    focusOrder.Add($"{baseKey}-add");
                }
                else
                {
                    focusOrder.Add(baseKey);
                }
            }
            focusOrder.Add("overlay-form-submit");
            focusOrder.Add("overlay-form-cancel");
        }

        // Initialize values from model
        foreach (var f in form.Fields)
        {
            var key = $"overlay-form-field-{f.Key}";
            var text = f.Formatter(form.Model);
            switch (f.Kind)
            {
                case UiFieldKind.Bool:
                    values[key] = bool.TryParse(text, out var b) ? b : (!string.IsNullOrEmpty(text) && text != "0" && !text.Equals("false", StringComparison.OrdinalIgnoreCase));
                    break;
                case UiFieldKind.Enum:
                    values[key] = text ?? string.Empty;
                    break;
                default:
                    values[key] = text ?? string.Empty;
                    break;
            }
            errors[f.Key] = ""; // seed error map
        }

        RebuildFocusOrder(values);
        int focusIndex = focusOrder.Count > 0 ? 0 : -1;
        string? currentFocusKey = focusIndex >= 0 ? focusOrder[focusIndex] : null;
        int bodyScroll = 0;

        // Initial render + push via reconciler
        var prevNode = RenderOverlay(form, values, errors, currentFocusKey, bodyScroll);
        await ui.PatchAsync(UiFrameBuilder.PushOverlay(prevNode));
        if (!string.IsNullOrEmpty(currentFocusKey)) await ui.FocusAsync(currentFocusKey!);

        // Helper: re-render overlay and reconcile
        async Task RefreshOverlayAsync()
        {
            var next = RenderOverlay(form, values, errors, currentFocusKey, bodyScroll);
            await ui.ReconcileAsync(prevNode, next);
            prevNode = next;
        }

        // Input loop
        var router = ui.GetInputRouter();
        bool submitted = false;

        // offset from bottom, like ChatSurface (already initialized)

        // Helper: move focus respecting Tab/Shift+Tab
        async Task MoveFocusAsync(int delta)
        {
            if (focusOrder.Count == 0) return;
            // Resync numeric index with the key we believe is focused
            if (!string.IsNullOrEmpty(currentFocusKey))
            {
                var curIx = focusOrder.IndexOf(currentFocusKey);
                if (curIx >= 0) focusIndex = curIx;
            }
            focusIndex = (focusIndex + delta) % focusOrder.Count;
            if (focusIndex < 0) focusIndex += focusOrder.Count;
            currentFocusKey = focusOrder[focusIndex];
            await ui.FocusAsync(currentFocusKey);
            // Keep bottom-aligned when navigating by Tab
            bodyScroll = 0;
            await RefreshOverlayAsync();
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

            // Manual scroll keys affecting the form body (like ChatSurface)
            int page = Math.Max(3, (int)Math.Round(ui.Height * 0.7));
            bool scrolled = false;
            if (key.Key == ConsoleKey.UpArrow) { bodyScroll = Math.Max(0, bodyScroll - 1); scrolled = true; }
            else if (key.Key == ConsoleKey.DownArrow) { bodyScroll = Math.Max(0, bodyScroll + 1); scrolled = true; }
            else if (key.Key == ConsoleKey.PageUp) { bodyScroll = Math.Max(0, bodyScroll - page); scrolled = true; }
            else if (key.Key == ConsoleKey.PageDown) { bodyScroll = Math.Max(0, bodyScroll + page); scrolled = true; }
            else if (key.Key == ConsoleKey.Home) { bodyScroll = 0; scrolled = true; }
            else if (key.Key == ConsoleKey.End) { bodyScroll = int.MaxValue; scrolled = true; }

            if (scrolled)
            {
                await RefreshOverlayAsync();
                continue;
            }

            // Determine focused element
            var currentKey = currentFocusKey;
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
            var matchedField = form.Fields.FirstOrDefault(f => $"overlay-form-field-{f.Key}" == currentKey || currentKey.StartsWith($"overlay-form-field-{f.Key}-"));
            if (matchedField != null)
            {
                var fk = $"overlay-form-field-{matchedField.Key}";
                switch (matchedField.Kind)
                {
                    case UiFieldKind.Bool:
                        if (key.Key == ConsoleKey.Spacebar)
                        {
                            var cur = (values.TryGetValue(fk, out var v) && v is bool b && b);
                            values[fk] = !cur;
                            await RefreshOverlayAsync();
                        }
                        break;
                    case UiFieldKind.Enum:
                        var items = matchedField.EnumChoices()?.ToList() ?? new List<string>();
                        var s = (values.TryGetValue(fk, out var vv) ? vv?.ToString() : "") ?? "";
                        var idx = Math.Max(0, items.FindIndex(i => string.Equals(i, s, StringComparison.OrdinalIgnoreCase)));
                        int enumPage = Math.Max(1, items.Count / 10);
                        if (key.Key == ConsoleKey.UpArrow) { idx = Math.Max(0, idx - 1); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshOverlayAsync(); }
                        else if (key.Key == ConsoleKey.DownArrow) { idx = Math.Min(Math.Max(0, items.Count - 1), idx + 1); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshOverlayAsync(); }
                        else if (key.Key == ConsoleKey.PageUp) { idx = Math.Max(0, idx - enumPage); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshOverlayAsync(); }
                        else if (key.Key == ConsoleKey.PageDown) { idx = Math.Min(Math.Max(0, items.Count - 1), idx + enumPage); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshOverlayAsync(); }
                        else if (key.Key == ConsoleKey.Home) { idx = 0; values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshOverlayAsync(); }
                        else if (key.Key == ConsoleKey.End) { idx = Math.Max(0, items.Count - 1); values[fk] = items.Count > 0 ? items[idx] : ""; await RefreshOverlayAsync(); }
                        else if (key.Key == ConsoleKey.Enter)
                        {
                            // Open an in-place menu overlay to pick from items
                            if (items.Count > 0)
                            {
                                var selectedIndex = Math.Max(0, items.FindIndex(i => string.Equals(i, s, StringComparison.OrdinalIgnoreCase)));
                                var picked = await MenuOverlay.ShowAsync(ui, matchedField.Label, items, selectedIndex);
                                if (!string.IsNullOrEmpty(picked))
                                {
                                    values[fk] = picked;
                                    await RefreshOverlayAsync();
                                }
                            }
                            // keep focus on the field after selecting from overlay
                        }
                        break;
                    case UiFieldKind.Array:
                        // Array controls are per-item edit/delete buttons and an Add button; handle via key matching
                        if (key.Key != ConsoleKey.Enter) break;
                        var json = (values.TryGetValue(fk, out var lv0) ? lv0?.ToString() : "[]") ?? "[]";
                        var arrNow = json.FromJson<List<string>>() ?? new List<string>();
                        if (currentKey!.EndsWith("-add"))
                        {
                            var added = await InputOverlay.ShowAsync(ui, $"Add to {matchedField.Label}", initial: string.Empty, placeholder: matchedField.Placeholder);
                            if (!string.IsNullOrEmpty(added))
                            {
                                arrNow.Add(added);
                                values[fk] = arrNow.ToJson();
                                RebuildFocusOrder(values);
                                await RefreshOverlayAsync();
                            }
                            break;
                        }
                        else if (currentKey!.Contains("-item-"))
                        {
                            // parse index
                            var parts = currentKey.Split('-');
                            var idxPart = Array.IndexOf(parts, "item");
                            int itemIndex = -1;
                            if (idxPart >= 0 && idxPart + 1 < parts.Length)
                                int.TryParse(parts[idxPart + 1], out itemIndex);

                            if (itemIndex >= 0 && itemIndex < arrNow.Count)
                            {
                                if (currentKey.EndsWith("-edit"))
                                {
                                    var edited = await InputOverlay.ShowAsync(ui, $"Edit {matchedField.Label}", arrNow[itemIndex], matchedField.Placeholder);
                                    if (edited != null)
                                    {
                                        arrNow[itemIndex] = edited;
                                        values[fk] = arrNow.ToJson();
                                        RebuildFocusOrder(values);
                                        await RefreshOverlayAsync();
                                    }
                                }
                                else if (currentKey.EndsWith("-delete"))
                                {
                                    arrNow.RemoveAt(itemIndex);
                                    values[fk] = arrNow.ToJson();
                                    RebuildFocusOrder(values);
                                    await RefreshOverlayAsync();
                                }
                            }
                            break;
                        }
                        break;
                    default:
                        // text-like inputs
                        if (!char.IsControl(key.KeyChar))
                        {
                            var curText = (values.TryGetValue(fk, out var tv) ? tv?.ToString() : "") ?? "";
                            curText += key.KeyChar;
                            values[fk] = curText;
                            await RefreshOverlayAsync();
                        }
                        else if (key.Key == ConsoleKey.Backspace)
                        {
                            var curText = (values.TryGetValue(fk, out var tv) ? tv?.ToString() : "") ?? "";
                            if (curText.Length > 0)
                            {
                                curText = curText.Substring(0, curText.Length - 1);
                                values[fk] = curText;
                                await RefreshOverlayAsync();
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

            // No additional array action handling here; per-item buttons handled above
        }

        bool result;
        if (submitted)
        {
            // Clear errors
            foreach (var f in form.Fields) errors[f.Key] = "";

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
                    errors[fk] = err ?? "";
                }
            }

            // Form-level validation
            if (allOk && form.Validate is not null)
            {
                foreach (var (k, message) in form.Validate(form.Model!) ?? Array.Empty<(string, string)>())
                {
                    allOk = false;
                    errors[k] = message;
                }
            }

            await RefreshOverlayAsync();

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

    // Render the full overlay from current state (values, errors, focus, scroll)
    private static UiNode RenderOverlay(UiForm form, Dictionary<string, object?> values, Dictionary<string, string?> errors, string? currentFocusKey, int bodyScroll)
    {
        var children = new List<UiNode>();

        children.Add(
            Ui.Text("overlay-form-title", form.Title)
              .WithStyles(Style.Combine(Style.Bold, Style.AlignCenter))
        );

        var inputItems = new List<UiNode>();
        foreach (var field in form.Fields)
        {
            var fieldKey = $"overlay-form-field-{field.Key}";
            var focused = !string.IsNullOrEmpty(currentFocusKey) && currentFocusKey!.StartsWith(fieldKey);
            var currentVal = values.TryGetValue(fieldKey, out var vv) ? vv : null;
            var currentText = field.Kind == UiFieldKind.Bool ? ((currentVal as bool?) ?? false ? "true" : "false") : currentVal?.ToString() ?? string.Empty;

            var labelNode = Ui.Text($"{fieldKey}-label", field.Label + (field.Required ? " *" : ""));

            UiNode inputNode = CreateFieldInput(fieldKey, field, currentText);

            if (field.Kind == UiFieldKind.Array)
            {
                inputItems.Add(
                    Ui.Row($"{fieldKey}-labelrow",
                        labelNode,
                        Ui.Text($"{fieldKey}-labelrow-placeholder", "")
                    ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)), State = focused })
                );
                inputItems.Add(
                    Ui.Row($"{fieldKey}-arrayrow",
                        Ui.Spacer($"{fieldKey}-arrayrow-spacer"),
                        inputNode
                    ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)), State = focused })
                );
            }
            else
            {
                inputItems.Add(
                    Ui.Row($"{fieldKey}-row", labelNode, inputNode)
                      .WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)), State = focused })
                );
            }

            if (!string.IsNullOrEmpty(field.Help))
            {
                if (field.Kind == UiFieldKind.Array)
                {
                    inputItems.Add(
                        Ui.Row($"{fieldKey}-help-row",
                            Ui.Spacer($"{fieldKey}-help-row-spacer"),
                            Ui.Text($"{fieldKey}-help", field.Help).WithStyles(Style.Tag("dim"))
                        ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)) })
                    );
                }
                else
                {
                    inputItems.Add(
                        Ui.Text($"{fieldKey}-help", field.Help).WithStyles(Style.Tag("dim"))
                    );
                }
            }

            var errText = errors.TryGetValue(field.Key, out var em) ? (em ?? "") : "";
            if (field.Kind == UiFieldKind.Array)
            {
                inputItems.Add(
                    Ui.Row($"{fieldKey}-error-row",
                        Ui.Spacer($"{fieldKey}-error-row-spacer"),
                        Ui.Text($"{fieldKey}-error", errText).WithStyles(Style.Color(ConsoleColor.Red))
                    ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Percent(20), GridColumnSpec.Percent(80)) })
                );
            }
            else
            {
                inputItems.Add(
                    Ui.Text($"{fieldKey}-error", errText).WithStyles(Style.Color(ConsoleColor.Red))
                );
            }
        }

        var inputItemColumn = Ui.Column("overlay-form-input-items", inputItems.ToArray())
            .WithProps(new { AutoScroll = true, Scrollable = true, Min = Math.Max(0, bodyScroll) });
        children.Add(inputItemColumn);

        children.Add(
            Ui.Row("overlay-form-buttons",
                Ui.Button("overlay-form-submit", "Submit").WithProps(new { Focusable = true }),
                Ui.Button("overlay-form-cancel", "Cancel").WithProps(new { Focusable = true })
            ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Fr(1), GridColumnSpec.Fr(1)) })
        );

        return Ui.Column("overlay-form", children.ToArray())
            .WithProps(new { Modal = true, Role = "overlay", ZIndex = 3000, Width = "80%", Padding = "2" });
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

        // Special handling for Array fields -> render as per-item rows with Edit/Delete buttons and Add at bottom
        if (field.Kind == UiFieldKind.Array)
        {
            List<string> items;
            try { items = (currentText ?? "[]").FromJson<List<string>>() ?? new List<string>(); }
            catch { items = new List<string>(); }
            var col = BuildArrayColumn(fieldKey, items, field.Label, field.Placeholder);
            return Ui.Column(fieldKey, col).WithProps(new { Focusable = false });
        }

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
                // Hint renderer to show collapsed dropdown chrome (single-line with chevron)
                props[UiProperty.Role] = "dropdown";
                props[UiProperty.Height] = 1;
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

        return Ui.Node(fieldKey, kind).WithProps(props);
    }

    // Builds the column that holds per-item rows and an Add button for an array field
    private static UiNode BuildArrayColumn(string fieldKey, List<string> items, string label, string? placeholder)
    {
        var rows = new List<UiNode>();
        for (int i = 0; i < items.Count; i++)
        {
            var textNode = Ui.Text($"{fieldKey}-item-{i}-text", items[i] ?? string.Empty)
                .WithStyles(Style.Wrap);
            var editBtn = Ui.Button($"{fieldKey}-item-{i}-edit", "✎")
                .WithProps(new { Focusable = true })
                .WithStyles(Style.AlignRight);
            var delBtn = Ui.Button($"{fieldKey}-item-{i}-delete", "×")
                .WithProps(new { Focusable = true })
                .WithStyles(Style.AlignRight);
            rows.Add(
                Ui.Row($"{fieldKey}-item-{i}-row", textNode, editBtn, delBtn)
                    .WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Fr(1), GridColumnSpec.Percent(8), GridColumnSpec.Percent(8)) })
            );
        }

        // Add button row (right aligned)
        rows.Add(
            Ui.Row($"{fieldKey}-add-row",
                Ui.Spacer($"{fieldKey}-add-row-spacer"),
                Ui.Button($"{fieldKey}-add", "+").WithProps(new { Focusable = true })
            ).WithProps(new { Layout = "grid", Columns = GridColumns.Of(GridColumnSpec.Fr(1), GridColumnSpec.Percent(16)) })
        );

        return Ui.Column($"{fieldKey}-col", rows.ToArray()).WithProps(new { Focusable = false });
    }
}