using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Three-pane mail client overlay: Folder sidebar | Message list | Reading pane.
///
/// Rendering is fully UiNode-based (no direct console writes).
/// Colors and styles are referenced from <see cref="MailClientTheme"/> and kept
/// separate from layout/interaction logic so themes can later be swapped at runtime.
///
/// Entry points:
///   ShowAsync       — full 3-pane mail client (used by the "client" command)
///   ShowMessageAsync — single-message reading view (used by "summarize" drill-in)
/// </summary>
public static class MailClientOverlay
{
    // ── Internal state ─────────────────────────────────────────────────────

    private enum ActivePanel { Sidebar, MessageList, ReadingPane }

    private sealed record MailState(
        int FolderIndex,
        List<IMailMessage> Messages,
        int MessageIndex,           // -1 = none selected
        IMailMessage? OpenMessage,
        string[] BodyLines,         // raw lines of the open message body
        int BodyScroll,             // index of the first visible body line
        ActivePanel Panel,
        string Status
    );

    // Stable overlay root keys
    private const string ClientKey  = "mail-client";
    private const string MsgViewKey = "mail-msgview";

    // ── Public: 3-pane mail client ─────────────────────────────────────────

    public static async Task ShowAsync(IUi ui, IMailProvider provider, List<FavoriteMailFolder> folders)
    {
        // Available height for each column's list view:
        //   screen - 2 (overlay borders) - 2 (toolbar + status rows) - 2 (box margin) = screen - 6
        int viewH = Math.Max(6, Program.ui.Height - 6);

        var state = new MailState(
            FolderIndex: 0, Messages: new(), MessageIndex: -1,
            OpenMessage: null, BodyLines: Array.Empty<string>(), BodyScroll: 0,
            Panel: ActivePanel.MessageList, Status: "Loading…");

        // Push overlay (loading state) so the user sees immediate feedback
        var node = BuildClientNode(state, folders, viewH);
        await ui.PatchAsync(UiFrameBuilder.PushOverlay(node));
        var prev = node;
        await ui.FocusAsync("mail-list-items");

        // Load the first folder and auto-preview its first message
        state = await LoadFolderAsync(state, provider, folders, 0);
        state = await AutoPreviewAsync(state);
        var loaded = BuildClientNode(state, folders, viewH);
        await ui.ReconcileAsync(prev, loaded);
        prev = loaded;

        // ── Local navigation helpers (capture folders for sidebar bounds) ──

        MailState MoveUp(MailState s) => s.Panel switch
        {
            ActivePanel.Sidebar     => s with { FolderIndex = Math.Max(0, s.FolderIndex - 1) },
            ActivePanel.MessageList => s.Messages.Count > 0
                                        ? s with { MessageIndex = Math.Max(0, s.MessageIndex - 1) }
                                        : s,
            ActivePanel.ReadingPane => s.BodyLines.Length > 0
                                        ? s with { BodyScroll = Math.Max(0, s.BodyScroll - 1) }
                                        : s,
            _                       => s
        };

        MailState MoveDown(MailState s) => s.Panel switch
        {
            ActivePanel.Sidebar     => s with { FolderIndex = Math.Min(folders.Count - 1, s.FolderIndex + 1) },
            ActivePanel.MessageList => s.Messages.Count > 0
                                        ? s with { MessageIndex = Math.Min(s.Messages.Count - 1, s.MessageIndex + 1) }
                                        : s,
            ActivePanel.ReadingPane => s.BodyLines.Length > 0
                                        ? s with { BodyScroll = Math.Min(s.BodyLines.Length - 1, s.BodyScroll + 1) }
                                        : s,
            _                       => s
        };

        // Auto-preview: fetch and populate reading pane whenever MessageIndex changes.
        // Panel stays on MessageList; Enter simply shifts keyboard focus to the pane.
        async Task<MailState> AutoPreviewAsync(MailState s)
        {
            if (s.Panel != ActivePanel.MessageList) return s;
            if (s.MessageIndex < 0 || s.MessageIndex >= s.Messages.Count)
                return s with { OpenMessage = null, BodyLines = Array.Empty<string>(), BodyScroll = 0 };
            try
            {
                var full = await provider.GetMessageAsync(s.Messages[s.MessageIndex].Id);
                if (full != null)
                    return s with
                    {
                        OpenMessage = full,
                        BodyLines   = SplitBody(full.BodyPreview ?? ""),
                        BodyScroll  = 0,
                        Status      = Utilities.TruncatePlain(full.Subject ?? "(no subject)", 80)
                    };
            }
            catch { }
            return s with { OpenMessage = null, BodyLines = Array.Empty<string>(), BodyScroll = 0 };
        }

        // ── Main input loop ────────────────────────────────────────────────
        var router = ui.GetInputRouter();
        while (true)
        {
            var k = router.TryReadKey();
            if (k is null) { await Task.Delay(10); continue; }
            var key = k.Value;

            MailState? next = null;

            if (key.Key == ConsoleKey.Tab)
            {
                // Rotate focus: Sidebar → MessageList → ReadingPane (if open) → Sidebar
                next = state with
                {
                    Panel = state.Panel switch
                    {
                        ActivePanel.Sidebar     => ActivePanel.MessageList,
                        ActivePanel.MessageList => state.OpenMessage != null
                                                    ? ActivePanel.ReadingPane
                                                    : ActivePanel.Sidebar,
                        ActivePanel.ReadingPane => ActivePanel.Sidebar,
                        _                       => ActivePanel.Sidebar
                    }
                };
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                if (state.Panel == ActivePanel.ReadingPane)
                {
                    // Email action menu; if cancelled, stay in reading pane
                    next = await HandleActionsAsync(ui, provider, state, folders);
                }
                else if (state.Panel == ActivePanel.ReadingPane)
                {
                    next = state with { Panel = ActivePanel.MessageList };
                }
                else
                {
                    break; // exit mail client
                }
            }
            else if (key.Key == ConsoleKey.UpArrow)
            {
                var moved = MoveUp(state);
                if (state.Panel == ActivePanel.Sidebar && moved.FolderIndex != state.FolderIndex)
                {
                    moved = await LoadFolderAsync(moved, provider, folders, moved.FolderIndex);
                    moved = await AutoPreviewAsync(moved);
                }
                else if (state.Panel == ActivePanel.MessageList && moved.MessageIndex != state.MessageIndex)
                    moved = await AutoPreviewAsync(moved);
                next = moved;
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                var moved = MoveDown(state);
                if (state.Panel == ActivePanel.Sidebar && moved.FolderIndex != state.FolderIndex)
                {
                    moved = await LoadFolderAsync(moved, provider, folders, moved.FolderIndex);
                    moved = await AutoPreviewAsync(moved);
                }
                else if (state.Panel == ActivePanel.MessageList && moved.MessageIndex != state.MessageIndex)
                    moved = await AutoPreviewAsync(moved);
                next = moved;
            }
            else if (key.Key == ConsoleKey.PageUp)
            {
                next = state.Panel switch
                {
                    ActivePanel.MessageList => state with { MessageIndex = Math.Max(0, state.MessageIndex - 10) },
                    ActivePanel.ReadingPane => state with { BodyScroll   = Math.Max(0, state.BodyScroll   - 10) },
                    _                       => null
                };
                if (next?.Panel == ActivePanel.MessageList && next.MessageIndex != state.MessageIndex)
                    next = await AutoPreviewAsync(next);
            }
            else if (key.Key == ConsoleKey.PageDown)
            {
                next = state.Panel switch
                {
                    ActivePanel.MessageList => state with { MessageIndex = Math.Min(Math.Max(0, state.Messages.Count - 1),    state.MessageIndex + 10) },
                    ActivePanel.ReadingPane => state with { BodyScroll   = Math.Min(Math.Max(0, state.BodyLines.Length - 1), state.BodyScroll   + 10) },
                    _                       => null
                };
                if (next?.Panel == ActivePanel.MessageList && next.MessageIndex != state.MessageIndex)
                    next = await AutoPreviewAsync(next);
            }
            else if (key.Key == ConsoleKey.Home)
            {
                if      (state.Panel == ActivePanel.MessageList) next = await AutoPreviewAsync(state with { MessageIndex = 0 });
                else if (state.Panel == ActivePanel.ReadingPane) next = state with { BodyScroll = 0 };
            }
            else if (key.Key == ConsoleKey.End)
            {
                if      (state.Panel == ActivePanel.MessageList) next = await AutoPreviewAsync(state with { MessageIndex = Math.Max(0, state.Messages.Count   - 1) });
                else if (state.Panel == ActivePanel.ReadingPane) next = state with { BodyScroll = Math.Max(0, state.BodyLines.Length - 1) };
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                if (state.Panel == ActivePanel.Sidebar)
                {
                    // (Re-)load the selected folder, auto-preview first message, switch to list
                    var reloaded = await LoadFolderAsync(
                        state with { Panel = ActivePanel.MessageList },
                        provider, folders, state.FolderIndex);
                    next = await AutoPreviewAsync(reloaded);
                }
                else if (state.Panel == ActivePanel.MessageList && state.OpenMessage != null)
                {
                    // Message already loaded via auto-preview — just move focus to the reading pane
                    next = state with { Panel = ActivePanel.ReadingPane };
                }
            }
            else if (state.OpenMessage != null)
            {
                // Quick-key shortcuts when a message is open
                char ch = char.ToLowerInvariant(key.KeyChar);
                if      (ch == 'r') next = await HandleActionsAsync(ui, provider, state, folders, "reply");
                else if (ch == 'a') next = await HandleActionsAsync(ui, provider, state, folders, "reply-all");
                else if (ch == 'd') next = await HandleActionsAsync(ui, provider, state, folders, "delete");
                else if (ch == 'm') next = await HandleActionsAsync(ui, provider, state, folders, "move");
            }

            if (next != null)
            {
                var panelChanged = next.Panel != state.Panel;
                state = next;
                var nextNode = BuildClientNode(state, folders, viewH);
                await ui.ReconcileAsync(prev, nextNode);
                prev = nextNode;
                if (panelChanged)
                    await ui.FocusAsync(PanelFocusKey(next.Panel));
            }
        }

        await ui.PatchAsync(UiFrameBuilder.PopOverlay(ClientKey));
    }

