using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

/// <summary>
/// Centralized keys for well-known UI nodes to avoid string duplication and typos.
/// </summary>
public static class UiFrameKeys
{
    // Frame structure
    public const string Root = "frame.root";
    public const string Header = "frame.header";
    public const string Content = "frame.content";
    public const string Overlays = "frame.overlays";

    // Chat surface
    public const string Messages = "messages";
    public const string ComposerInput = "composer.input"; // reserved for future use
    public const string SendButton = "send-btn";
}

/// <summary>
/// UiFrameController constructs the main frame (header + content), wires the input router,
/// delegates chat input handling to ChatSurface, and menu interactions to MenuOverlay.
/// Program.cs should instantiate this and call InitializeAsync + RunLoopAsync.
/// </summary>
public sealed class UiFrameController
{
    private readonly IUi _ui;
    private readonly IInputRouter _inputRouter;
    private Context _context;
    private CommandManager? _commands;
    private readonly Config _config;

    // chat input state is owned by ChatSurface
    private ChatSurface.ChatInputState _chatState = new ChatSurface.ChatInputState();

    // Cached subtrees for reconciler-based updates at the frame level
    private UiNode? _prevHeaderNode;
    private UiNode? _prevContentNode;

    public UiFrameController(IUi ui, IInputRouter inputRouter, Context context, Config config)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _inputRouter = inputRouter ?? throw new ArgumentNullException(nameof(inputRouter));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Attaches the command manager after initialization (for startup scenarios where commands aren't ready yet)
    /// </summary>
    public void SetCommandManager(CommandManager commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    /// <summary>
    /// Updates the controller's context reference (needed after Program.Context is modified)
    /// </summary>
    public void UpdateContext(Context context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Refreshes the chat surface with current context messages (e.g., after loading thread history)
    /// Uses AppendMessage to add messages individually, preserving any realtime nodes
    /// </summary>
    public async Task RefreshMessagesAsync() => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var messages = _context.Messages(InluceSystemMessage: false).ToList();
        ctx.Append(Log.Data.Count, messages.Count);

        // Append messages one by one to preserve realtime nodes
        for (int i = 0; i < messages.Count; i++)
        {
            await _ui.PatchAsync(ChatSurface.AppendMessage(messages[i], i));
        }

        ctx.Succeeded();
    });

    /// <summary>
    /// Builds and mounts the initial frame (header + chat surface).
    /// </summary>
    public async Task InitializeAsync()
    {
        // Header with Clear button
        var header = ChatSurface.CreateHeader(
            threadName: Program.config.ChatThreadSettings.ActiveThreadName
        );

        // Content: messages + composer; input handlers are managed by ChatSurface via input router
        var messages = _context.Messages(InluceSystemMessage: false).ToList();
        var content = ChatSurface.Create(messages);

        var frame = new UiFrame(Header: header, Content: content, Overlays: Array.Empty<UiNode>());
        var root = UiFrameBuilder.Create(frame);
        await _ui.SetRootAsync(root, new UiControlOptions(TrapKeys: true, InitialFocusKey: "input"));
        _chatState.FocusKey = "input";

        // Seed previous subtrees for future reconciler diffs using canonical frame keys
        _prevHeaderNode = header with { Key = UiFrameKeys.Header };
        _prevContentNode = content with { Key = UiFrameKeys.Content };
    }

    /// <summary>
    /// Runs the main input loop: ESC opens menu (handled by MenuOverlay),
    /// other keys are handed to ChatSurface to update input and submit messages.
    /// </summary>
    public async Task RunLoopAsync()
    {
        var ct = _ui.ShutdownToken;
        while (!ct.IsCancellationRequested)
        {
            var maybeKey = _inputRouter.TryReadKey();
            if (maybeKey is null)
            {
                try { await Task.Delay(10, ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            var key = maybeKey.Value;

            // Toggle menu on ESC
            if (key.Key == ConsoleKey.Escape)
            {
                // Progress takes priority — cancel if anything is running
                if (_ui.TryCancelActiveProgress())
                    continue;

                if (_commands != null)
                {
                    var choices = _commands.SubCommands
                        .Select(c => $"{c.Name} - {c.Description()}")
                        .ToList();

                    var selected = await MenuOverlay.ShowAsync(_ui, "Commands", choices, 0);
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        var commandName = selected.Split('-')[0].Trim();
                        var command = _commands.SubCommands.FirstOrDefault(c =>
                            c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));
                        if (command != null)
                        {
                            await command.Action.Invoke();
                        }
                    }

                    // restore focus to input after overlay closes
                    await _ui.FocusAsync("input");
                }
                else
                {
                    // Commands not yet initialized - restore focus
                    await _ui.FocusAsync("input");
                }
                _chatState.FocusKey = "input";
                continue;
            }

            // Delegate chat input to ChatSurface
            var (newState, action) = await ChatSurface.HandleKeyAsync(_ui, key, _chatState);
            _chatState = newState;

            if (action == ChatSurface.ChatInputAction.Submit)
            {
                // In Photino mode, text edits arrive as change events (not keystrokes),
                // so _chatState.Text may be empty. Fall back to the router's pending text.
                var submittedText = string.IsNullOrEmpty(_chatState.Text)
                    ? (_inputRouter.ConsumePendingText() ?? _chatState.Text)
                    : _chatState.Text;
                if (!string.IsNullOrWhiteSpace(submittedText))
                {
                    await ChatSurface.ProcessChatInputAsync(
                        _ui,
                        submittedText,
                        _context,
                        async (ctx) =>
                        {
                            var (result, updatedContext) = await Engine.PostChatAsync(ctx);
                            _context = updatedContext; // keep local context in sync
                            Program.Context = updatedContext; // update global for other subsystems expecting it
                            return (result, updatedContext);
                        });
                }

                // Append submitted text to history (oldest-first), then reset input state
                var newHistory = string.IsNullOrWhiteSpace(submittedText)
                    ? _chatState.History
                    : (IReadOnlyList<string>)_chatState.History.Append(submittedText).ToList();

                _chatState = new ChatSurface.ChatInputState { History = newHistory };
                await _ui.FocusAsync("input");
                continue;
            }
        }
    }

    /// <summary>
    /// Remounts the frame from a new UiFrame (e.g. after thread switch) and resets
    /// the reconciler caches so subsequent UpdateHeaderAsync/UpdateContentAsync work correctly.
    /// Use this instead of calling Program.ui.SetRootAsync directly.
    /// </summary>
    public async Task RemountAsync(UiFrame frame)
    {
        if (frame == null) throw new ArgumentNullException(nameof(frame));
        var root = UiFrameBuilder.Create(frame);
        await _ui.SetRootAsync(root, new UiControlOptions(TrapKeys: true, InitialFocusKey: "input"));
        _chatState = new ChatSurface.ChatInputState { FocusKey = "input" };
        _prevHeaderNode = frame.Header with { Key = UiFrameKeys.Header };
        _prevContentNode = frame.Content with { Key = UiFrameKeys.Content };
    }

    /// <summary>
    /// Reconcile header against previous header subtree and patch minimal changes.
    /// </summary>
    public async Task UpdateHeaderAsync(UiNode nextHeader)
    {
        if (nextHeader == null) throw new ArgumentNullException(nameof(nextHeader));
        var remapped = nextHeader with { Key = UiFrameKeys.Header };
        await _ui.ReconcileAsync(_prevHeaderNode, remapped);
        _prevHeaderNode = remapped;
    }

    /// <summary>
    /// Reconcile content against previous content subtree and patch minimal changes.
    /// </summary>
    public async Task UpdateContentAsync(UiNode nextContent)
    {
        if (nextContent == null) throw new ArgumentNullException(nameof(nextContent));
        var remapped = nextContent with { Key = UiFrameKeys.Content };
        await _ui.ReconcileAsync(_prevContentNode, remapped);
        _prevContentNode = remapped;
    }
}

/// <summary>
/// Frame has 3 layers: Header (toolbar), Content (a single mounted surface), OverlayStack (0..N modals)
/// </summary>
public sealed record UiFrame(
    UiNode Header,           // e.g., thread title, buttons
    UiNode Content,          // e.g., ChatSurface root
    IReadOnlyList<UiNode> Overlays // e.g., MenuOverlay, FormOverlay (topmost is last)
);

/// <summary>
/// Creates and manipulates frame trees with semantic keys for stable patching
/// </summary>
public static class UiFrameBuilder
{
    /// <summary>
    /// Creates a frame tree with semantic keys for stable patching.
    /// Returns root UiNode:
    ///   root(Column role=frame)
    ///     ├── header(Row role=header)
    ///     ├── content(Column role=content)
    ///     └── overlays(Column role=overlay) // children layered by zIndex
    /// </summary>
    public static UiNode Create(UiFrame frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));

        // Build header with canonical key and role prop
        var headerWithRole = AddRoleIfMissing(frame.Header, "header") with { Key = UiFrameKeys.Header };

        // Build content with canonical key and role prop
        var contentWithRole = AddRoleIfMissing(frame.Content, "content") with { Key = UiFrameKeys.Content };

        // Build overlays container
        var overlayChildren = frame.Overlays
            .Select((overlay, idx) => AddRoleIfMissing(overlay, "overlay"))
            .ToList();

        var overlaysContainer = Ui.Column(UiFrameKeys.Overlays, overlayChildren.ToArray())
            .WithProps(new { Role = "overlay" });

        // Build root frame
        return Ui.Column(UiFrameKeys.Root, headerWithRole, contentWithRole, overlaysContainer)
            .WithProps(new { Role = "frame" });
    }

