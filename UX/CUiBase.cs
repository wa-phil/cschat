using System;
using System.Text;
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
    public virtual Task PatchAsync(UiPatch patch) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        if (patch == null)
        {
            ctx.Append(Log.Data.Error, "Patch is null");
            throw new ArgumentNullException(nameof(patch));
        }

        // Apply the patch atomically to the tree (validates operations)
        _uiTree.ApplyPatch(patch);

        // Debug: Log the messages panel child count after applying realtime patches
        var messagesNode = _uiTree.FindNode(UiFrameKeys.Messages);
        if (messagesNode != null && patch.Ops.Any(op => op is InsertChildOp ico && ico.ParentKey == UiFrameKeys.Messages))
        {
            ctx.Append(Log.Data.Message, $"Messages panel has {messagesNode.Children.Count} children after patch");
        }

        // Delegate to platform-specific patching
        var result = PostPatchAsync(patch);
        ctx.Succeeded();
        return result;
    });

    /// <summary>
    /// Compute a minimal patch between previous and next using UiReconciler and apply it.
    /// </summary>
    public virtual async Task ReconcileAsync(UiNode? previous, UiNode next)
    {
        var patch = new UiReconciler().BuildPatch(previous, next);
        await PatchAsync(patch);
    }

    /// <summary>
    /// Returns a UiPatchBuilder bound to this UI for fluent patch construction and application.
    /// </summary>
    public UiPatchBuilder MakePatch() => new UiPatchBuilder(this);

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
    public virtual IRealtimeWriter BeginRealtime(string title) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Name, title);
        var result = new RealtimeWriterImpl(this, title);
        ctx.Succeeded();
        return result;
    });

    // ========== Progress Implementation (UiNode-based) ==========
    protected readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _progressMap = new();
    private readonly Dictionary<string, UiNode> _progressPrevNodes = new(); // key: progress-{id}

    public virtual string StartProgress(string title, CancellationTokenSource cts)
    {
        var id = Guid.NewGuid().ToString("n");
        _progressMap[id] = cts;

        try
        {
            // Check if messages panel exists
            if (_uiTree.Root != null && _uiTree.FindNode(UiFrameKeys.Messages) != null)
            {
                // Insert ephemeral progress node
                var progressNode = ProgressUi.CreateNode(id, new ProgressSnapshot(
                    Id: id,
                    Title: title,
                    Description: "",
                    Items: Array.Empty<(string, double, ProgressState, string?, (int, int))>(),
                    Stats: (0, 0, 0, 0, 0),
                    EtaHint: null,
                    IsActive: true
                ));

                // Use fluent builder to insert the node
                MakePatch()
                    .Insert(UiFrameKeys.Messages, int.MaxValue, progressNode)
                    .PatchAsync().GetAwaiter().GetResult();

                // Cache as previous for reconciler-based updates
                _progressPrevNodes[$"progress-{id}"] = progressNode;
            }
        }
        catch
        {
            // If we can't insert into UI tree, progress will be invisible until messages panel is ready
        }

        return id;
    }

    public virtual void UpdateProgress(string id, ProgressSnapshot snapshot)
    {
        try
        {
            // Check for ESC key to cancel (delegate to platform-specific input router)
            var router = GetInputRouter();
            var key = router?.TryReadKey();
            if (key.HasValue && key.Value.Key == ConsoleKey.Escape)
            {
                if (_progressMap.TryGetValue(id, out var cts))
                {
                    try { cts.Cancel(); } catch { }
                }
            }

            // Build composed progress subtree and either reconcile against existing node or insert if missing
            var nodeKey = $"progress-{id}";
            var newNode = ProgressUi.CreateNode(id, snapshot);
            if (_uiTree.Root != null && _uiTree.FindNode(nodeKey) != null)
            {
                // Reconcile with previous snapshot if available, else use direct replace
                if (!_progressPrevNodes.TryGetValue(nodeKey, out var prevNode))
                {
                    // Try to pull from current tree as previous
                    prevNode = _uiTree.FindNode(nodeKey) ?? newNode;
                }
                this.ReconcileAsync(prevNode, newNode).GetAwaiter().GetResult();
                _progressPrevNodes[nodeKey] = newNode;
            }
            else if (_uiTree.Root != null && _uiTree.FindNode(UiFrameKeys.Messages) != null)
            {
                // Node doesn't exist yet (maybe tree was replaced) - insert it and seed cache
                MakePatch().Insert(UiFrameKeys.Messages, int.MaxValue, newNode).PatchAsync().GetAwaiter().GetResult();
                _progressPrevNodes[nodeKey] = newNode;
            }
        }
        catch
        {
            // Best effort - ignore errors during progress updates
        }
    }

    public virtual void CompleteProgress(string id, ProgressSnapshot finalSnapshot, string artifactMarkdown)
    {
    _progressMap.TryRemove(id, out _);
    _progressPrevNodes.Remove($"progress-{id}");

        try
        {
            var nodeKey = $"progress-{id}";
            
            // Remove the progress node
            if (_uiTree.Root != null && _uiTree.FindNode(nodeKey) != null)
            {
                MakePatch()
                    .Remove(nodeKey)
                    .PatchAsync().GetAwaiter().GetResult();
            }

            // Display the artifact as a Tool message (same as old behavior)
            RenderChatMessage(new ChatMessage { Role = Roles.Tool, Content = artifactMarkdown });
        }
        catch
        {
            // Best effort - ignore errors during cleanup
        }
    }

    // Progress UI composition is implemented in ProgressUi.CreateNode
    public abstract IInputRouter GetInputRouter();
    public string? RenderMenu(string header, List<string> choices, int selected = 0)
    {
        // Use MenuOverlay for UiNode-based menu rendering
        // This is a synchronous wrapper around the async ShowAsync method
        return MenuOverlay.ShowAsync(this, header, choices, selected).GetAwaiter().GetResult();
    }
    
    public abstract ConsoleKeyInfo ReadKey(bool intercept);
    public void RenderChatMessage(ChatMessage message)
    {
        // Use ChatSurface to render the message via patch
        // Get current message count to determine the index
        var currentMessages = Program.Context?.Messages(InluceSystemMessage: false).ToList() ?? new List<ChatMessage>();
        var index = currentMessages.Count > 0 ? currentMessages.Count - 1 : 0;

        // Apply patch to append the message
        var patch = ChatSurface.AppendMessage(message, index);
        PatchAsync(patch).GetAwaiter().GetResult();
    }

    public void RenderChatHistory(IEnumerable<ChatMessage> messages)
    {
        // Use ChatSurface to render all messages via patch
        var messageList = messages.ToList();

        // Apply patch to update all messages
        var patch = ChatSurface.UpdateMessages(messageList);
        PatchAsync(patch).GetAwaiter().GetResult();
    }
        
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

    private class RealtimeWriterImpl : IRealtimeWriter
    {
        private readonly CUiBase _ui;
        private readonly string _realtimeKey;
        private readonly StringBuilder _content;
        private bool _disposed;
        private bool _nodeInserted;

        public RealtimeWriterImpl(CUiBase ui, string title)
        {
            _ui = ui;
            _realtimeKey = $"realtime_{Guid.NewGuid():N}";
            _content = new StringBuilder();
            _nodeInserted = false;

            // Try to insert an ephemeral message into the messages panel
            // This makes realtime output scrollable with chat history but not persisted
            try
            {
                if (_ui._uiTree.Root != null && _ui._uiTree.FindNode(UiFrameKeys.Messages) != null)
                {
                    _content.AppendLine(title);
                    
                    // Insert as an ephemeral message in the messages panel
                    var patch = ChatSurface.InsertRealtimeMessage(_realtimeKey, _content.ToString());
                    _ui.PatchAsync(patch).GetAwaiter().GetResult();
                    _nodeInserted = true;
                }
                else
                {
                    // Chat surface not mounted yet - queue the title for later display
                    _content.AppendLine(title);
                }
            }
            catch
            {
                // If we can't insert into the UI tree, queue content for when surface is ready
                _content.AppendLine(title);
            }
        }

        public void Write(string text)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RealtimeWriterImpl));
            
            _content.Append(text);
            UpdateContent();
        }

        public void WriteLine(string? text = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RealtimeWriterImpl));
            
            _content.AppendLine(text);
            UpdateContent();
        }

        private void UpdateContent() => Log.Method(ctx =>
        {
            ctx.OnlyEmitOnFailure();
            // Always check if we can insert/update in the UI tree
            try
            {
                // Check if messages panel exists
                if (_ui._uiTree.Root != null && _ui._uiTree.FindNode(UiFrameKeys.Messages) != null)
                {
                    // Check if our node exists
                    var nodeExists = _ui._uiTree.FindNode($"msg-{_realtimeKey}") != null;

                    if (nodeExists)
                    {
                        // Update existing node
                        ctx.Append(Log.Data.Message, $"Updating realtime node {_realtimeKey}");
                        var patch = ChatSurface.UpsertRealtimeMessage(_realtimeKey, _content.ToString());
                        _ui.PatchAsync(patch).GetAwaiter().GetResult();
                        _nodeInserted = true;
                    }
                    else
                    {
                        // Node doesn't exist (maybe tree was replaced) - insert it
                        ctx.Append(Log.Data.Message, $"Inserting realtime node {_realtimeKey}");
                        var patch = ChatSurface.InsertRealtimeMessage(_realtimeKey, _content.ToString());
                        _ui.PatchAsync(patch).GetAwaiter().GetResult();
                        _nodeInserted = true;
                    }
                }
                else
                {
                    // Messages panel not found - log for debugging
                    ctx.Append(Log.Data.Message, $"Messages panel not found for realtime key {_realtimeKey}, root={_ui._uiTree.Root != null}, messages={_ui._uiTree.FindNode(UiFrameKeys.Messages) != null}");
                }

                ctx.Succeeded(_nodeInserted);
            }
            catch (Exception ex)
            {
                // If we can't update, content remains queued in _content for next attempt
                _nodeInserted = false;
                ctx.Failed($"Failed to update realtime content for key {_realtimeKey}: {ex.GetType().Name}: {ex.Message}", ex);
            }
        });

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Mark the realtime message as finalized instead of removing it
            // This keeps it visible in the chat but marks it as non-ephemeral
            // so it won't be persisted to chat history (EphemeralActive messages are filtered out on save)
            if (_nodeInserted)
            {
                try
                {
                    // Update state to Finalized so it stays visible but won't be saved
                    var patch = ChatSurface.UpdateMessageState(_realtimeKey, ChatMessageState.Finalized);
                    _ui.PatchAsync(patch).GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore errors during disposal
                }
            }
        }
    }
}
