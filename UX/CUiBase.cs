using System;
using System.Threading.Tasks;
using System.Collections.Generic;

/// <summary>
/// Abstract base class for UI implementations providing shared UiNode/UiPatch functionality.
/// Owns UiNodeTree mounting/patching, focus routing, overlay helpers.
/// Photino and Terminal inherit from this to reuse common control layer logic.
/// </summary>
public abstract partial class CUiBase : IUi
{
    // Shared retained-mode UI tree
    protected readonly UiNodeTree _uiTree = new();
    protected UiControlOptions? _controlOptions;

    // ========== Declarative Control Layer (Shared) ==========

    /// <summary>
    /// Mounts a new control surface as the root of the UI tree
    /// </summary>
    public virtual Task SetRootAsync(UiNode root, UiControlOptions? options = null)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        // Validate and set the root
        _uiTree.SetRoot(root);
        _controlOptions = options ?? new UiControlOptions();

        // Set initial focus if specified
        if (!string.IsNullOrEmpty(_controlOptions.InitialFocusKey))
        {
            try
            {
                _uiTree.SetFocus(_controlOptions.InitialFocusKey);
            }
            catch (Exception ex)
            {
                Log.Method(ctx => ctx.Failed($"Failed to set initial focus to '{_controlOptions.InitialFocusKey}'", ex));
            }
        }

        // Delegate to platform-specific mounting
        return PostSetRootAsync(root, _controlOptions);
    }

    /// <summary>
    /// Applies a patch to the mounted UI tree
    /// </summary>
    public virtual Task PatchAsync(UiPatch patch)
    {
        if (patch == null)
            throw new ArgumentNullException(nameof(patch));

        // Apply the patch atomically to the tree (validates operations)
        _uiTree.ApplyPatch(patch);

        // Delegate to platform-specific patching
        return PostPatchAsync(patch);
    }

    /// <summary>
    /// Moves input focus to the specified node
    /// </summary>
    public virtual Task FocusAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        // Set focus in the tree (validates node exists and is focusable)
        _uiTree.SetFocus(key);

        // Delegate to platform-specific focus handling
        return PostFocusAsync(key);
    }

    // ========== Template Methods for Platform-Specific Implementation ==========

    /// <summary>
    /// Platform-specific implementation for mounting the root node
    /// </summary>
    protected abstract Task PostSetRootAsync(UiNode root, UiControlOptions options);

    /// <summary>
    /// Platform-specific implementation for applying a patch
    /// </summary>
    protected abstract Task PostPatchAsync(UiPatch patch);

    /// <summary>
    /// Platform-specific implementation for setting focus
    /// </summary>
    protected abstract Task PostFocusAsync(string key);

    // ========== Abstract Methods (must be implemented by platform) ==========

    public async Task<bool> ShowFormAsync(UiForm form)
    {
        return await FormOverlay.ShowAsync(this, form);
    }
    
    public abstract Task<bool> ConfirmAsync(string question, bool defaultAnswer = false);
    public abstract Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt);
    public abstract void RenderTable(Table table, string? title = null);
    public abstract void RenderReport(Report report);
    public abstract IRealtimeWriter BeginRealtime(string title);
    public abstract string StartProgress(string title, CancellationTokenSource cts);
    public abstract void UpdateProgress(string id, ProgressSnapshot snapshot);
    public abstract void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown);
    public abstract Task<string?> ReadInputAsync(CommandManager commands);
    [Obsolete("Use ReadInputAsync instead")]
    public abstract Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager);
    public abstract IInputRouter GetInputRouter();
    public abstract string? RenderMenu(string header, List<string> choices, int selected = 0);
    public abstract ConsoleKeyInfo ReadKey(bool intercept);
    public abstract void RenderChatMessage(ChatMessage message);
    public abstract void RenderChatHistory(IEnumerable<ChatMessage> messages);
    public abstract int CursorTop { get; }
    public abstract int Width { get; }
    public abstract int Height { get; }
    public abstract bool KeyAvailable { get; }
    public abstract bool IsOutputRedirected { get; }
    public abstract void SetCursorPosition(int left, int top);
    public abstract ConsoleColor ForegroundColor { get; set; }
    public abstract ConsoleColor BackgroundColor { get; set; }
    public abstract void ResetColor();
    public abstract void Write(string text);
    public abstract void WriteLine(string? text = null);
    public abstract void Clear();
    public abstract Task RunAsync(Func<Task> appMain);
}