    // ── Public: single-message viewer (summarize drill-in) ────────────────

    public static async Task ShowMessageAsync(IUi ui, IMailProvider provider, IMailMessage message)
    {
        int viewH      = Math.Max(6, Program.ui.Height - 6);
        int bodyHeight = Math.Max(1, viewH - 5); // 5 = from+to+date+subj+sep
        var bodyLines  = SplitBody(message.BodyPreview ?? "");
        int bodyScroll = 0;

        UiNode Build(int scroll) => BuildMsgViewNode(message, bodyLines, scroll, bodyHeight);

        var node = Build(bodyScroll);
        await ui.PatchAsync(UiFrameBuilder.PushOverlay(node));
        var prev = node;
        await ui.FocusAsync("mv-body");

        var router = ui.GetInputRouter();
        while (true)
        {
            var k = router.TryReadKey();
            if (k is null) { await Task.Delay(10); continue; }
            var key = k.Value;

            bool changed = false;

            if (key.Key == ConsoleKey.Escape)
            {
                var pick = await MenuOverlay.ShowAsync(ui, "Email actions",
                    new List<string> { "reply", "reply-all", "delete", "move", "close" }, 0);
                if (string.IsNullOrWhiteSpace(pick) || pick == "close") break;
                await ExecuteActionAsync(ui, provider, message, pick, null);
                if (pick == "delete" || pick == "move") break;
                changed = true; // after reply, stay in reading view
            }
            else if (key.Key == ConsoleKey.UpArrow)   { bodyScroll = Math.Max(0, bodyScroll - 1);  changed = true; }
            else if (key.Key == ConsoleKey.DownArrow)  { bodyScroll = Math.Min(Math.Max(0, bodyLines.Length - 1), bodyScroll + 1); changed = true; }
            else if (key.Key == ConsoleKey.PageUp)     { bodyScroll = Math.Max(0, bodyScroll - bodyHeight); changed = true; }
            else if (key.Key == ConsoleKey.PageDown)   { bodyScroll = Math.Min(Math.Max(0, bodyLines.Length - 1), bodyScroll + bodyHeight); changed = true; }
            else if (key.Key == ConsoleKey.Home)       { bodyScroll = 0; changed = true; }
            else if (key.Key == ConsoleKey.End)        { bodyScroll = Math.Max(0, bodyLines.Length - bodyHeight); changed = true; }

            if (changed)
            {
                var next = Build(bodyScroll);
                await ui.ReconcileAsync(prev, next);
                prev = next;
            }
        }

        await ui.PatchAsync(UiFrameBuilder.PopOverlay(MsgViewKey));
    }

