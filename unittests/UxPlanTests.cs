using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests for the functionality implemented in the UxRefactor plan:
///   Item 1b  — ConfirmOverlay.Create structure
///   Item 2   — ChatSurface input history (Up/Down arrow recall)
///   Item 4   — CUiBase.DispatchEventAsync + OnChange/OnEnter/OnClick wiring in HandleKeyAsync
/// Items 3 and 5 (ConsoleResizeWatcher, streaming debounce) require real console I/O
/// and are validated by integration testing rather than unit tests.
/// </summary>
public class UxPlanTests
{
    // ── Shared test infrastructure ─────────────────────────────────────────────

    /// <summary>
    /// Minimal CUiBase implementation: records applied patches, performs no rendering.
    /// </summary>
    private sealed class FakeUi : CUiBase
    {
        public readonly List<UiPatch> Patches = new();
        public readonly List<string> FocusedKeys = new();

        protected override Task PostSetRootAsync(UiNode root, UiControlOptions options) => Task.CompletedTask;
        protected override Task PostPatchAsync(UiPatch patch) { Patches.Add(patch); return Task.CompletedTask; }
        protected override Task PostFocusAsync(string key) { FocusedKeys.Add(key); return Task.CompletedTask; }

        public override Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public override Task RenderTableAsync(Table table, string? title = null) => Task.CompletedTask;
        public override Task RenderReportAsync(Report report) => Task.CompletedTask;
        public override IInputRouter GetInputRouter() => NullInputRouter.Instance;
        public override Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept) => Task.FromResult(default(ConsoleKeyInfo));
        public override int CursorTop => 0;
        public override int Width => 80;
        public override int Height => 24;
        public override bool KeyAvailable => false;
        public override bool IsOutputRedirected => false;
        public override void SetCursorPosition(int left, int top) { }
        public override ConsoleColor ForegroundColor { get; set; }
        public override ConsoleColor BackgroundColor { get; set; }
        public override void ResetColor() { }
        public override void Write(string text) { }
        public override void WriteLine(string? text = null) { }
        public override void Clear() { }
        public override Task RunAsync(Func<Task> appMain) => appMain();
    }

    private sealed class NullInputRouter : IInputRouter
    {
        public static readonly NullInputRouter Instance = new();
        public ConsoleKeyInfo? TryReadKey() => null;
    }

    /// <summary>
    /// Builds a minimal tree suitable for HandleKeyAsync:
    ///   chat-root (Column)
    ///     messages (Column)
    ///     composer (Row)
    ///       input    (TextBox)  — OnChange and OnEnter optionally wired
    ///       send-btn (Button)   — OnClick optionally wired
    /// </summary>
    private static UiNode MakeChatRoot(UiHandler? onInput = null, UiHandler? onSend = null)
    {
        var inputProps = new Dictionary<UiProperty, object?>
        {
            [UiProperty.Text]        = "",
            [UiProperty.Placeholder] = "Type a message..."
        };
        if (onInput != null) inputProps[UiProperty.OnChange] = onInput;
        if (onSend  != null) inputProps[UiProperty.OnEnter]  = onSend;

        var sendBtnProps = new Dictionary<UiProperty, object?>
        {
            [UiProperty.Text]    = "Send",
            [UiProperty.Enabled] = false
        };
        if (onSend != null) sendBtnProps[UiProperty.OnClick] = onSend;

        return new UiNode("chat-root", UiKind.Column,
            new Dictionary<UiProperty, object?>(),
            new UiNode[]
            {
                new UiNode("messages", UiKind.Column,
                    new Dictionary<UiProperty, object?> { [UiProperty.AutoScroll] = true },
                    Array.Empty<UiNode>()),
                new UiNode("composer", UiKind.Row,
                    new Dictionary<UiProperty, object?>(),
                    new UiNode[]
                    {
                        new UiNode("input", UiKind.TextBox, inputProps, Array.Empty<UiNode>()),
                        new UiNode(UiFrameKeys.SendButton, UiKind.Button, sendBtnProps, Array.Empty<UiNode>())
                    })
            });
    }

    /// <summary>Helper to construct a ConsoleKeyInfo without the full constructor verbosity.</summary>
    private static ConsoleKeyInfo Key(ConsoleKey key, char ch = '\0', ConsoleModifiers mod = 0) =>
        new ConsoleKeyInfo(ch, key,
            (mod & ConsoleModifiers.Shift)   != 0,
            (mod & ConsoleModifiers.Alt)     != 0,
            (mod & ConsoleModifiers.Control) != 0);

    // ── DispatchEventAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchEventAsync_Returns_False_When_Node_Not_Found()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(
            new UiNode("root", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>()));

        var result = await ui.DispatchEventAsync("nonexistent", "OnChange");

        Assert.False(result);
    }

    [Fact]
    public async Task DispatchEventAsync_Returns_False_For_Unknown_Event_Name()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(
            new UiNode("root", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>()));

        var result = await ui.DispatchEventAsync("root", "NotAUiProperty");

        Assert.False(result);
    }

    [Fact]
    public async Task DispatchEventAsync_Returns_False_When_Node_Has_No_Handler_Prop()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(
            new UiNode("btn", UiKind.Button,
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Go" },
                Array.Empty<UiNode>()));

        var result = await ui.DispatchEventAsync("btn", "OnClick");

        Assert.False(result);
    }

    [Fact]
    public async Task DispatchEventAsync_Returns_False_When_Prop_Is_Not_UiHandler()
    {
        var ui = new FakeUi();
        // OnClick set to a plain string, not a UiHandler delegate
        await ui.SetRootAsync(
            new UiNode("btn", UiKind.Button,
                new Dictionary<UiProperty, object?> { [UiProperty.OnClick] = "not-a-handler" },
                Array.Empty<UiNode>()));

        var result = await ui.DispatchEventAsync("btn", "OnClick");

        Assert.False(result);
    }

    [Fact]
    public async Task DispatchEventAsync_Invokes_Handler_Returns_True_And_Passes_Correct_Event()
    {
        UiEvent? received = null;
        UiHandler handler = e => { received = e; return Task.CompletedTask; };

        var ui = new FakeUi();
        await ui.SetRootAsync(
            new UiNode("btn", UiKind.Button,
                new Dictionary<UiProperty, object?> { [UiProperty.OnClick] = handler },
                Array.Empty<UiNode>()));

        var result = await ui.DispatchEventAsync("btn", "OnClick", "hello");

        Assert.True(result);
        Assert.NotNull(received);
        Assert.Equal("btn",     received!.Key);
        Assert.Equal("OnClick", received.Name);
        Assert.Equal("hello",   received.Value);
    }

    [Fact]
    public async Task DispatchEventAsync_Returns_False_And_Does_Not_Rethrow_When_Handler_Throws()
    {
        UiHandler handler = _ => throw new InvalidOperationException("boom");

        var ui = new FakeUi();
        await ui.SetRootAsync(
            new UiNode("btn", UiKind.Button,
                new Dictionary<UiProperty, object?> { [UiProperty.OnClick] = handler },
                Array.Empty<UiNode>()));

        // Must not propagate the exception
        var result = await ui.DispatchEventAsync("btn", "OnClick");

        Assert.False(result);
    }

    // ── Input history ──────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleKeyAsync_UpArrow_EmptyInput_RecallsMostRecentHistory()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot());

        // History stored oldest-first; "second" is index 1, "first" is index 0
        var state = new ChatSurface.ChatInputState
        {
            Text    = "",
            History = new[] { "first message", "second message" }
        };

        var (newState, action) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.UpArrow), state);

        Assert.Equal("second message", newState.Text); // most recent = last in list
        Assert.Equal(0, newState.HistoryIndex);         // browsing index 0 = newest
        Assert.Equal(ChatSurface.ChatInputAction.None, action);
    }

    [Fact]
    public async Task HandleKeyAsync_UpArrow_WhileBrowsing_RecallsOlderEntry()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot());

        var state = new ChatSurface.ChatInputState
        {
            Text         = "second message",
            HistoryIndex = 0,                                   // currently at newest
            History      = new[] { "first message", "second message" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.UpArrow), state);

        Assert.Equal("first message", newState.Text);
        Assert.Equal(1, newState.HistoryIndex);
    }

    [Fact]
    public async Task HandleKeyAsync_UpArrow_AtOldestEntry_ClampsToBoundary()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot());

        var state = new ChatSurface.ChatInputState
        {
            Text         = "first message",
            HistoryIndex = 1,                                   // already at oldest in 2-item history
            History      = new[] { "first message", "second message" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.UpArrow), state);

        // Should not go further back; text and index stay at oldest
        Assert.Equal("first message", newState.Text);
        Assert.Equal(1, newState.HistoryIndex);
    }

    [Fact]
    public async Task HandleKeyAsync_DownArrow_WhileBrowsing_MovesForward()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot());

        var state = new ChatSurface.ChatInputState
        {
            Text         = "first message",
            HistoryIndex = 1,
            History      = new[] { "first message", "second message" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.DownArrow), state);

        Assert.Equal("second message", newState.Text);
        Assert.Equal(0, newState.HistoryIndex);
    }

    [Fact]
    public async Task HandleKeyAsync_DownArrow_AtIndexZero_ClearsInputAndExitsBrowsing()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot());

        var state = new ChatSurface.ChatInputState
        {
            Text         = "second message",
            HistoryIndex = 0,
            History      = new[] { "first message", "second message" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.DownArrow), state);

        Assert.Equal("", newState.Text);
        Assert.Equal(-1, newState.HistoryIndex);
    }

    [Fact]
    public async Task HandleKeyAsync_PrintableKey_ExitsHistoryModeAndAppendsChar()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot());

        var state = new ChatSurface.ChatInputState
        {
            Text         = "recalled",
            Caret        = 8,            // caret at end, as history recall leaves it
            HistoryIndex = 0,
            History      = new[] { "recalled" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.A, 'a'), state);

        Assert.Equal(-1, newState.HistoryIndex);  // exits browsing mode
        Assert.Equal("recalleda", newState.Text); // char appended at caret position
    }

    [Fact]
    public async Task HandleKeyAsync_UpArrow_NonEmptyInputNotBrowsing_ScrollsMessagesNotHistory()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot());

        var state = new ChatSurface.ChatInputState
        {
            Text         = "some text",   // non-empty → history recall skipped
            HistoryIndex = -1,
            History      = new[] { "old" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.UpArrow), state);

        Assert.Equal(-1, newState.HistoryIndex); // did not enter history mode
        Assert.True(newState.Scroll > 0);        // scroll was incremented
    }

    [Fact]
    public async Task HandleKeyAsync_CtrlUpArrow_AlwaysScrollsEvenWithEmptyInput()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot());

        var state = new ChatSurface.ChatInputState
        {
            Text         = "",            // empty — would normally trigger history
            HistoryIndex = -1,
            History      = new[] { "old" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(
            ui, Key(ConsoleKey.UpArrow, '\0', ConsoleModifiers.Control), state);

        Assert.Equal(-1, newState.HistoryIndex); // Ctrl+Up never enters history mode
        Assert.True(newState.Scroll > 0);
    }

    // ── OnChange dispatch ──────────────────────────────────────────────────────

    [Fact]
    public async Task HandleKeyAsync_PrintableChar_DispatchesOnChange_WithNewText()
    {
        string? capturedValue = null;
        UiHandler onChange = e => { capturedValue = e.Value; return Task.CompletedTask; };

        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot(onInput: onChange));

        await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.H, 'h'),
            new ChatSurface.ChatInputState { Text = "" });

        Assert.Equal("h", capturedValue);
    }

    [Fact]
    public async Task HandleKeyAsync_Backspace_DispatchesOnChange()
    {
        int changeCount = 0;
        UiHandler onChange = _ => { changeCount++; return Task.CompletedTask; };

        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot(onInput: onChange));

        await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Backspace, '\b'),
            new ChatSurface.ChatInputState { Text = "hi", Caret = 2 });

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task HandleKeyAsync_Delete_DispatchesOnChange()
    {
        int changeCount = 0;
        UiHandler onChange = _ => { changeCount++; return Task.CompletedTask; };

        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot(onInput: onChange));

        // Caret at 0 with text "hi"; Delete removes 'h'
        await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Delete),
            new ChatSurface.ChatInputState { Text = "hi", Caret = 0 });

        Assert.Equal(1, changeCount);
    }

    // ── OnEnter dispatch ───────────────────────────────────────────────────────

    [Fact]
    public async Task HandleKeyAsync_Enter_NoOnEnterHandler_ReturnsSubmit()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot()); // no OnEnter wired

        var (_, action) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Enter, '\r'),
            new ChatSurface.ChatInputState { Text = "hello" });

        Assert.Equal(ChatSurface.ChatInputAction.Submit, action);
    }

    [Fact]
    public async Task HandleKeyAsync_Enter_WithOnEnterHandler_ReturnsNone_ClearsInput_InvokesHandler()
    {
        string? capturedText = null;
        UiHandler onSend = e => { capturedText = e.Value; return Task.CompletedTask; };

        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot(onSend: onSend));

        var (newState, action) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Enter, '\r'),
            new ChatSurface.ChatInputState { Text = "hello world", Caret = 11 });

        Assert.Equal(ChatSurface.ChatInputAction.None, action); // handler owns submission
        Assert.Equal("hello world", capturedText);              // handler received the text
        Assert.Equal("", newState.Text);                        // input cleared
        Assert.Equal(0, newState.Caret);
    }

    [Fact]
    public async Task HandleKeyAsync_Enter_WithOnEnterHandler_AppendsSubmittedTextToHistory()
    {
        UiHandler onSend = _ => Task.CompletedTask;

        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot(onSend: onSend));

        var state = new ChatSurface.ChatInputState
        {
            Text    = "new message",
            History = new[] { "old message" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Enter, '\r'), state);

        Assert.Equal(2, newState.History.Count);
        Assert.Equal("new message", newState.History[1]); // appended at end (oldest-first order)
    }

    [Fact]
    public async Task HandleKeyAsync_Enter_WithOnEnterHandler_ResetsHistoryIndex()
    {
        UiHandler onSend = _ => Task.CompletedTask;

        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot(onSend: onSend));

        // Simulate entering text while in browsing mode and then pressing Enter
        var state = new ChatSurface.ChatInputState
        {
            Text         = "edited recalled text",
            HistoryIndex = 1,
            History      = new[] { "old a", "old b" }
        };

        var (newState, _) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Enter, '\r'), state);

        Assert.Equal(-1, newState.HistoryIndex); // history mode exited after submit
    }

    // ── OnClick dispatch (send button) ─────────────────────────────────────────

    [Fact]
    public async Task HandleKeyAsync_SendButton_NoOnClickHandler_ReturnsSubmit()
    {
        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot()); // no OnClick wired on send-btn

        var (_, action) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Enter, '\r'),
            new ChatSurface.ChatInputState
            {
                Text     = "hello",
                FocusKey = UiFrameKeys.SendButton
            });

        Assert.Equal(ChatSurface.ChatInputAction.Submit, action);
    }

    [Fact]
    public async Task HandleKeyAsync_SendButton_WithOnClickHandler_ReturnsNone_ClearsInput()
    {
        string? capturedText = null;
        UiHandler onSend = e => { capturedText = e.Value; return Task.CompletedTask; };

        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot(onSend: onSend));

        var (newState, action) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Enter, '\r'),
            new ChatSurface.ChatInputState
            {
                Text     = "click send",
                FocusKey = UiFrameKeys.SendButton
            });

        Assert.Equal(ChatSurface.ChatInputAction.None, action);
        Assert.Equal("click send", capturedText);
        Assert.Equal("", newState.Text);
    }

    [Fact]
    public async Task HandleKeyAsync_SendButton_Spacebar_WithOnClickHandler_ReturnsNone()
    {
        UiHandler onSend = _ => Task.CompletedTask;

        var ui = new FakeUi();
        await ui.SetRootAsync(MakeChatRoot(onSend: onSend));

        var (_, action) = await ChatSurface.HandleKeyAsync(ui, Key(ConsoleKey.Spacebar, ' '),
            new ChatSurface.ChatInputState
            {
                Text     = "space submit",
                FocusKey = UiFrameKeys.SendButton
            });

        Assert.Equal(ChatSurface.ChatInputAction.None, action);
    }

    // ── ConfirmOverlay.Create ──────────────────────────────────────────────────

    [Fact]
    public void ConfirmOverlay_Create_ProducesCorrectNodeStructure()
    {
        var node = ConfirmOverlay.Create("Delete this file?", yesDefault: false);

        Assert.Equal("overlay-confirm", node.Key);
        Assert.Equal(UiKind.Column, node.Kind);
        Assert.True((bool)node.Props[UiProperty.Modal]!);

        // Three children: question label, spacer, buttons row
        Assert.Equal(3, node.Children.Count);

        var question = node.Children[0];
        Assert.Equal("overlay-confirm-question", question.Key);
        Assert.Equal("Delete this file?", question.Props[UiProperty.Text]);

        Assert.Equal("overlay-confirm-spacer", node.Children[1].Key);

        var buttonsRow = node.Children[2];
        Assert.Equal("overlay-confirm-buttons", buttonsRow.Key);
        Assert.Equal(UiKind.Row, buttonsRow.Kind);

        Assert.Equal(2, buttonsRow.Children.Count);
        Assert.Equal("overlay-confirm-yes", buttonsRow.Children[0].Key);
        Assert.Equal("overlay-confirm-no",  buttonsRow.Children[1].Key);
    }

    [Fact]
    public void ConfirmOverlay_Create_YesDefault_False_HighlightsNoButton()
    {
        var node = ConfirmOverlay.Create("Sure?", yesDefault: false);

        var buttons = node.Children[2]; // overlay-confirm-buttons
        var yesText = (string?)buttons.Children[0].Props[UiProperty.Text];
        var noText  = (string?)buttons.Children[1].Props[UiProperty.Text];

        // No-default: Yes is plain, No has brackets
        Assert.Equal(" Yes ", yesText);
        Assert.Equal("[No] ", noText);
    }

    [Fact]
    public void ConfirmOverlay_Create_YesDefault_True_HighlightsYesButton()
    {
        var node = ConfirmOverlay.Create("Sure?", yesDefault: true);

        var buttons = node.Children[2];
        var yesText = (string?)buttons.Children[0].Props[UiProperty.Text];
        var noText  = (string?)buttons.Children[1].Props[UiProperty.Text];

        // Yes-default: Yes has brackets, No is plain
        Assert.Equal("[Yes]", yesText);
        Assert.Equal(" No ", noText);
    }
}
