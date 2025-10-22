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
    // Encapsulate input state for chat composer
    public sealed record ChatInputState
    {
        public string Text { get; init; } = "";
        public int Caret { get; init; } = 0;
        public string FocusKey { get; set; } = "input"; // "input" or "send-btn"
        public int Scroll { get; init; } = 0;
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
        var messageNodes = messages
            .Select((msg, index) => CreateMessageNode(msg, index))
            .ToArray();

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

        return new UiNode(
            "chat-root",
            UiKind.Column,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.Layout] = "dock-bottom"
            },
            new UiNode[]
            {
                CreateMessagesPanel(messageNodes),
                new UiNode("spacer", UiKind.Spacer, new Dictionary<UiProperty, object?> { [UiProperty.Height] = 1 }, Array.Empty<UiNode>()),                
                CreateComposer(string.Empty, onSend, onInput)
            }
        );
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
                await ui.FocusAsync("send-btn");
                current.FocusKey = "send-btn";
            }
            else
            {
                await ui.FocusAsync("input");
                current.FocusKey = "input";
            }
            return (current, ChatInputAction.None);
        }

        // If focus is on send button, Enter/Space triggers submit
        if (current.FocusKey == "send-btn" && (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Spacebar))
        {
            return (current, ChatInputAction.Submit);
        }

        // Input-specific keys
        if (current.FocusKey != "input")
            return (current, ChatInputAction.None);

        // Submit (Enter without Shift)
        if (key.Key == ConsoleKey.Enter && (key.Modifiers & ConsoleModifiers.Shift) == 0)
        {
            return (current, ChatInputAction.Submit);
        }

        // Shift+Enter inserts newline
        if (key.Key == ConsoleKey.Enter && (key.Modifiers & ConsoleModifiers.Shift) != 0)
        {
            var text = current.Text.Insert(current.Caret, "\n");
            current = current with { Text = text, Caret = current.Caret + 1 };
            await ui.PatchAsync(UpdateInput(current.Text));
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
            }
            return (current, ChatInputAction.None);
        }

        // Printable character input
        if (!char.IsControl(key.KeyChar))
        {
            var text = current.Text.Insert(current.Caret, key.KeyChar.ToString());
            current = current with { Text = text, Caret = current.Caret + 1 };
            await ui.PatchAsync(UpdateInput(current.Text));
            return (current, ChatInputAction.None);
        }

        // Scroll the messages panel with Up/Down/PageUp/PageDown/Home/End
        // We model scroll as an offset from the bottom (0 = bottom, larger = scrolled up)
        int page = Math.Max(3, (int)Math.Round(ui.Height * 0.7));
        bool scrolled = false;

        if (key.Key == ConsoleKey.UpArrow) { current = current with { Scroll = Math.Max(0, current.Scroll + 1) }; scrolled = true; }
        else if (key.Key == ConsoleKey.DownArrow) { current = current with { Scroll = Math.Max(0, current.Scroll - 1) }; scrolled = true; }
        else if (key.Key == ConsoleKey.PageUp) { current = current with { Scroll = Math.Max(0, current.Scroll + page) }; scrolled = true; }
        else if (key.Key == ConsoleKey.PageDown) { current = current with { Scroll = Math.Max(0, current.Scroll - page) }; scrolled = true; }
        else if (key.Key == ConsoleKey.Home) { current = current with { Scroll = int.MaxValue }; scrolled = true; }
        else if (key.Key == ConsoleKey.End) { current = current with { Scroll = 0 }; scrolled = true; }

        if (scrolled)
        {
            await ui.MakePatch()
                .Update(
                    "messages",
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
        
        var titleProps = new Dictionary<UiProperty, object?>
        {
            [UiProperty.Text] = title
        };

        var spacerProps = new Dictionary<UiProperty, object?>
        {
            [UiProperty.Width] = 1
        };

        return new UiNode(
            "header",
            UiKind.Row,
            new Dictionary<UiProperty, object?>(),
            new[]
            {
                new UiNode("thread-title", UiKind.Label, titleProps, Array.Empty<UiNode>(),
                    UiStyles.Of((UiStyleKey.Align, "center"), (UiStyleKey.ForegroundColor, ConsoleColor.Cyan))),
                new UiNode("spacer-header", UiKind.Spacer, spacerProps, Array.Empty<UiNode>())
            }
        );
    }

    /// <summary>
    /// Creates the messages panel containing the conversation history
    /// </summary>
    private static UiNode CreateMessagesPanel(UiNode[] messageNodes)
    {
        return new UiNode(
            "messages",
            UiKind.Column,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.Scrollable] = true,
                [UiProperty.AutoScroll] = true
            },
            messageNodes
        );
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

        var props = new Dictionary<UiProperty, object?>
        {
            [UiProperty.Role] = message.Role.ToString(),
            [UiProperty.Timestamp] = message.CreatedAt,
            [UiProperty.State] = message.State.ToString()
        };

        var children = new List<UiNode>
        {
            new UiNode(
                $"msg-{message.Id}-header",
                UiKind.Label,
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = header
                },
                Array.Empty<UiNode>(), UiStyles.Of((UiStyleKey.ForegroundColor, color))),
            new UiNode(
                $"msg-{message.Id}-content",
                UiKind.Label,
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.Text] = message.Content ?? ""
                },
                Array.Empty<UiNode>(), UiStyles.Of((UiStyleKey.ForegroundColor, color), (UiStyleKey.Wrap, true)))
        };

        return new UiNode(
            $"msg-{message.Id}",
            UiKind.Column,
            props,
            children.ToArray()
        );
    }

    /// <summary>
    /// Creates the composer area with input field and send button
    /// </summary>
    private static UiNode CreateComposer(string inputText, UiHandler? onSend, UiHandler? onInput)
    {
        var inputProps = new Dictionary<UiProperty, object?>
        {
            [UiProperty.Text] = inputText,
            [UiProperty.Placeholder] = "Type a message..."
        };

        if (onInput != null)
        {
            inputProps[UiProperty.OnChange] = onInput;
        }

        // Also wire up Enter key to trigger send
        if (onSend != null)
        {
            inputProps[UiProperty.OnEnter] = onSend;
        }

        var sendButtonProps = new Dictionary<UiProperty, object?>
        {
            [UiProperty.Text] = "Send",
            [UiProperty.Enabled] = !string.IsNullOrWhiteSpace(inputText)
        };

        if (onSend != null)
        {
            sendButtonProps[UiProperty.OnClick] = onSend;
        }
        var composerStyle = UiStyles.Of(
            (UiStyleKey.Align, "bottom"),
            (UiStyleKey.BackgroundColor, ConsoleColor.DarkGray)
        );
        var inputStyle = UiStyles.Of(
            (UiStyleKey.Justify, "left")
        );
        var buttonStyle = UiStyles.Of(
            (UiStyleKey.Justify, "right")
        );

        return new UiNode(
            "composer",
            UiKind.Row,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.Layout] = "row-justify"
            },
            new[]
            {
                new UiNode("input", UiKind.TextBox, inputProps, Array.Empty<UiNode>(), inputStyle),
                new UiNode("send-btn", UiKind.Button, sendButtonProps, Array.Empty<UiNode>(), buttonStyle)
            },
            composerStyle
        );
    }

    /// <summary>
    /// Creates a patch to append a new message to the messages list
    /// </summary>
    public static UiPatch AppendMessage(ChatMessage message, int index)
    {
        var messageNode = CreateMessageNode(message, index);
        // Use int.MaxValue to clamp to append at end; the Term UI tree will append safely.
        return new UiPatch(new InsertChildOp("messages", int.MaxValue, messageNode));
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
                    [UiProperty.Text] = newContent,
                    [UiProperty.Wrap] = true
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
                "send-btn",
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
        return new UiPatch(
            new ReplaceOp(
                "messages",
                new UiNode(
                    "messages",
                    UiKind.Column,
                    new Dictionary<UiProperty, object?>
                    {
                        [UiProperty.Scrollable] = true,
                        [UiProperty.AutoScroll] = true
                    },
                    Array.Empty<UiNode>()
                )
            )
        );
    }

    /// <summary>
    /// Creates a patch to update multiple messages at once (batch update)
    /// </summary>
    public static UiPatch UpdateMessages(IEnumerable<ChatMessage> messages)
    {
        var messageNodes = messages
            .Select((msg, index) => CreateMessageNode(msg, index))
            .ToArray();

        return new UiPatch(
            new ReplaceOp(
                "messages",
                new UiNode(
                    "messages",
                    UiKind.Column,
                    new Dictionary<UiProperty, object?>
                    {
                        [UiProperty.Scrollable] = true,
                        [UiProperty.AutoScroll] = true
                    },
                    messageNodes
                )
            )
        );
    }

    /// <summary>
    /// Creates a patch to update message state (e.g., for ephemeral messages)
    /// </summary>
    public static UiPatch UpdateMessageState(string messageId, ChatMessageState state)
    {
        var node = new UiNode(
            $"msg-{messageId}",
            UiKind.Column,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.State] = state.ToString()
            },
            Array.Empty<UiNode>()
        );

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
                    [UiProperty.Text] = content,
                    [UiProperty.Wrap] = true
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
        return new UiPatch(new InsertChildOp("messages", int.MaxValue, messageNode));
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
        
        // Render user message via ChatSurface
        var currentMessages = context.Messages(InluceSystemMessage: false).ToList();
        await ui.PatchAsync(AppendMessage(userMessage, currentMessages.Count - 1));

        // Clear the input box
        await ui.PatchAsync(UpdateInput(""));
        // Ensure view snaps to bottom after sending
        await ui.MakePatch()
            .Update(
                "messages",
                new Dictionary<UiProperty, object?>
                {
                    [UiProperty.AutoScroll] = true,
                    [UiProperty.Min] = 0
                })
            .PatchAsync();

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

            // Append assistant message bubble
            currentMessages = updatedContext.Messages(InluceSystemMessage: false).ToList();
            if (last != null)
            {
                await ui.PatchAsync(AppendMessage(last, currentMessages.Count - 1));
            }
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
            await ui.PatchAsync(AppendMessage(errorMsg, currentMessages.Count - 1));
        }
    }
}