    // ── Node builders ──────────────────────────────────────────────────────

    private static UiNode BuildClientNode(MailState state, List<FavoriteMailFolder> folders, int viewH)
    {
        return Ui.Column(ClientKey,
            BuildToolbar(),
            BuildBody(state, folders, viewH),
            Ui.Text("mail-status", state.Status).WithStyles(MailClientTheme.Status)
        ).WithProps(new { Modal = true, Role = "overlay", Width = "100%" });
    }

    private static UiNode BuildToolbar()
    {
        return Ui.Text("mail-toolbar",
            "[N]ew  [R]eply  reply-[A]ll  [D]elete  [M]ove   │   TAB:switch pane  ESC:actions/exit")
            .WithStyles(MailClientTheme.Toolbar);
    }

    private static UiNode BuildBody(MailState state, List<FavoriteMailFolder> folders, int viewH)
    {
        return Ui.Column("mail-body",
            BuildSidebar(state, folders, viewH),
            BuildMessageList(state, viewH),
            BuildReadingPane(state, viewH)
        ).WithProps(new
        {
            Layout  = "grid",
            Columns = GridColumns.Of(
                GridColumnSpec.Percent(20),
                GridColumnSpec.Percent(35),
                GridColumnSpec.Fr(1)
            ),
            Role = "body"   // marks the grid as the scrollable body in CompositeOverlayBox
        });
    }

