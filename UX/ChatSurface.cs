using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Pipelines.WebApi;

/// <summary>
/// ChatSurface builds a UiNode tree for the chat interface
/// Provides methods to create and update the chat UI using declarative UiNode structure
/// </summary>
public static class ChatSurface
{
    // Declarative VM representing the chat surface
    public sealed record ChatVm(IReadOnlyList<ChatMessage> Messages, string InputText);

    // Cache of last rendered root for reconciler-based updates
    private static UiNode? _prevRoot;

    // Encapsulate input state for chat composer
    public sealed record ChatInputState
    {
        public string Text { get; init; } = "";
        public int Caret { get; init; } = 0;
        public string FocusKey { get; set; } = "input"; // "input" or UiFrameKeys.SendButton
        public int Scroll { get; init; } = 0;

        // Input history: list of previously submitted messages (oldest first).
        // HistoryIndex = -1 means not in history-browsing mode.
        // When browsing, index counts from the end: 0 = most recent.
        public IReadOnlyList<string> History { get; init; } = Array.Empty<string>();
        public int HistoryIndex { get; init; } = -1;
    }

    public enum ChatInputAction
    {
        None,
        Submit
    }

    /// <summary>
    /// Creates the root UiNode for the chat surface (Content area only - no toolbar)
    /// </summary>
    /// <param name="messages">List of chat messages to display</param>
    /// <returns>Root UiNode representing the chat surface content</returns>
    public static UiNode Create(IEnumerable<ChatMessage> messages)
    {
        var messagesPanel = Ui.Column(UiFrameKeys.Messages)
            .WithProps(new { Scrollable = true, AutoScroll = true })
            .ForEach(messages.Select((msg, index) => (msg, index)), t => CreateMessageNode(t.msg, t.index));

        UiHandler onInput = async e => {
            var currentInputText = e.Value ?? "";
            await Program.ui.PatchAsync(ChatSurface.UpdateInput(currentInputText));
        };

        // Wire Send/Enter events to the chat pipeline so GUI and terminal behave the same
        UiHandler onSend = async e => {
            var text = e.Value ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;

            // Delegate to shared ChatSurface processing which appends user msg, calls provider, and appends response
            await ProcessChatInputAsync(
                Program.ui,
                text,
                Program.Context,
                async (ctx) => await Engine.PostChatAsync(ctx)
            );

            // Focus back to input after sending
            try { await Program.ui.FocusAsync("input"); } catch { /* best effort */ }
        };

        return Ui.Column("chat-root",
            messagesPanel,
            Ui.Spacer("spacer", new { Height = 1 }),
            CreateComposer(string.Empty, onSend, onInput)
        ).WithProps(new { Layout = "dock-bottom" });
    }

    /// <summary>
    /// Render the chat surface for a given VM using the provided UI's event wiring, and reconcile with the previous render.
    /// </summary>
    public static async Task UpdateAsync(IUi ui, ChatVm vm)
    {
        if (ui == null) throw new ArgumentNullException(nameof(ui));
        if (vm == null) throw new ArgumentNullException(nameof(vm));

        // Build handlers bound to this UI instance
        UiHandler onInput = async e => {
            var currentInputText = e.Value ?? "";
            await UpdateAsync(ui, vm with { InputText = currentInputText });
        };

        UiHandler onSend = async e => {
            var text = e.Value ?? vm.InputText;
            if (string.IsNullOrWhiteSpace(text)) return;

            await ProcessChatInputAsync(
                ui,
                text,
                Program.Context,
                async (ctx) => await Engine.PostChatAsync(ctx)
            );

            try { await ui.FocusAsync("input"); } catch { /* best effort */ }
        };

        // Build next tree
        var messagesPanel = Ui.Column(UiFrameKeys.Messages)
            .WithProps(new { Scrollable = true, AutoScroll = true })
            .ForEach(vm.Messages.Select((msg, index) => (msg, index)), t => CreateMessageNode(t.msg, t.index));

        var root = Ui.Column("chat-root",
            messagesPanel,
            Ui.Spacer("spacer", new { Height = 1 }),
            CreateComposer(vm.InputText, onSend, onInput)
        ).WithProps(new { Layout = "dock-bottom" });

        await ui.ReconcileAsync(_prevRoot, root);
        _prevRoot = root;
    }

