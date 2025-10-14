using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    }

    /// <summary>
    /// Runs the main input loop: ESC opens menu (handled by MenuOverlay),
    /// other keys are handed to ChatSurface to update input and submit messages.
    /// </summary>
    public async Task RunLoopAsync()
    {
        while (true)
        {
            var maybeKey = _inputRouter.TryReadKey();
            if (maybeKey is null)
            {
                await Task.Delay(10);
                continue;
            }

            var key = maybeKey.Value;

            // Toggle menu on ESC
            if (key.Key == ConsoleKey.Escape)
            {
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
                if (!string.IsNullOrWhiteSpace(_chatState.Text))
                {
                    await ChatSurface.ProcessChatInputAsync(
                        _ui,
                        _chatState.Text,
                        _context,
                        async (ctx) =>
                        {
                            var (result, updatedContext) = await Engine.PostChatAsync(ctx);
                            _context = updatedContext; // keep local context in sync
                            Program.Context = updatedContext; // update global for other subsystems expecting it
                            return (result, updatedContext);
                        });
                }

                // reset input state after sending
                _chatState = new ChatSurface.ChatInputState();
                await _ui.PatchAsync(ChatSurface.UpdateInput(""));
                await _ui.FocusAsync("input");
                continue;
            }
        }
    }
}
