using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// InputOverlay displays a minimal modal with a title and a single-line TextBox.
/// Returns the entered string on Enter, or null on Escape.
/// Keys used: "overlay-input", "overlay-input-title", "overlay-input-box".
/// </summary>
public static class InputOverlay
{
    public static UiNode Create(string title, string? initial = null, string? placeholder = null)
    {
        var children = new List<UiNode>
        {
            new UiNode(
                "overlay-input-title",
                UiKind.Label,
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = title },
                Array.Empty<UiNode>(), UiStyles.Of((UiStyleKey.Align, "center"), (UiStyleKey.Bold, true))
            ),
            new UiNode(
                "overlay-input-box",
                UiKind.TextBox,
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = initial ?? string.Empty,
                    [UiProperty.Placeholder] = placeholder ?? string.Empty,
                    [UiProperty.Focusable] = true
                },
                Array.Empty<UiNode>()
            )
        };

        return new UiNode(
            "overlay-input",
            UiKind.Column,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.Modal] = true,
                [UiProperty.Role] = "overlay",
                [UiProperty.ZIndex] = 4000,
                [UiProperty.Width] = "60%",
                [UiProperty.Padding] = "2"
            },
            children
        );
    }

    public static async Task<string?> ShowAsync(IUi ui, string title, string? initial = null, string? placeholder = null)
    {
        if (ui == null) throw new ArgumentNullException(nameof(ui));

        var node = Create(title, initial, placeholder);
        await ui.PatchAsync(UiFrameBuilder.PushOverlay(node));
        await ui.FocusAsync("overlay-input-box");

        string buffer = initial ?? string.Empty;
        var router = ui.GetInputRouter();

        async Task RefreshAsync()
        {
            await ui.PatchAsync(new UiPatch(new UpdatePropsOp("overlay-input-box", new Dictionary<UiProperty, object?>
            {
                [UiProperty.Text] = buffer
            })));
        }

        string? result = null;
        while (true)
        {
            var maybeKey = router.TryReadKey();
            if (maybeKey is null) { await Task.Delay(10); continue; }
            var key = maybeKey.Value;

            if (key.Key == ConsoleKey.Escape)
            {
                result = null; break;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                result = buffer; break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer = buffer.Substring(0, buffer.Length - 1);
                    await RefreshAsync();
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                buffer += key.KeyChar;
                await RefreshAsync();
                continue;
            }
        }

        await ui.PatchAsync(UiFrameBuilder.PopOverlay("overlay-input"));
        return result;
    }
}
