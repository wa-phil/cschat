using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            new Dictionary<string, object?>
            {
                ["text"] = form.Title,
                ["style"] = "bold",
                ["align"] = "center"
            },
            Array.Empty<UiNode>()
        ));

        // Iterate through fields and create row with label (left) + control (right)
        foreach (var field in form.Fields)
        {
            var fieldKey = $"overlay-form-field-{field.Key}";
            var currentText = field.Formatter(form.Model);

            var labelNode = new UiNode(
                $"{fieldKey}-label",
                UiKind.Label,
                new Dictionary<string, object?>
                {
                    ["text"] = field.Label + (field.Required ? " *" : ""),
                },
                Array.Empty<UiNode>()
            );

            var inputNode = CreateFieldInput(fieldKey, field, currentText);

            // Compose the field line as a split row (50/50)
            children.Add(new UiNode(
                $"{fieldKey}-row",
                UiKind.Row,
                new Dictionary<string, object?>
                {
                    ["layout"] = "split-50-50"
                },
                new[] { labelNode, inputNode }
            ));

            // Help text if present
            if (!string.IsNullOrEmpty(field.Help))
            {
                children.Add(new UiNode(
                    $"{fieldKey}-help",
                    UiKind.Label,
                    new Dictionary<string, object?>
                    {
                        ["text"] = field.Help,
                        ["style"] = "dim"
                    },
                    Array.Empty<UiNode>()
                ));
            }

            // Error placeholder (initially empty)
            children.Add(new UiNode(
                $"{fieldKey}-error",
                UiKind.Label,
                new Dictionary<string, object?>
                {
                    ["text"] = "",
                    ["color"] = "red"
                },
                Array.Empty<UiNode>()
            ));
        }

        // Button row
        children.Add(new UiNode(
            "overlay-form-buttons",
            UiKind.Row,
            new Dictionary<string, object?>
            {
                ["layout"] = "split-50-50"
            },
            new[]
            {
                new UiNode(
                    "overlay-form-submit",
                    UiKind.Button,
                    new Dictionary<string, object?>
                    {
                        ["text"] = "Submit",
                        [UiProps.Focusable] = true
                    },
                    Array.Empty<UiNode>()
                ),
                new UiNode(
                    "overlay-form-cancel",
                    UiKind.Button,
                    new Dictionary<string, object?>
                    {
                        ["text"] = "Cancel",
                        [UiProps.Focusable] = true
                    },
                    Array.Empty<UiNode>()
                )
            }
        ));

        return new UiNode(
            "overlay-form",
            UiKind.Column,
            new Dictionary<string, object?>
            {
                [UiProps.Modal] = true,
                [UiProps.Role] = "overlay",
                [UiProps.ZIndex] = 3000, // ensure forms sit above menu overlays
                ["width"] = "80%",
                ["padding"] = "2"
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
                ops.Add(new UpdatePropsOp(rowKey, new Dictionary<string, object?>
                {
                    ["layout"] = "split-50-50",
                    ["focused"] = focused
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
            var op = new UpdatePropsOp($"overlay-form-field-{fieldKey}-error", new Dictionary<string, object?>
            {
                ["text"] = message ?? ""
            });
            await ui.PatchAsync(new UiPatch(op));
        }

        // Clear all errors initially
        var clearOps = new List<UiOp>();
        foreach (var f in form.Fields)
        {
            clearOps.Add(new UpdatePropsOp($"overlay-form-field-{f.Key}-error", new Dictionary<string, object?> { ["text"] = "" }));
        }
        if (clearOps.Count > 0)
            await ui.PatchAsync(new UiPatch(clearOps.ToArray()));

        // Helper: update a field node's UI props from local 'values'
        async Task RefreshFieldAsync(IUiField f)
        {
            var fk = $"overlay-form-field-{f.Key}";
            var val = values.TryGetValue(fk, out var v) ? v : null;
            var props = new Dictionary<string, object?>();
            switch (f.Kind)
            {
                case UiFieldKind.Bool:
                    props["checked"] = (val as bool?) ?? false;
                    break;
                case UiFieldKind.Enum:
                    // keep items from Create; only update selectedIndex via value lookup
                    var items = f.EnumChoices()?.ToList() ?? new List<string>();
                    var s = (val as string) ?? "";
                    var idx = Math.Max(0, items.FindIndex(i => string.Equals(i, s, StringComparison.OrdinalIgnoreCase)));
                    props["selectedIndex"] = items.Count == 0 ? -1 : idx;
                    break;
                default:
                    props["text"] = val?.ToString() ?? "";
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
                            await ui.PatchAsync(new UiPatch(new UpdatePropsOp(fk, new Dictionary<string, object?> { ["text"] = curText })));
                        }
                        else if (key.Key == ConsoleKey.Backspace)
                        {
                            var curText = (values.TryGetValue(fk, out var tv) ? tv?.ToString() : "") ?? "";
                            if (curText.Length > 0)
                            {
                                curText = curText.Substring(0, curText.Length - 1);
                                values[fk] = curText;
                                await ui.PatchAsync(new UiPatch(new UpdatePropsOp(fk, new Dictionary<string, object?> { ["text"] = curText })));
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
                ops.Add(new UpdatePropsOp($"overlay-form-field-{f.Key}-error", new Dictionary<string, object?> { ["text"] = "" }));
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
        var props = new Dictionary<string, object?>
        {
            [UiProps.Focusable] = true
        };

        // Add placeholder if present
        if (!string.IsNullOrEmpty(field.Placeholder))
            props["placeholder"] = field.Placeholder;

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
                props["items"] = list;
                var sel = Math.Max(0, list.FindIndex(i => string.Equals(i, currentText ?? "", StringComparison.OrdinalIgnoreCase)));
                props["selectedIndex"] = list.Count == 0 ? -1 : sel;
            }
        }

        // Add constraints for number fields
        if (field.Kind == UiFieldKind.Number)
        {
            var min = field.MinInt();
            var max = field.MaxInt();
            if (min.HasValue)
                props["min"] = min.Value;
            if (max.HasValue)
                props["max"] = max.Value;
        }

        // Initialize value props based on kind
        switch (field.Kind)
        {
            case UiFieldKind.Bool:
                props["checked"] = bool.TryParse(currentText, out var b) ? b : (!string.IsNullOrEmpty(currentText) && currentText != "0" && !currentText.Equals("false", StringComparison.OrdinalIgnoreCase));
                break;
            case UiFieldKind.Enum:
                // handled above
                break;
            default:
                props["text"] = currentText;
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