    private static UiNode BuildSidebar(MailState state, List<FavoriteMailFolder> folders, int viewH)
    {
        var items = folders.Select(f => (object)f.DisplayName).ToList();
        return Ui.Column("mail-sidebar",
            Ui.Text("mail-sb-hdr", "── Favorites ──").WithStyles(MailClientTheme.PanelHeader),
            Ui.Node("mail-folder-list", UiKind.ListView, new
            {
                Items         = items,
                SelectedIndex = state.FolderIndex,
                Height        = viewH - 1,
                Focusable     = true
            })
        );
    }

    private static UiNode BuildMessageList(MailState state, int viewH)
    {
        var items = state.Messages.Select(FormatMessageRow).Cast<object>().ToList();
        return Ui.Column("mail-list",
            Ui.Text("mail-list-hdr", $"── Messages ({state.Messages.Count}) ──").WithStyles(MailClientTheme.PanelHeader),
            Ui.Node("mail-list-items", UiKind.ListView, new
            {
                Items         = items,
                SelectedIndex = state.MessageIndex < 0 ? 0 : state.MessageIndex,
                Height        = viewH - 1,
                Focusable     = true
            })
        );
    }

    private static UiNode BuildReadingPane(MailState state, int viewH)
    {
        var msg = state.OpenMessage;
        if (msg is null)
        {
            return Ui.Column("mail-reading",
                Ui.Text("mail-reading-empty", "(select a message to preview)")
                    .WithStyles(MailClientTheme.Muted)
            );
        }

        // Each body ListView item maps to one raw line of body text.
        // We control which portion is visible via SelectedIndex:
        //   selIdx = bodyScroll + bodyHeight - 1  →  offset (top of visible window) = bodyScroll
        int bodyHeight = Math.Max(1, viewH - 5); // 5 = from+to+date+subj+sep
        int selIdx     = state.BodyLines.Length == 0
                            ? 0
                            : Math.Min(state.BodyLines.Length - 1, state.BodyScroll + bodyHeight - 1);
        var bodyItems = state.BodyLines.Cast<object>().ToList();
        var to        = string.Join("; ",
                            msg.ToRecipients?.Select(r => r.EmailAddress) ?? Enumerable.Empty<string>());

        return Ui.Column("mail-reading",
            Ui.Text("mail-from", $"From: {msg.From?.EmailAddress ?? "(unknown)"}").WithStyles(MailClientTheme.MetaLabel),
            Ui.Text("mail-to",   $"To:   {to}").WithStyles(MailClientTheme.MetaLabel),
            Ui.Text("mail-date", $"Date: {msg.ReceivedDateTime.ToLocalTime():yyyy-MM-dd HH:mm}").WithStyles(MailClientTheme.MetaLabel),
            Ui.Text("mail-subj", $"Subj: {msg.Subject ?? "(no subject)"}").WithStyles(MailClientTheme.MetaLabel),
            Ui.Text("mail-sep",  new string('─', 30)).WithStyles(MailClientTheme.Muted),
            Ui.Node("mail-reading-body", UiKind.ListView, new
            {
                Items         = bodyItems,
                SelectedIndex = selIdx,
                Height        = bodyHeight,
                Focusable     = true
            })
        );
    }

