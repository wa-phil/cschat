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
    /// Binds to IUi to receive ControlEvent / key events
    /// </summary>
    void Attach(IUi ui);

    /// <summary>
    /// Non-blocking poll; returns a ConsoleKeyInfo if a key is available, else null.
    /// Backend must not block the caller. The actual key processing is deferred to the UI layer.
    /// </summary>
    ConsoleKeyInfo? TryReadKey();

    /// <summary>
    /// Optional: propagate TextBox onChange to callers
    /// </summary>
    event Action<string>? OnInputChanged;
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

    // input
    /// <summary>
    /// Accumulates ConsoleKeyInfo from IInputRouter.TryReadKey into a buffer and resolves on submit (Enter),
    /// honoring Shift+Enter for newline. Photino routes DOM "enter/click" to synthetic keys; Terminal uses real keys.
    /// </summary>
    Task<string?> ReadInputAsync(CommandManager commands);

    [Obsolete("Use ReadInputAsync instead. This method delegates to ReadInputAsync and is kept for backward compatibility.")]
    Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager);
    
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

    void Write(string text);
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
    /// Moves input focus to the specified node
    /// </summary>
    /// <param name="key">The key of the node to focus</param>
    /// <exception cref="KeyNotFoundException">Thrown when no such node exists</exception>
    /// <exception cref="InvalidOperationException">Thrown when node is not focusable</exception>
    Task FocusAsync(string key);
}