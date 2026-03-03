using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// ConfirmOverlay displays a modal yes/no dialog over the current frame.
/// Returns true (Yes) or false (No / Escape).
/// Keys: Y / Enter = confirm; N / Escape = cancel; Tab cycles Y↔N focus.
/// Node keys: "overlay-confirm", "overlay-confirm-question", "overlay-confirm-yes", "overlay-confirm-no"
/// </summary>
public static class ConfirmOverlay
{
    public static UiNode Create(string question, bool yesDefault)
    {
        var yesFg = yesDefault ? ConsoleColor.Black : (ConsoleColor?)null;
        var yesBg = yesDefault ? ConsoleColor.White  : (ConsoleColor?)null;

        return Ui.Column("overlay-confirm",
            Ui.Text("overlay-confirm-question", question)
                .WithStyles(Style.Combine(Style.AlignCenter, Style.Wrap)),
            Ui.Spacer("overlay-confirm-spacer"),
            Ui.Row("overlay-confirm-buttons",
                Ui.Button("overlay-confirm-yes", yesDefault ? "[Yes]" : " Yes ")
                    .WithProps(new { Focusable = true })
                    .WithStyles(yesFg.HasValue ? Style.Color(yesFg, yesBg) : UiStyles.Empty),
                Ui.Button("overlay-confirm-no",  yesDefault ? " No " : "[No] ")
                    .WithProps(new { Focusable = true })
            ).WithProps(new { Layout = "row-justify" })
        ).WithProps(new { Modal = true, Role = "overlay",Width = "50%", Padding = "2" });
    }

    public static async Task<bool> ShowAsync(IUi ui, string question, bool defaultAnswer = false)
    {
        if (ui == null) throw new ArgumentNullException(nameof(ui));

        var node = Create(question, defaultAnswer);
        await ui.PatchAsync(UiFrameBuilder.PushOverlay(node));

        // Focus the default button
        var defaultKey = defaultAnswer ? "overlay-confirm-yes" : "overlay-confirm-no";
        try { await ui.FocusAsync(defaultKey); } catch { /* best effort */ }

        bool focused = defaultAnswer; // true = Yes focused, false = No focused
        var router = ui.GetInputRouter();
        bool result = defaultAnswer;

        while (true)
        {
            var maybeKey = router.TryReadKey();
            if (maybeKey is null) { await Task.Delay(10); continue; }
            var key = maybeKey.Value;

            if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.N)
            {
                result = false; break;
            }
            if (key.Key == ConsoleKey.Y)
            {
                result = true; break;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                result = focused; break;
            }
            if (key.Key == ConsoleKey.Tab || key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow)
            {
                focused = !focused;
                var focusKey = focused ? "overlay-confirm-yes" : "overlay-confirm-no";
                try { await ui.FocusAsync(focusKey); } catch { /* best effort */ }
            }
        }

        await ui.PatchAsync(UiFrameBuilder.PopOverlay("overlay-confirm"));
        return result;
    }
}

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
            Ui.Text("overlay-input-title", title).WithStyles(Style.Combine(Style.AlignCenter, Style.Bold)),
            Ui.TextBox("overlay-input-box", initial ?? string.Empty, placeholder ?? string.Empty)
                .WithProps(new { Focusable = true })
        };

        return Ui.Column("overlay-input", children.ToArray())
            .WithProps(new { Modal = true, Role = "overlay", Width = "60%", Padding = "2" });
    }

    public static async Task<string?> ShowAsync(IUi ui, string title, string? initial = null, string? placeholder = null)
    {
        if (ui == null) throw new ArgumentNullException(nameof(ui));

        // Initial render + push
        var prevNode = Create(title, initial, placeholder);
        await ui.PatchAsync(UiFrameBuilder.PushOverlay(prevNode));
        await ui.FocusAsync("overlay-input-box");

        string buffer = initial ?? string.Empty;
        var router = ui.GetInputRouter();

        // Re-render the overlay and reconcile with previous
        async Task RefreshAsync()
        {
            var nextNode = Create(title, buffer, placeholder);
            await ui.ReconcileAsync(prevNode, nextNode);
            prevNode = nextNode;
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