    private static UiNode BuildMsgViewNode(IMailMessage msg, string[] bodyLines, int bodyScroll, int bodyHeight)
    {
        int selIdx    = bodyLines.Length == 0 ? 0 : Math.Min(bodyLines.Length - 1, bodyScroll + bodyHeight - 1);
        var bodyItems = bodyLines.Cast<object>().ToList();
        var to        = string.Join("; ",
                            msg.ToRecipients?.Select(r => r.EmailAddress) ?? Enumerable.Empty<string>());

        return Ui.Column(MsgViewKey,
            Ui.Text("mv-from", $"From: {msg.From?.EmailAddress ?? "(unknown)"}").WithStyles(MailClientTheme.MetaLabel),
            Ui.Text("mv-to",   $"To:   {to}").WithStyles(MailClientTheme.MetaLabel),
            Ui.Text("mv-date", $"Date: {msg.ReceivedDateTime.ToLocalTime():yyyy-MM-dd HH:mm}").WithStyles(MailClientTheme.MetaLabel),
            Ui.Text("mv-subj", $"Subj: {msg.Subject ?? "(no subject)"}").WithStyles(MailClientTheme.MetaLabel),
            Ui.Text("mv-sep",  new string('─', 30)).WithStyles(MailClientTheme.Muted),
            Ui.Node("mv-body", UiKind.ListView, new
            {
                Items         = bodyItems,
                SelectedIndex = selIdx,
                Height        = bodyHeight,
                Focusable     = true
            }),
            Ui.Text("mv-hint", "ESC:actions  ↑/↓:scroll  PgUp/PgDn  Home/End").WithStyles(MailClientTheme.Muted)
        ).WithProps(new { Modal = true, Role = "overlay", Width = "80%" });
    }

    // ── Action handling ────────────────────────────────────────────────────

    private static async Task<MailState?> HandleActionsAsync(
        IUi ui, IMailProvider provider, MailState state,
        List<FavoriteMailFolder> folders, string? preselected = null)
    {
        string? pick = preselected
            ?? await MenuOverlay.ShowAsync(ui, "Email actions",
                   new List<string> { "reply", "reply-all", "delete", "move", "close" }, 0);

        if (string.IsNullOrWhiteSpace(pick)) return state; // cancelled

        if (pick == "close")
        {
            return state with
            {
                OpenMessage = null, BodyLines = Array.Empty<string>(),
                BodyScroll = 0, Panel = ActivePanel.MessageList,
                Status = folders.Count > 0 ? $"📂 {folders[state.FolderIndex].DisplayName}" : ""
            };
        }

        await ExecuteActionAsync(ui, provider, state.OpenMessage!, pick, folders);

        if (pick == "delete" || pick == "move")
        {
            // Refresh the folder after structural change and return to message list
            return await LoadFolderAsync(
                state with { OpenMessage = null, BodyLines = Array.Empty<string>(), BodyScroll = 0, Panel = ActivePanel.MessageList },
                provider, folders, state.FolderIndex);
        }

        return state; // reply actions stay in reading pane
    }

