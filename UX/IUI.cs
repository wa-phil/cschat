using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;

/// <summary>
/// Interface for a realtime text writer (e.g. console or terminal)
/// </summary>
public interface IRealtimeWriter : IDisposable
{
    void Write(string text);
    void WriteLine(string? text = null);
}

/// <summary>
/// Backend-agnostic input routing: converts ControlEvent (Photino) or key sequences (Terminal)
/// into unified input submission. Bridges enter/click on "composer.input" or "send-btn" into input text.
/// </summary>
public interface IInputRouter
{
    /// <summary>
    /// Non-blocking poll; returns a ConsoleKeyInfo if a key is available, else null.
    /// Backend must not block the caller. The actual key processing is deferred to the UI layer.
    /// </summary>
    ConsoleKeyInfo? TryReadKey();

    /// <summary>
    /// Returns pending submitted text captured from a GUI event (e.g. Photino change events),
    /// clearing the pending value. Returns null in terminal mode where text is tracked via keystrokes.
    /// </summary>
    string? ConsumePendingText() => null;
}

public interface IUi
{
    // high-level I/O
    Task<bool> ShowFormAsync(UiForm form);
    // simple yes/no confirmation (true=yes, false=no). Blank input chooses defaultAnswer.
    Task<bool> ConfirmAsync(string question, bool defaultAnswer = false);
    // launches current platform file picker with given options, returns empty list if cancelled
    Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt);

    Task RenderTableAsync(Table table, string? title = null);
    Task RenderReportAsync(Report report);

    IRealtimeWriter BeginRealtime(string title);

    // progress reporting
    Task<string> StartProgressAsync(string title, CancellationTokenSource cts);
    Task UpdateProgressAsync(string id, ProgressSnapshot snapshot);
    Task CompleteProgressAsync(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown);

    
    IInputRouter GetInputRouter();

    /// <summary>Cancels all active progress ops. Returns true if any were active.
    /// Called by the input loop on ESC so progress gets first claim on the key.</summary>
    bool TryCancelActiveProgress();

    Task<string?> RenderMenuAsync(string header, List<string> choices, int selected = 0);
    Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept);

    // output
    Task RenderChatMessageAsync(ChatMessage message);
    Task RenderChatHistoryAsync(IEnumerable<ChatMessage> messages);

    // low-level console-like I/O (to be removed)
    int CursorTop { get; }

    int Width { get; }
    int Height { get; }
    bool KeyAvailable { get; }
    bool IsOutputRedirected { get; }
    void SetCursorPosition(int left, int top);
    ConsoleColor ForegroundColor { get; set; }
    ConsoleColor BackgroundColor { get; set; }
    void ResetColor();

    [Obsolete("Use IRealtimeWriter for streaming output")]
    void Write(string text);
    [Obsolete("Use IRealtimeWriter for streaming output")]
    void WriteLine(string? text = null);
    void Clear();

    // lets each UI decide how to run/pump itself
    Task RunAsync(Func<Task> appMain);

    /// <summary>
    /// Cancellation token that fires when the UI host is shutting down (e.g. Photino window closed).
    /// Terminal mode returns CancellationToken.None (process exits via Environment.Exit).
    /// </summary>
    CancellationToken ShutdownToken { get; }

    // Declarative control layer (UiNode/UiPatch)
    /// <summary>
    /// Mounts a new control surface as the root of the UI tree
    /// </summary>
    /// <param name="root">The root UiNode to mount</param>
    /// <param name="options">Optional control options (key trapping, initial focus)</param>
    /// <exception cref="ArgumentNullException">Thrown when root is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when duplicate keys exist in subtree</exception>
    /// <exception cref="PlatformNotReadyException">Thrown when UI is not initialized</exception>
    Task SetRootAsync(UiNode root, UiControlOptions? options = null);

    /// <summary>
    /// Applies a patch to the mounted UI tree
    /// </summary>
    /// <param name="patch">The patch containing operations to apply</param>
    /// <exception cref="KeyNotFoundException">Thrown when target key is missing</exception>
    /// <exception cref="InvalidOperationException">Thrown on structural conflicts</exception>
    /// <exception cref="System.ComponentModel.DataAnnotations.ValidationException">Thrown when props are invalid for kind</exception>
    Task PatchAsync(UiPatch patch);

    /// <summary>
    /// Computes a minimal patch between previous and next UiNode trees using the reconciler
    /// and applies it atomically to this UI. If previous is null, this will emit a Replace
    /// operation for the next node's key.
    /// </summary>
    /// <param name="previous">The previous UiNode subtree (or null if mounting)</param>
    /// <param name="next">The next UiNode subtree to render</param>
    Task ReconcileAsync(UiNode? previous, UiNode next);

    /// <summary>
    /// Creates a fluent UiPatchBuilder bound to this UI, allowing chaining ops and then calling PatchAsync().
    /// Usage: await ui.MakePatch().Update(key, props).PatchAsync();
    /// </summary>
    UiPatchBuilder MakePatch();

    /// <summary>
    /// Moves input focus to the specified node
    /// </summary>
    /// <param name="key">The key of the node to focus</param>
    /// <exception cref="KeyNotFoundException">Thrown when no such node exists</exception>
    /// <exception cref="InvalidOperationException">Thrown when node is not focusable</exception>
    Task FocusAsync(string key);

    /// <summary>
    /// Dispatches a named event to the handler prop of a UiNode (OnChange, OnEnter, OnClick, etc.).
    /// Returns true if a handler was found and invoked without throwing; false otherwise.
    /// </summary>
    Task<bool> DispatchEventAsync(string nodeKey, string eventName, string? value = null);
}