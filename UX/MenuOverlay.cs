using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// MenuOverlay builds a modal overlay node with a list view, filter box, and OK/Cancel buttons.
/// Replaces legacy RenderMenu with a UiNode-based implementation.
/// </summary>
public static class MenuOverlay
{
    /// <summary>
    /// Builds a modal overlay node with a list view, filter box, and OK/Cancel
    /// Keys: "overlay-menu", "overlay-menu-title", "overlay-menu-filter", "overlay-menu-list", "overlay-menu-ok", "overlay-menu-cancel"
    /// </summary>
    public static UiNode Create(string title, IReadOnlyList<string> choices, int selectedIndex = 0, string filterText = "")
    {
        if (choices == null || choices.Count == 0)
            throw new ArgumentException("Choices cannot be null or empty", nameof(choices));

        if (selectedIndex < 0 || selectedIndex >= choices.Count)
            selectedIndex = 0;

        var children = new List<UiNode>
        {
            Ui.Text("overlay-menu-title", title).WithStyles(Style.Combine(Style.AlignCenter, Style.Bold)),
            Ui.TextBox("overlay-menu-filter", filterText, "Filter...").WithProps(new { Focusable = true }),
            Ui.Node("overlay-menu-list", UiKind.ListView, new { Items = choices.ToList(), SelectedIndex = selectedIndex, Focusable = true })
        };

        return Ui.Column("overlay-menu", children.ToArray())
            .WithProps(new { Modal = true, Role = "overlay", Width = "80%", Padding = "2" });
    }

    /// <summary>
    /// Drives the overlay interaction and returns the chosen string (or null if cancelled)
    /// </summary>
    public static async Task<string?> ShowAsync(IUi ui, string title, IReadOnlyList<string> choices, int selectedIndex = 0)
    {
        if (ui == null)
            throw new ArgumentNullException(nameof(ui));

        if (choices == null || choices.Count == 0)
            throw new ArgumentException("Choices cannot be null or empty", nameof(choices));

        if (selectedIndex < 0 || selectedIndex >= choices.Count)
            selectedIndex = 0;

        // Local state for interaction
        int currentSelected = selectedIndex;
        string currentFilter = "";
        var filteredChoices = new List<string>(choices);
        const int page = 10;

        // Create and push overlay (no external handlers; we'll drive via input router)
        var menuNode = Create(title, filteredChoices, currentSelected, currentFilter);
        await ui.PatchAsync(UiFrameBuilder.PushOverlay(menuNode));
        var prevNode = menuNode;

        // Set initial focus to filter box so users can start typing immediately
        await ui.FocusAsync("overlay-menu-filter");

        // Helper to refresh the list and filter text in UI
        async Task RefreshAsync(bool updateFilter = true, bool updateList = true)
        {
            // Clamp selection based on filtered list
            if (updateList)
            {
                if (filteredChoices.Count == 0) currentSelected = -1;
                else if (currentSelected < 0) currentSelected = 0;
                else if (currentSelected >= filteredChoices.Count) currentSelected = filteredChoices.Count - 1;
            }

            var nextNode = Create(title, filteredChoices, currentSelected, currentFilter);
            await ui.ReconcileAsync(prevNode, nextNode);
            prevNode = nextNode;
        }

        // Main input loop (implementation detail of MenuOverlay)
        string? result = null;
        var router = ui.GetInputRouter();
        while (true)
        {
            var maybeKey = router.TryReadKey();
            if (maybeKey is null)
            {
                await Task.Delay(10);
                continue;
            }

            var key = maybeKey.Value;

            // ESC cancels
            if (key.Key == ConsoleKey.Escape)
            {
                result = null;
                break;
            }

            // Enter selects current item
            if (key.Key == ConsoleKey.Enter)
            {
                if (filteredChoices.Count > 0 && currentSelected >= 0 && currentSelected < filteredChoices.Count)
                    result = filteredChoices[currentSelected];
                else
                    result = null;
                break;
            }

            // Filtering: any printable character appends to filter
            if (!char.IsControl(key.KeyChar))
            {
                currentFilter += key.KeyChar;
                filteredChoices = choices
                    .Where(c => c.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                currentSelected = 0;
                await RefreshAsync(updateFilter: true, updateList: true);
                continue;
            }

            // Backspace modifies filter
            if (key.Key == ConsoleKey.Backspace)
            {
                if (currentFilter.Length > 0)
                {
                    currentFilter = currentFilter.Substring(0, currentFilter.Length - 1);
                    filteredChoices = choices
                        .Where(c => c.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    currentSelected = 0;
                    await RefreshAsync(updateFilter: true, updateList: true);
                }
                continue;
            }

            // Navigation: Up/Down/Home/End/PageUp/PageDown
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (currentSelected > 0)
                {
                    currentSelected--;
                    await RefreshAsync(updateFilter: false, updateList: true);
                }
                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                if (filteredChoices.Count > 0 && currentSelected < filteredChoices.Count - 1)
                {
                    currentSelected++;
                    await RefreshAsync(updateFilter: false, updateList: true);
                }
                continue;
            }

            if (key.Key == ConsoleKey.PageUp)
            {
                currentSelected = Math.Max(0, currentSelected - page);
                await RefreshAsync(updateFilter: false, updateList: true);
                continue;
            }

            if (key.Key == ConsoleKey.PageDown)
            {
                currentSelected = Math.Min(Math.Max(0, filteredChoices.Count - 1), currentSelected + page);
                await RefreshAsync(updateFilter: false, updateList: true);
                continue;
            }

            if (key.Key == ConsoleKey.Home)
            {
                currentSelected = 0;
                await RefreshAsync(updateFilter: false, updateList: true);
                continue;
            }

            if (key.Key == ConsoleKey.End)
            {
                currentSelected = Math.Max(0, filteredChoices.Count - 1);
                await RefreshAsync(updateFilter: false, updateList: true);
                continue;
            }
        }

        // Pop the overlay and return
        await ui.PatchAsync(UiFrameBuilder.PopOverlay("overlay-menu"));
        return result;
    }
}
