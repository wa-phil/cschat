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
}

public interface IUi
{
    // high-level I/O
    Task<bool> ShowFormAsync(UiForm form);
    // simple yes/no confirmation (true=yes, false=no). Blank input chooses defaultAnswer.
    Task<bool> ConfirmAsync(string question, bool defaultAnswer = false);
    // launches current platform file picker with given options, returns empty list if cancelled
    Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt);

    void RenderTable(Table table, string? title = null);
    void RenderReport(Report report);

    IRealtimeWriter BeginRealtime(string title);

    // progress reporting
    string StartProgress(string title, CancellationTokenSource cts);
    void UpdateProgress(string id, ProgressSnapshot snapshot);
    void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown);

    
    IInputRouter GetInputRouter();
    string? RenderMenu(string header, List<string> choices, int selected = 0);
    ConsoleKeyInfo ReadKey(bool intercept);

    // output
    void RenderChatMessage(ChatMessage message);
    void RenderChatHistory(IEnumerable<ChatMessage> messages);

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