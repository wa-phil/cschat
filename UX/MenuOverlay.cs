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
    public static UiNode Create(string title, IReadOnlyList<string> choices, int selectedIndex = 0)
    {
        if (choices == null || choices.Count == 0)
            throw new ArgumentException("Choices cannot be null or empty", nameof(choices));

        if (selectedIndex < 0 || selectedIndex >= choices.Count)
            selectedIndex = 0;

        var children = new List<UiNode>
        {
            // Title
            new UiNode(
                "overlay-menu-title",
                UiKind.Label,
                new Dictionary<string, object?>
                {
                    ["text"] = title,
                    ["style"] = "bold"
                },
                Array.Empty<UiNode>()
            ),

            // Filter box (for future filtering support)
            new UiNode(
                "overlay-menu-filter",
                UiKind.TextBox,
                new Dictionary<string, object?>
                {
                    ["placeholder"] = "Filter...",
                    [UiProps.Focusable] = true
                },
                Array.Empty<UiNode>()
            ),

            // List view with choices
            new UiNode(
                "overlay-menu-list",
                UiKind.ListView,
                new Dictionary<string, object?>
                {
                    ["items"] = choices.ToList(),
                    ["selectedIndex"] = selectedIndex,
                    [UiProps.Focusable] = true
                },
                Array.Empty<UiNode>()
            ),

            // Button row
            new UiNode(
                "overlay-menu-buttons",
                UiKind.Row,
                new Dictionary<string, object?>(),
                new[]
                {
                    new UiNode(
                        "overlay-menu-ok",
                        UiKind.Button,
                        new Dictionary<string, object?>
                        {
                            ["text"] = "OK",
                            [UiProps.Focusable] = true
                        },
                        Array.Empty<UiNode>()
                    ),
                    new UiNode(
                        "overlay-menu-cancel",
                        UiKind.Button,
                        new Dictionary<string, object?>
                        {
                            ["text"] = "Cancel",
                            [UiProps.Focusable] = true
                        },
                        Array.Empty<UiNode>()
                    )
                }
            )
        };

        return new UiNode(
            "overlay-menu",
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

        // Create TaskCompletionSource to wait for user selection
        var tcs = new TaskCompletionSource<string?>();
        int currentSelected = selectedIndex;
        string currentFilter = "";
        var filteredChoices = new List<string>(choices);

        // Event handlers for menu interaction
        UiHandler onListChange = async (e) =>
        {
            if (e.Value != null && int.TryParse(e.Value, out var newIndex))
            {
                currentSelected = newIndex;
            }
            await Task.CompletedTask;
        };

        UiHandler onFilterChange = async (e) =>
        {
            currentFilter = e.Value ?? "";
            // Filter choices based on the filter text
            filteredChoices = choices
                .Where(c => c.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            
            // Update the list view with filtered choices
            await ui.PatchAsync(new UiPatch(
                new UpdatePropsOp("overlay-menu-list", new Dictionary<string, object?>
                {
                    ["items"] = filteredChoices,
                    ["selectedIndex"] = 0
                })
            ));
            currentSelected = 0;
        };

        UiHandler onOk = async (e) =>
        {
            if (filteredChoices.Count > 0 && currentSelected >= 0 && currentSelected < filteredChoices.Count)
            {
                tcs.TrySetResult(filteredChoices[currentSelected]);
            }
            else
            {
                tcs.TrySetResult(null);
            }
            await Task.CompletedTask;
        };

        UiHandler onCancel = async (e) =>
        {
            tcs.TrySetResult(null);
            await Task.CompletedTask;
        };

        // Create the menu overlay with event handlers
        var menuNode = CreateWithHandlers(title, choices, selectedIndex, onListChange, onFilterChange, onOk, onCancel);

        // Push overlay onto the frame
        var pushPatch = UiFrameBuilder.PushOverlay(menuNode);
        await ui.PatchAsync(pushPatch);

        // Focus the list
        await ui.FocusAsync("overlay-menu-list");

        // Wait for user selection
        var result = await tcs.Task;

        // Pop the overlay
        var popPatch = UiFrameBuilder.PopOverlay("overlay-menu");
        await ui.PatchAsync(popPatch);

        return result;
    }

    /// <summary>
    /// Creates a menu overlay with event handlers attached
    /// </summary>
    private static UiNode CreateWithHandlers(
        string title, 
        IReadOnlyList<string> choices, 
        int selectedIndex,
        UiHandler onListChange,
        UiHandler onFilterChange,
        UiHandler onOk,
        UiHandler onCancel)
    {
        var children = new List<UiNode>
        {
            // Title
            new UiNode(
                "overlay-menu-title",
                UiKind.Label,
                new Dictionary<string, object?>
                {
                    ["text"] = title,
                    ["style"] = "bold"
                },
                Array.Empty<UiNode>()
            ),

            // Filter box
            new UiNode(
                "overlay-menu-filter",
                UiKind.TextBox,
                new Dictionary<string, object?>
                {
                    ["placeholder"] = "Filter...",
                    ["onChange"] = onFilterChange,
                    [UiProps.Focusable] = true
                },
                Array.Empty<UiNode>()
            ),

            // List view with choices
            new UiNode(
                "overlay-menu-list",
                UiKind.ListView,
                new Dictionary<string, object?>
                {
                    ["items"] = choices.ToList(),
                    ["selectedIndex"] = selectedIndex,
                    ["onChange"] = onListChange,
                    ["onEnter"] = onOk,  // Enter key submits
                    [UiProps.Focusable] = true
                },
                Array.Empty<UiNode>()
            ),

            // Button row
            new UiNode(
                "overlay-menu-buttons",
                UiKind.Row,
                new Dictionary<string, object?>(),
                new[]
                {
                    new UiNode(
                        "overlay-menu-ok",
                        UiKind.Button,
                        new Dictionary<string, object?>
                        {
                            ["text"] = "OK",
                            ["onClick"] = onOk,
                            [UiProps.Focusable] = true
                        },
                        Array.Empty<UiNode>()
                    ),
                    new UiNode(
                        "overlay-menu-cancel",
                        UiKind.Button,
                        new Dictionary<string, object?>
                        {
                            ["text"] = "Cancel",
                            ["onClick"] = onCancel,
                            [UiProps.Focusable] = true
                        },
                        Array.Empty<UiNode>()
                    )
                }
            )
        };

        return new UiNode(
            "overlay-menu",
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
}