    private static async Task ExecuteActionAsync(
        IUi ui, IMailProvider provider, IMailMessage msg,
        string action, List<FavoriteMailFolder>? folders)
    {
        if (action.StartsWith("reply", StringComparison.OrdinalIgnoreCase))
        {
            bool all   = action.Equals("reply-all", StringComparison.OrdinalIgnoreCase);
            var  form  = UiForm.Create("Draft reply", new ReplyModel());
            form.AddText<ReplyModel>("Additional instructions (leave blank to auto-draft)",
                m => m.Prompt ?? "", (m, v) => m.Prompt = v).MakeOptional();
            if (!await ui.ShowFormAsync(form)) return;

            var prompt = ((ReplyModel)form.Model!).Prompt ?? "";
            var ctx    = new Context("Draft a professional, concise email reply. Keep it short, specific, and kind. Output plain text only.");
            ctx.AddUserMessage($"Original message subject: {msg.Subject}\n\nUser instructions:\n{prompt}");

            var body  = await Engine.Provider!.PostChatAsync(ctx, 0.2f);
            var draft = all ? await provider.DraftReplyAllAsync(msg, body)
                            : await provider.DraftReplyAsync(msg, body);
            // Draft saved — status or next render will reflect it
        }
        else if (action == "delete")
        {
            try { await provider.DeleteAsync(msg); }
            catch { /* silently fail; next folder reload will reflect reality */ }
        }
        else if (action == "move" && folders != null)
        {
            var destChoices = folders.Select(f => f.DisplayName).ToList();
            var dest        = await MenuOverlay.ShowAsync(ui, "Move to which folder?", destChoices, 0);
            if (!string.IsNullOrWhiteSpace(dest))
            {
                var target = await provider.GetFolderByIdOrNameAsync(dest);
                if (target != null)
                    try { await provider.MoveAsync(msg, target); } catch { }
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string PanelFocusKey(ActivePanel panel) => panel switch
    {
        ActivePanel.Sidebar     => "mail-folder-list",
        ActivePanel.MessageList => "mail-list-items",
        ActivePanel.ReadingPane => "mail-reading-body",
        _                       => "mail-list-items"
    };

    private static async Task<MailState> LoadFolderAsync(
        MailState state, IMailProvider provider, List<FavoriteMailFolder> folders, int folderIdx)
    {
        try
        {
            var fav    = folders[folderIdx];
            var folder = await provider.GetFolderByIdOrNameAsync(fav.IdOrName);
            if (folder != null)
            {
                var msgs = await provider.ListMessagesSinceAsync(
                    folder,
                    TimeSpan.FromDays(Program.config.MailSettings.LookbackWindow),
                    Program.config.MailSettings.LookbackCount);

                msgs = msgs.OrderByDescending(m => m.ReceivedDateTime)
                           .Take(Program.config.MailSettings.LookbackCount)
                           .ToList();

                return state with
                {
                    FolderIndex  = folderIdx,
                    Messages     = msgs,
                    MessageIndex = msgs.Count > 0 ? 0 : -1,
                    OpenMessage  = null,
                    BodyLines    = Array.Empty<string>(),
                    BodyScroll   = 0,
                    Status       = $"📂 {fav.DisplayName}: {msgs.Count} messages"
                };
            }
        }
        catch (Exception ex)
        {
            return state with
            {
                FolderIndex  = folderIdx,
                Messages     = new(),
                MessageIndex = -1,
                Status       = $"Error: {ex.Message}"
            };
        }
        return state with { FolderIndex = folderIdx };
    }

    private static string FormatMessageRow(IMailMessage m)
    {
        var from = Utilities.TruncatePlain(m.From?.EmailAddress ?? "(unknown)", 22);
        var date = m.ReceivedDateTime.ToLocalTime().ToString("MM/dd HH:mm");
        var subj = m.Subject ?? "(no subject)";
        return $"  {from,-22} {date}  {subj}";
    }

    private static string[] SplitBody(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

    // Reply form model (private to this overlay)
    private sealed class ReplyModel { public string? Prompt { get; set; } }
}
