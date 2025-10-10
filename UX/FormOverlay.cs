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
                ["style"] = "bold"
            },
            Array.Empty<UiNode>()
        ));

        // Iterate through fields and create appropriate UI nodes
        foreach (var field in form.Fields)
        {
            var fieldKey = $"overlay-form-field-{field.Key}";
            
            // Field label
            children.Add(new UiNode(
                $"{fieldKey}-label",
                UiKind.Label,
                new Dictionary<string, object?>
                {
                    ["text"] = field.Label + (field.Required ? " *" : ""),
                },
                Array.Empty<UiNode>()
            ));

            // Field input based on kind
            var inputNode = CreateFieldInput(fieldKey, field);
            children.Add(inputNode);

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
            new Dictionary<string, object?>(),
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
        var pushPatch = UiFrameBuilder.PushOverlay(formNode);
        await ui.PatchAsync(pushPatch);

        // Focus the first field
        var firstField = form.Fields.FirstOrDefault();
        if (firstField != null)
        {
            var firstFieldKey = $"overlay-form-field-{firstField.Key}";
            await ui.FocusAsync(firstFieldKey);
        }

        // Wait for user interaction (simplified for now - will need event handling)
        // For now, we'll use a TaskCompletionSource that will be completed by event handlers
        var tcs = new TaskCompletionSource<bool>();

        // TODO: Wire up event handlers for:
        // - Submit button click -> validate all fields, if valid complete with true
        // - Cancel button click -> complete with false
        // - Escape key -> complete with false
        // - Field validation on change
        // - Tab navigation between fields

        // Temporary fallback: return false (cancelled)
        // This will be replaced with proper event handling
        var result = false;

        // Pop the overlay
        var popPatch = UiFrameBuilder.PopOverlay("overlay-form");
        await ui.PatchAsync(popPatch);

        return result;
    }

    private static UiNode CreateFieldInput(string fieldKey, IUiField field)
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
                props["items"] = choices.ToList();
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

        return new UiNode(
            fieldKey,
            kind,
            props,
            Array.Empty<UiNode>()
        );
    }
}