    /// <summary>
    /// Handles low-level key input for the chat surface. Returns updated state and an action (e.g. Submit).
    /// This keeps input editing logic local to ChatSurface.
    /// </summary>
    public static async Task<(ChatInputState state, ChatInputAction action)> HandleKeyAsync(IUi ui, ConsoleKeyInfo key, ChatInputState state)
    {
        var current = state with { };

        // Tab cycles between input and send button
        if (key.Key == ConsoleKey.Tab)
        {
            if (current.FocusKey == "input")
            {
                await ui.FocusAsync(UiFrameKeys.SendButton);
                current.FocusKey = UiFrameKeys.SendButton;
            }
            else
            {
                await ui.FocusAsync("input");
                current.FocusKey = "input";
            }
            return (current, ChatInputAction.None);
        }

        // If focus is on send button, Enter/Space triggers submit (dispatches OnClick if wired)
        if (current.FocusKey == UiFrameKeys.SendButton && (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Spacebar))
        {
            var textToSubmit = current.Text;
            bool clicked = await ui.DispatchEventAsync(UiFrameKeys.SendButton, "OnClick", textToSubmit);
            if (clicked)
            {
                // Handler owns submission; manage history and clear input here
                var newHistory = string.IsNullOrWhiteSpace(textToSubmit)
                    ? current.History
                    : (IReadOnlyList<string>)current.History.Append(textToSubmit).ToList();
                current = current with { Text = "", Caret = 0, HistoryIndex = -1, History = newHistory };
                await ui.PatchAsync(UpdateInput(""));
                return (current, ChatInputAction.None);
            }
            return (current, ChatInputAction.Submit);
        }

        // Input-specific keys
        if (current.FocusKey != "input")
            return (current, ChatInputAction.None);

        // Submit (Enter without Shift); dispatches OnEnter if wired, else returns Submit for RunLoopAsync
        if (key.Key == ConsoleKey.Enter && (key.Modifiers & ConsoleModifiers.Shift) == 0)
        {
            var textToSubmit = current.Text;
            bool entered = await ui.DispatchEventAsync("input", "OnEnter", textToSubmit);
            if (entered)
            {
                // Handler owns submission; manage history and clear input here
                var newHistory = string.IsNullOrWhiteSpace(textToSubmit)
                    ? current.History
                    : (IReadOnlyList<string>)current.History.Append(textToSubmit).ToList();
                current = current with { Text = "", Caret = 0, HistoryIndex = -1, History = newHistory };
                await ui.PatchAsync(UpdateInput(""));
                return (current, ChatInputAction.None);
            }
            return (current, ChatInputAction.Submit);
        }

        // Shift+Enter inserts newline
        if (key.Key == ConsoleKey.Enter && (key.Modifiers & ConsoleModifiers.Shift) != 0)
        {
            var text = current.Text.Insert(current.Caret, "\n");
            current = current with { Text = text, Caret = current.Caret + 1 };
            await ui.PatchAsync(UpdateInput(current.Text));
            await ui.DispatchEventAsync("input", "OnChange", current.Text);
            return (current, ChatInputAction.None);
        }

        // Caret navigation: Home/End
        if (key.Key == ConsoleKey.Home)
        {
            current = current with { Caret = 0 };
            return (current, ChatInputAction.None);
        }
        if (key.Key == ConsoleKey.End)
        {
            current = current with { Caret = current.Text.Length };
            return (current, ChatInputAction.None);
        }

        // Word navigation with Ctrl+Arrows
        if (key.Key == ConsoleKey.LeftArrow && (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            var c = current.Caret;
            while (c > 0 && char.IsWhiteSpace(current.Text[c - 1])) c--;
            while (c > 0 && !char.IsWhiteSpace(current.Text[c - 1])) c--;
            current = current with { Caret = c };
            return (current, ChatInputAction.None);
        }
        if (key.Key == ConsoleKey.RightArrow && (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            var c = current.Caret;
            while (c < current.Text.Length && char.IsWhiteSpace(current.Text[c])) c++;
            while (c < current.Text.Length && !char.IsWhiteSpace(current.Text[c])) c++;
            current = current with { Caret = c };
            return (current, ChatInputAction.None);
        }

        // Backspace/Delete
        if (key.Key == ConsoleKey.Backspace)
        {
            if (current.Caret > 0)
            {
                var text = current.Text.Remove(current.Caret - 1, 1);
                current = current with { Text = text, Caret = current.Caret - 1 };
                await ui.PatchAsync(UpdateInput(current.Text));
                await ui.DispatchEventAsync("input", "OnChange", current.Text);
            }
            return (current, ChatInputAction.None);
        }
        if (key.Key == ConsoleKey.Delete)
        {
            if (current.Caret < current.Text.Length)
            {
                var text = current.Text.Remove(current.Caret, 1);
                current = current with { Text = text };
                await ui.PatchAsync(UpdateInput(current.Text));
                await ui.DispatchEventAsync("input", "OnChange", current.Text);
            }
            return (current, ChatInputAction.None);
        }

        // Printable character input — also exits history-browsing mode
        if (!char.IsControl(key.KeyChar))
        {
            var text = current.Text.Insert(current.Caret, key.KeyChar.ToString());
            current = current with { Text = text, Caret = current.Caret + 1, HistoryIndex = -1 };
            await ui.PatchAsync(UpdateInput(current.Text));
            await ui.DispatchEventAsync("input", "OnChange", current.Text);
            return (current, ChatInputAction.None);
        }

        // ---------------------------------------------------------------
        // Input history: Up Arrow when input is empty recalls the last
        // sent message.  While browsing, Up/Down move through entries.
        // Ctrl+Up / Ctrl+Down always scroll the messages panel.
        // ---------------------------------------------------------------

        bool isCtrl = (key.Modifiers & ConsoleModifiers.Control) != 0;

        // History navigation: Up Arrow (no Ctrl) while input is empty OR already browsing
        if (key.Key == ConsoleKey.UpArrow && !isCtrl &&
            (string.IsNullOrEmpty(current.Text) || current.HistoryIndex >= 0) &&
            current.History.Count > 0)
        {
            int nextIdx = current.HistoryIndex < 0
                ? 0                                              // start browsing from most recent
                : Math.Min(current.HistoryIndex + 1, current.History.Count - 1);

            // History is stored oldest-first; browsing from end
            int histPos = current.History.Count - 1 - nextIdx;
            var recalled = current.History[histPos];
            current = current with { Text = recalled, Caret = recalled.Length, HistoryIndex = nextIdx };
            await ui.PatchAsync(UpdateInput(current.Text));
            return (current, ChatInputAction.None);
        }

        if (key.Key == ConsoleKey.DownArrow && !isCtrl && current.HistoryIndex >= 0)
        {
            if (current.HistoryIndex == 0)
            {
                // Return to empty input
                current = current with { Text = "", Caret = 0, HistoryIndex = -1 };
            }
            else
            {
                int nextIdx = current.HistoryIndex - 1;
                int histPos = current.History.Count - 1 - nextIdx;
                var recalled = current.History[histPos];
                current = current with { Text = recalled, Caret = recalled.Length, HistoryIndex = nextIdx };
            }
            await ui.PatchAsync(UpdateInput(current.Text));
            return (current, ChatInputAction.None);
        }

        // Scroll the messages panel with Ctrl+Up/Ctrl+Down or PageUp/PageDown
        // Plain Up/Down only scroll when NOT in history-browsing mode and input is non-empty
        int page = Math.Max(3, (int)Math.Round(ui.Height * 0.7));
        bool scrolled = false;

        if      (key.Key == ConsoleKey.UpArrow   && (isCtrl || current.HistoryIndex < 0)) { current = current with { Scroll = Math.Max(0, current.Scroll + 1) };    scrolled = true; }
        else if (key.Key == ConsoleKey.DownArrow && (isCtrl || current.HistoryIndex < 0)) { current = current with { Scroll = Math.Max(0, current.Scroll - 1) };    scrolled = true; }
        else if (key.Key == ConsoleKey.PageUp)   { current = current with { Scroll = Math.Max(0, current.Scroll + page) }; scrolled = true; }
        else if (key.Key == ConsoleKey.PageDown) { current = current with { Scroll = Math.Max(0, current.Scroll - page) }; scrolled = true; }

        if (scrolled)
        {
            await ui.MakePatch()
                .Update(
                    UiFrameKeys.Messages,
                    new Dictionary<UiProperty, object?>
                    {
                        [UiProperty.AutoScroll] = false,
                        // Using Min as a simple numeric holder for scroll offset (from bottom)
                        [UiProperty.Min] = current.Scroll
                    })
                .PatchAsync();
            return (current, ChatInputAction.None);
        }

        return (current, ChatInputAction.None);
    }

    /// <summary>
    /// Creates the header/toolbar for the frame (thread title and action buttons)
    /// </summary>
    public static UiNode CreateHeader(string? threadName)
    {
        var title = string.IsNullOrEmpty(threadName) ? "Chat" : $"Chat: {threadName}";

        return Ui.Row("header")
            .WithChildren(
                Ui.Text("thread-title", title)
                    .WithStyles(Style.Combine(Style.AlignCenter, Style.Color(ConsoleColor.Cyan))),
                Ui.Spacer("spacer-header", new { Width = 1 })
            );
    }

    /// <summary>
    /// Creates the messages panel containing the conversation history
    /// </summary>
    private static UiNode CreateMessagesPanel(UiNode[] messageNodes)
    {
        return Ui.Column(UiFrameKeys.Messages, messageNodes)
            .WithProps(new { Scrollable = true, AutoScroll = true });
    }

    /// <summary>
    /// Creates a node for a single message bubble
    /// </summary>
    private static UiNode CreateMessageNode(ChatMessage message, int index)
    {
        var color = message.Role switch
        {
            Roles.User => ConsoleColor.White,
            Roles.Assistant => ConsoleColor.Green,
            Roles.System => ConsoleColor.Gray,
            Roles.Tool => ConsoleColor.Yellow,
            Roles.Progress => ConsoleColor.Cyan,
            _ => ConsoleColor.White
        };

        var roleLabel = message.Role.ToString();
        var timestamp = message.CreatedAt.ToString("HH:mm:ss");
        var header = $"[{timestamp}] {roleLabel}:";

        return Ui.Column($"msg-{message.Id}",
                Ui.Text($"msg-{message.Id}-header", header).WithStyles(Style.Color(color)),
                Ui.Text($"msg-{message.Id}-content", message.Content ?? string.Empty).WithStyles(Style.Combine(Style.Color(color), Style.Wrap))
            )
            .WithProps(new
            {
                Role = message.Role.ToString(),
                Timestamp = message.CreatedAt,
                State = message.State.ToString()
            });
    }

    /// <summary>
    /// Creates the composer area with input field and send button
    /// </summary>
    private static UiNode CreateComposer(string inputText, UiHandler? onSend, UiHandler? onInput)
    {
        var input = Ui.TextBox("input", inputText, "Type a message...")
            .WithProps(new
            {
                OnChange = onInput,
                OnEnter = onSend
            })
            .WithStyles(Style.Tag("left"));

        var sendBtn = Ui.Button(UiFrameKeys.SendButton, "Send", onSend)
            .WithProps(new { Enabled = !string.IsNullOrWhiteSpace(inputText) })
            .WithStyles(Style.Tag("right"));

        return Ui.Row("composer", input, sendBtn)
            .WithProps(new { Layout = "row-justify" })
            .WithStyles(Style.Combine(Style.AlignLeft, Style.Color(bg: ConsoleColor.DarkGray)));
    }

    /// <summary>
    /// Creates a patch to append a new message to the messages list
    /// </summary>
    public static UiPatch AppendMessage(ChatMessage message, int index)
    {
        var messageNode = CreateMessageNode(message, index);
        // Use int.MaxValue to clamp to append at end; the Term UI tree will append safely.
    return new UiPatch(new InsertChildOp(UiFrameKeys.Messages, int.MaxValue, messageNode));
    }

    /// <summary>
    /// Creates a patch to update a message's content (for streaming)
    /// </summary>
    public static UiPatch UpdateMessageContent(string messageId, string newContent)
    {
        return new UiPatch(
            new UpdatePropsOp(
                $"msg-{messageId}-content",
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = newContent
                }
            )
        );
    }

    /// <summary>
    /// Creates a patch to update the input text
    /// </summary>
    public static UiPatch UpdateInput(string text)
    {
        return new UiPatch(
            new UpdatePropsOp(
                "input",
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = text,
                    [UiProperty.Placeholder] = "Type a message..."
                }
            ),
            new UpdatePropsOp(
                UiFrameKeys.SendButton,
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = "Send",
                    [UiProperty.Enabled] = !string.IsNullOrWhiteSpace(text)
                }
            )
        );
    }

    /// <summary>
    /// Creates a patch to clear all messages
    /// </summary>
    public static UiPatch ClearMessages()
    {
        var empty = Ui.Column(UiFrameKeys.Messages)
            .WithProps(new { Scrollable = true, AutoScroll = true });
        return new UiPatch(new ReplaceOp(UiFrameKeys.Messages, empty));
    }

    /// <summary>
    /// Creates a patch to update multiple messages at once (batch update)
    /// </summary>
    public static UiPatch UpdateMessages(IEnumerable<ChatMessage> messages)
    {
        var container = Ui.Column(UiFrameKeys.Messages)
            .WithProps(new { Scrollable = true, AutoScroll = true })
            .ForEach(messages.Select((msg, index) => (msg, index)), t => CreateMessageNode(t.msg, t.index));
        return container.ToPatch(UiFrameKeys.Messages);
    }

    /// <summary>
    /// Creates a patch to update message state (e.g., for ephemeral messages)
    /// </summary>
    public static UiPatch UpdateMessageState(string messageId, ChatMessageState state)
    {
        // Note: This would require getting the current message content
        // For now, just update the state property
        return new UiPatch(
            new UpdatePropsOp(
                $"msg-{messageId}",
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.State] = state.ToString()
                }
            )
        );
    }

    /// <summary>
    /// Creates or updates an ephemeral realtime message in the messages panel
    /// These messages are not persisted to chat history but are scrollable
    /// </summary>
    public static UiPatch UpsertRealtimeMessage(string key, string content)
    {
        var realtimeMessage = new ChatMessage
        {
            Id = key,
            Role = Roles.System,
            Content = content,
            CreatedAt = DateTime.Now,
            State = ChatMessageState.EphemeralActive
        };

        // Check if node exists - if so, update; otherwise insert
        var messageNode = CreateMessageNode(realtimeMessage, -1);
        
        // Try to update first, fallback to insert if not found
        // The caller should handle the potential KeyNotFoundException
        return new UiPatch(
            new UpdatePropsOp(
                $"msg-{key}-content",
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = content
                }
            )
        );
    }

    /// <summary>
    /// Inserts a new realtime message node
    /// </summary>
    public static UiPatch InsertRealtimeMessage(string key, string content)
    {
        var realtimeMessage = new ChatMessage
        {
            Id = key,
            Role = Roles.System,
            Content = content,
            CreatedAt = DateTime.Now,
            State = ChatMessageState.EphemeralActive
        };

        var messageNode = CreateMessageNode(realtimeMessage, -1);
        
        // Insert at the end of messages panel (before composer)
    return new UiPatch(new InsertChildOp(UiFrameKeys.Messages, int.MaxValue, messageNode));
    }

    /// <summary>
    /// Removes a realtime message from the UI
    /// </summary>
    public static UiPatch RemoveRealtimeMessage(string key)
    {
        return new UiPatch(new RemoveOp($"msg-{key}"));
    }

    /// <summary>
    /// Processes chat input: adds user message, gets AI response, updates UI
    /// </summary>
    public static async Task ProcessChatInputAsync(
        IUi ui,
        string userInput,
        Context context,
        Func<Context, Task<(string response, Context updatedContext)>> getAIResponse)
    {
        if (string.IsNullOrWhiteSpace(userInput)) return;

        // Add user message to context
        context.AddUserMessage(userInput);
        var userMessage = context.Messages().Last();

        // Re-render full chat surface via reconciler (clears input and appends message)
        var currentMessages = context.Messages(InluceSystemMessage: false).ToList();
        await UpdateAsync(ui, new ChatVm(currentMessages, ""));

        try
        {
            // Get response from AI
            var (response, updatedContext) = await getAIResponse(context);

            // Persist assistant reply in conversation history if provider pipeline didn't
            var last = updatedContext.Messages().LastOrDefault();
            bool alreadyAdded = last != null && last.Role == Roles.Assistant && string.Equals(last.Content ?? string.Empty, response, StringComparison.Ordinal);
            if (!alreadyAdded)
            {
                updatedContext.AddAssistantMessage(response);
                last = updatedContext.Messages().LastOrDefault();
            }

            // Keep Program.Context in sync for other subsystems
            Program.Context = updatedContext;

            // Append assistant message and re-render
            currentMessages = updatedContext.Messages(InluceSystemMessage: false).ToList();
            await UpdateAsync(ui, new ChatVm(currentMessages, ""));
        }
        catch (Exception ex)
        {
            // Render an error message from assistant to keep the UI responsive
            var errorMsg = new ChatMessage
            {
                Role = Roles.Assistant,
                Content = $"Sorry, I ran into an error: {ex.Message}",
                CreatedAt = DateTime.Now
            };
            currentMessages = context.Messages(InluceSystemMessage: false).ToList();
            currentMessages.Add(errorMsg);
            await UpdateAsync(ui, new ChatVm(currentMessages, ""));
        }
    }
}