    /// <summary>
    /// Creates a patch to replace the content node
    /// </summary>
    public static UiPatch ReplaceContent(UiNode newContent)
    {
        if (newContent == null)
            throw new ArgumentNullException(nameof(newContent));

        var contentWithRole = AddRoleIfMissing(newContent, "content");
        return contentWithRole.ToPatch(UiFrameKeys.Content);
    }    

    /// <summary>
    /// Creates a patch to push an overlay onto the stack (top-most modal)
    /// </summary>
    public static UiPatch PushOverlay(UiNode overlay)
    {
        if (overlay == null)
            throw new ArgumentNullException(nameof(overlay));

        var overlayWithRole = AddRoleIfMissing(overlay, "overlay");

        // Insert at the end (highest index = topmost)
    return new UiPatchBuilder().Insert(UiFrameKeys.Overlays, int.MaxValue, overlayWithRole).Build();
    }

    /// <summary>
    /// Creates a patch to pop the top-most overlay from the stack
    /// </summary>
    public static UiPatch PopOverlay(string overlayKey)
    {
        if (string.IsNullOrEmpty(overlayKey))
            throw new ArgumentNullException(nameof(overlayKey));

        return new UiPatch(new RemoveOp(overlayKey));
    }

    /// <summary>
    /// Creates a patch to replace the header node
    /// </summary>
    public static UiPatch ReplaceHeader(UiNode newHeader)
    {
        if (newHeader == null)
            throw new ArgumentNullException(nameof(newHeader));

    var headerWithRole = AddRoleIfMissing(newHeader, "header");
    return headerWithRole.ToPatch(UiFrameKeys.Header);
    }
    
    // Helper: adds role prop if not present, preserving other props
    private static UiNode AddRoleIfMissing(UiNode node, string role)
    {
        if (node.Props.ContainsKey(UiProperty.Role))
            return node;

        var newProps = new Dictionary<UiProperty, object?>(node.Props)
        {
            [UiProperty.Role] = role
        };

        return node with { Props = newProps };
    }

}