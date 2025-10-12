using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    /// <param name="inputText">Current text in the input field</param>
    /// <param name="onSend">Handler for send button click</param>
    /// <param name="onInput">Handler for input text changes</param>
    /// <returns>Root UiNode representing the chat surface content</returns>
    public static UiNode Create(
        IEnumerable<ChatMessage> messages,
        string inputText = "",
        UiHandler? onSend = null,
        UiHandler? onInput = null)
    {
        var messageNodes = messages
            .Select((msg, index) => CreateMessageNode(msg, index))
            .ToArray();

        return new UiNode(
            "chat-root",
            UiKind.Column,
            new Dictionary<string, object?>(),
            new UiNode[]
            {
                CreateMessagesPanel(messageNodes),
                new UiNode("spacer", UiKind.Spacer, new Dictionary<string, object?> { ["height"] = 1 }, Array.Empty<UiNode>()),
                CreateComposer(inputText, onSend, onInput)
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

        // Up/Down/PageUp/PageDown reserved for scroll – not implemented yet
        return (current, ChatInputAction.None);
    }

    /// <summary>
    /// Creates the header/toolbar for the frame (thread title and action buttons)
    /// </summary>
    public static UiNode CreateHeader(string? threadName, UiHandler? onClear)
    {
        var title = string.IsNullOrEmpty(threadName) ? "Chat" : $"Chat: {threadName}";
        
        var props = new Dictionary<string, object?>
        {
            ["text"] = title,
            ["color"] = ConsoleColor.Cyan
        };

        var clearButtonProps = new Dictionary<string, object?>
        {
            ["text"] = "Clear"
        };
        
        if (onClear != null)
        {
            clearButtonProps["onClick"] = onClear;
        }

        return new UiNode(
            "header",
            UiKind.Row,
            new Dictionary<string, object?>(),
            new[]
            {
                new UiNode("thread-title", UiKind.Label, props, Array.Empty<UiNode>()),
                new UiNode("spacer-header", UiKind.Spacer, new Dictionary<string, object?> { ["width"] = 2 }, Array.Empty<UiNode>()),
                new UiNode("clear-btn", UiKind.Button, clearButtonProps, Array.Empty<UiNode>())
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
            new Dictionary<string, object?>
            {
                ["scrollable"] = true,
                ["autoScroll"] = true
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

        var props = new Dictionary<string, object?>
        {
            ["role"] = message.Role.ToString(),
            ["timestamp"] = message.CreatedAt,
            ["state"] = message.State.ToString()
        };

        var children = new List<UiNode>
        {
            new UiNode(
                $"msg-{message.Id}-header",
                UiKind.Label,
                new Dictionary<string, object?>
                {
                    ["text"] = header,
                    ["color"] = color
                },
                Array.Empty<UiNode>()
            ),
            new UiNode(
                $"msg-{message.Id}-content",
                UiKind.Label,
                new Dictionary<string, object?>
                {
                    ["text"] = message.Content ?? "",
                    ["color"] = color,
                    ["wrap"] = true
                },
                Array.Empty<UiNode>()
            )
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
        var inputProps = new Dictionary<string, object?>
        {
            ["text"] = inputText,
            ["placeholder"] = "Type a message..."
        };

        if (onInput != null)
        {
            inputProps["onChange"] = onInput;
        }

        // Also wire up Enter key to trigger send
        if (onSend != null)
        {
            inputProps["onEnter"] = onSend;
        }

        var sendButtonProps = new Dictionary<string, object?>
        {
            ["text"] = "Send",
            ["enabled"] = !string.IsNullOrWhiteSpace(inputText)
        };

        if (onSend != null)
        {
            sendButtonProps["onClick"] = onSend;
        }

        return new UiNode(
            "composer",
            UiKind.Row,
            new Dictionary<string, object?>(),
            new[]
            {
                new UiNode("input", UiKind.TextBox, inputProps, Array.Empty<UiNode>()),
                new UiNode("send-btn", UiKind.Button, sendButtonProps, Array.Empty<UiNode>())
            }
        );
    }

    /// <summary>
    /// Creates a patch to append a new message to the messages list
    /// </summary>
    public static UiPatch AppendMessage(ChatMessage message, int index)
    {
        var messageNode = CreateMessageNode(message, index);
        return new UiPatch(new InsertChildOp("messages", index, messageNode));
    }

    /// <summary>
    /// Creates a patch to update a message's content (for streaming)
    /// </summary>
    public static UiPatch UpdateMessageContent(string messageId, string newContent)
    {
        return new UiPatch(
            new UpdatePropsOp(
                $"msg-{messageId}-content",
                new Dictionary<string, object?>
                {
                    ["text"] = newContent,
                    ["wrap"] = true
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
                new Dictionary<string, object?>
                {
                    ["text"] = text,
                    ["placeholder"] = "Type a message..."
                }
            ),
            new UpdatePropsOp(
                "send-btn",
                new Dictionary<string, object?>
                {
                    ["text"] = "Send",
                    ["enabled"] = !string.IsNullOrWhiteSpace(text)
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
                    new Dictionary<string, object?>
                    {
                        ["scrollable"] = true,
                        ["autoScroll"] = true
                    },
                    Array.Empty<UiNode>()
                )
            )
        );
    }

    /// <summary>
    /// Creates a patch to update the thread name in the toolbar
    /// </summary>
    public static UiPatch UpdateThreadName(string threadName)
    {
        var title = string.IsNullOrEmpty(threadName) ? "Chat" : $"Chat: {threadName}";
        return new UiPatch(
            new UpdatePropsOp(
                "thread-title",
                new Dictionary<string, object?>
                {
                    ["text"] = title,
                    ["color"] = ConsoleColor.Cyan
                }
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
                    new Dictionary<string, object?>
                    {
                        ["scrollable"] = true,
                        ["autoScroll"] = true
                    },
                    messageNodes
                )
            )
        );
    }

    /// <summary>
    /// Creates a patch to remove a specific message
    /// </summary>
    public static UiPatch RemoveMessage(string messageId)
    {
        return new UiPatch(new RemoveOp($"msg-{messageId}"));
    }

    /// <summary>
    /// Creates a patch to update message state (e.g., for ephemeral messages)
    /// </summary>
    public static UiPatch UpdateMessageState(string messageId, ChatMessageState state)
    {
        var node = new UiNode(
            $"msg-{messageId}",
            UiKind.Column,
            new Dictionary<string, object?>
            {
                ["state"] = state.ToString()
            },
            Array.Empty<UiNode>()
        );

        // Note: This would require getting the current message content
        // For now, just update the state property
        return new UiPatch(
            new UpdatePropsOp(
                $"msg-{messageId}",
                new Dictionary<string, object?>
                {
                    ["state"] = state.ToString()
                }
            )
        );
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

        try
        {
            // Get response from AI
            var (response, updatedContext) = await getAIResponse(context);
            
            // Create assistant message
            var assistantMessage = new ChatMessage 
            { 
                Role = Roles.Assistant, 
                Content = response,
                CreatedAt = DateTime.Now 
            };
            
            // Add assistant message to context (already done by getAIResponse, but update our reference)
            currentMessages = updatedContext.Messages(InluceSystemMessage: false).ToList();
            
            // Append assistant message bubble
            await ui.PatchAsync(AppendMessage(assistantMessage, currentMessages.Count - 1));
            
            // Update content (for streaming, this would be called incrementally)
            await ui.PatchAsync(UpdateMessageContent(assistantMessage.Id, response));
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

    /// <summary>
    /// Helper method to mount the chat surface with default options
    /// </summary>
    public static async Task MountAsync(
        IUi ui,
        IEnumerable<ChatMessage> messages,
        string inputText = "",
        UiHandler? onSend = null,
        UiHandler? onInput = null)
    {
        var surface = Create(messages, inputText, onSend, onInput);
        await ui.SetRootAsync(surface, new UiControlOptions(TrapKeys: true, InitialFocusKey: "input"));
    }
}
