using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

#nullable enable

[IsConfigurable("Mail")]
public sealed class MapiMailClient : ISubsystem, IMailProvider
{
    private bool _connected;

    public bool IsAvailable => true;  // present on Windows VM
    public bool IsEnabled
    {
        get => _connected;
        set
        {
            if (value && !_connected) { _connected = true; Connect(); Register(); }
            else if (!value && _connected) { Unregister(); Disconnect(); _connected = false; }
        }
    }

    // ===== Lifecycle =====

    private void Connect() => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Succeeded();
    });

    private void Disconnect() {}

    public void Register()
    {
        Program.commandManager.SubCommands.Add(MailCommands.Commands(this));
    }

    public void Unregister()
    {
        Program.commandManager.SubCommands.RemoveAll(c => c.Name.Equals("Mail", StringComparison.OrdinalIgnoreCase));
    }


    // ===== IMailProvider implementation =====
    // NOTE: These call into private MAPI helpers.

    public Task<IMailMessage?> GetMessageAsync(string id, CancellationToken ct = default)
        => Task.Run<IMailMessage?>(() => _GetMessage_MAPI(id, ct), ct);

    public Task<IMailFolder?> GetFolderByIdOrNameAsync(string idOrName, CancellationToken ct = default)
        => Task.Run<IMailFolder?>(() => _GetFolder_MAPI(idOrName, ct), ct);

    public Task<List<IMailFolder>> ListFoldersAsync(string? folderIdOrName = null, int top = 50, CancellationToken ct = default)
        => Task.Run<List<IMailFolder>>(() => _ListFolders_MAPI(folderIdOrName, top, ct), ct);

    public Task<List<IMailMessage>> ListMessagesSinceAsync(IMailFolder? folder, TimeSpan window, int top = 50, CancellationToken ct = default)
        => Task.Run<List<IMailMessage>>(() => _ListMessagesSince_MAPI(folder, window, top, ct), ct);

    public Task<IMailMessage> DraftMessage(string folderIdOrName, string subject, string body, List<MailRecipient> to, List<MailRecipient> cc, List<MailRecipient> bcc)
        => Task.Run<IMailMessage>(() => _DraftMessage_MAPI(folderIdOrName, subject, body, to, cc, bcc), CancellationToken.None);

    public Task<IMailMessage> DraftReplyAsync(IMailMessage message, string body, CancellationToken ct = default)
        => Task.Run<IMailMessage>(() => _DraftReply_MAPI(message, body, replyAll: false, ct), ct);

    public Task<IMailMessage> DraftReplyAllAsync(IMailMessage message, string body, CancellationToken ct = default)
        => Task.Run<IMailMessage>(() => _DraftReply_MAPI(message, body, replyAll: true, ct), ct);

    public Task<IMailMessage> DraftForwardAsync(IMailMessage message, List<MailRecipient> to, List<MailRecipient>? cc = null, List<MailRecipient>? bcc = null, string? body = null, CancellationToken ct = default)
        => Task.Run<IMailMessage>(() => _DraftForward_MAPI(message, to, cc, bcc, body, ct), ct);

    public Task SendAsync(IMailMessage message, CancellationToken ct = default)
        => Task.Run(() => _Send_MAPI(message, ct), ct);

    public Task MoveAsync(IMailMessage message, IMailFolder folder, CancellationToken ct = default)
        => Task.Run(() => _Move_MAPI(message, folder, ct), ct);

    public Task DeleteAsync(IMailMessage message, CancellationToken ct = default)
        => Task.Run(() => _Delete_MAPI(message, ct), ct);

    // ===== MAPI interop (late-bound Outlook COM) =====
    // Lightweight implementation that assumes Outlook is installed and a profile is available.
    // Uses late-bound COM (Activator.CreateInstance) so no interop assembly is required.

    private static IMailMessage? _GetMessage_MAPI(string id, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication();
            if (app == null) return null;
            var ns = app.GetNamespace("MAPI");
            try
            {
                // Try GetItemFromID which accepts an EntryID
                dynamic? item = null;
                try { item = ns.GetItemFromID(id); } catch { }
                if (item == null)
                {
                    // fallback: search inbox and other folders by ConversationID/EntryID-like string
                    item = FindMailItemById(ns, id);
                }
                if (item == null) return null;
                return (IMailMessage?)WrapMailItem(item);
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }
    }

    private static IMailFolder? _GetFolder_MAPI(string idOrName, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication();
            if (app == null) return null;
            var ns = app.GetNamespace("MAPI");
            try
            {
                var folder = FindFolderByIdOrName(ns, idOrName);
                if (folder == null) return null;
                return (IMailFolder?)WrapFolder(folder);
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }
    }

    private static List<IMailFolder> _ListFolders_MAPI(string? root, int top, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        var outList = new List<IMailFolder>();
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication();
            if (app == null) return outList;
            var ns = app.GetNamespace("MAPI");
            try
            {
                // Always resolve to a *Folders collection* before enumerating
                dynamic? foldersToEnumerate = null;

                if (string.IsNullOrWhiteSpace(root))
                {
                    foldersToEnumerate = ns.Folders; // collection of stores' root folders
                }
                else
                {
                    dynamic? rootFolder = FindFolderByIdOrName(ns, root);
                    // If we found a folder, enumerate its .Folders collection; otherwise fall back to top level
                    foldersToEnumerate = rootFolder != null ? rootFolder.Folders : ns.Folders;
                }

                if (foldersToEnumerate == null) return outList;

                // COM collections can be picky: try foreach, then fall back to 1-based indexing
                int added = 0;
                try
                {
                    foreach (dynamic f in foldersToEnumerate)
                    {
                        outList.Add((IMailFolder)WrapFolder(f));
                        added++;
                        if (added >= top) break;
                    }
                }
                catch
                {
                    try
                    {
                        int count = (int)(foldersToEnumerate.Count ?? 0);
                        for (int i = 1; i <= count && added < top; i++)
                        {
                            dynamic f = foldersToEnumerate[i];
                            outList.Add((IMailFolder)WrapFolder(f));
                            added++;
                        }
                    }
                    catch { /* swallow – return what we have */ }
                }
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }

        return outList;
    }


    private static List<IMailMessage> _ListMessagesSince_MAPI(IMailFolder? folder, TimeSpan window, int top, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        var outList = new List<IMailMessage>();
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication();
            if (app == null) return outList;
            var ns = app.GetNamespace("MAPI");
            try
            {
                dynamic? mapifolder = null;
                var mf = folder as MapiMailFolder;
                if (mf != null)
                {
                    try { var fobj = mf._folder; if (fobj != null) mapifolder = fobj; }
                    catch { }
                }
                if (mapifolder == null) mapifolder = ns.GetDefaultFolder(6); // olFolderInbox

                DateTime since = DateTime.UtcNow - window;
                string filter = "[ReceivedTime] >= '" + since.ToString("g") + "'";
                dynamic? items = null;
                try { items = mapifolder.Items; } catch { items = null; }
                dynamic? restricted = null;
                if (items != null)
                {
                    try { restricted = items.Restrict(filter); } catch { restricted = items; }
                    // Sort descending by ReceivedTime
                    try { if (restricted != null) restricted.Sort("[ReceivedTime]", true); } catch { }
                }
                int i = 0;
                if (restricted != null)
                {
                    foreach (dynamic it in restricted)
                    {
                        try
                        {
                            if (it.MessageClass != null && it.MessageClass.ToString().StartsWith("IPM.Note"))
                            {
                                outList.Add((IMailMessage)WrapMailItem(it));
                                i++;
                                if (i >= top) break;
                            }
                        }
                        catch { }
                    }
                }
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }

        return outList;
    }

    private static IMailMessage _DraftMessage_MAPI(string folder, string subject, string body, List<MailRecipient> to, List<MailRecipient> cc, List<MailRecipient> bcc)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication() ?? throw new InvalidOperationException("Outlook not available");
            var ns = app.GetNamespace("MAPI");
            try
            {
                dynamic mail = app.CreateItem(0); // olMailItem
                mail.Subject = subject ?? string.Empty;
                mail.Body = body ?? string.Empty;
                if (to != null && to.Count > 0) mail.To = string.Join(";", to.Select(r => r.EmailAddress));
                if (cc != null && cc.Count > 0) mail.CC = string.Join(";", cc.Select(r => r.EmailAddress));
                if (bcc != null && bcc.Count > 0) mail.BCC = string.Join(";", bcc.Select(r => r.EmailAddress));
                // Save as draft
                mail.Save();
                return (IMailMessage)WrapMailItem(mail);
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }
    }

    private static IMailMessage _DraftReply_MAPI(IMailMessage msg, string body, bool replyAll, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication() ?? throw new InvalidOperationException("Outlook not available");
            var ns = app.GetNamespace("MAPI");
            try
            {
                dynamic mailItem = ResolveToMailItem(ns, msg);
                if (mailItem == null) throw new InvalidOperationException("Original message not found");
                dynamic draft = replyAll ? mailItem.ReplyAll() : mailItem.Reply();
                draft.Body = (body ?? string.Empty) + "\r\n\r\n" + (draft.Body ?? string.Empty);
                draft.Save();
                return (IMailMessage)WrapMailItem(draft);
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }
    }

    private static IMailMessage _DraftForward_MAPI(IMailMessage msg, List<MailRecipient> to, List<MailRecipient>? cc, List<MailRecipient>? bcc, string? body, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication() ?? throw new InvalidOperationException("Outlook not available");
            var ns = app.GetNamespace("MAPI");
            try
            {
                dynamic mailItem = ResolveToMailItem(ns, msg);
                if (mailItem == null) throw new InvalidOperationException("Original message not found");
                dynamic draft = mailItem.Forward();
                if (to != null && to.Count > 0) draft.To = string.Join(";", to.Select(r => r.EmailAddress));
                if (cc != null && cc.Count > 0) draft.CC = string.Join(";", cc.Select(r => r.EmailAddress));
                if (bcc != null && bcc.Count > 0) draft.BCC = string.Join(";", bcc.Select(r => r.EmailAddress));
                if (!string.IsNullOrEmpty(body)) draft.Body = (body ?? string.Empty) + "\r\n\r\n" + (draft.Body ?? string.Empty);
                draft.Save();
                return (IMailMessage)WrapMailItem(draft);
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }
    }

    private static void _Send_MAPI(IMailMessage msg, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication() ?? throw new InvalidOperationException("Outlook not available");
            var ns = app.GetNamespace("MAPI");
            try
            {
                dynamic mailItem = ResolveToMailItem(ns, msg);
                if (mailItem == null) throw new InvalidOperationException("Message not found");
                mailItem.Send();
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }
    }

    private static void _Move_MAPI(IMailMessage msg, IMailFolder folder, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication() ?? throw new InvalidOperationException("Outlook not available");
            var ns = app.GetNamespace("MAPI");
            try
            {
                dynamic mailItem = ResolveToMailItem(ns, msg);
                if (mailItem == null) throw new InvalidOperationException("Message not found");
                dynamic? dest = null;
                var mf = folder as MapiMailFolder;
                if (mf != null)
                {
                    try { var d = mf._folder; if (d != null) dest = d; }
                    catch { }
                }
                if (dest == null) dest = FindFolderByIdOrName(ns, folder?.DisplayName ?? string.Empty);
                if (dest == null) throw new InvalidOperationException("Destination folder not found");
                mailItem.Move(dest);
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }
    }

    private static void _Delete_MAPI(IMailMessage msg, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("MAPI is only supported on Windows.");
        dynamic? app = null;
        try
        {
            app = GetOutlookApplication() ?? throw new InvalidOperationException("Outlook not available");
            var ns = app.GetNamespace("MAPI");
            try
            {
                dynamic mailItem = ResolveToMailItem(ns, msg);
                if (mailItem == null) throw new InvalidOperationException("Message not found");
                mailItem.Delete();
            }
            finally { ReleaseCom(ns); }
        }
        finally { ReleaseCom(app); }
    }

    // ----- Helpers and wrappers -----
    [SupportedOSPlatform("windows")]
    private static dynamic? GetOutlookApplication()
    {
        try
        {
            var prog = Type.GetTypeFromProgID("Outlook.Application");
            if (prog == null) return null;

            return Activator.CreateInstance(prog);
        }
        catch { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseCom(object? o)
    {
        if (o == null) return;
        try { Marshal.ReleaseComObject(o); } catch { }
    }

    private static dynamic? FindMailItemById(dynamic ns, string id)
    {
        try
        {
            // Search common folders for a matching EntryID or InternetMessageId
            var stores = ns.Stores;
            foreach (dynamic store in stores)
            {
                dynamic root = store.GetRootFolder();
                var found = FindMailItemInFolderRecursive(root, id);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }

    private static dynamic? FindMailItemInFolderRecursive(dynamic folder, string id)
    {
        try
        {
            dynamic items = folder.Items;
            foreach (dynamic it in items)
            {
                if (it.EntryID != null && string.Equals((string)it.EntryID, id, StringComparison.OrdinalIgnoreCase)) return it;
                if (it.InternetMessageID != null && string.Equals((string)it.InternetMessageID, id, StringComparison.OrdinalIgnoreCase)) return it;
            }
            dynamic sub = folder.Folders;
            foreach (dynamic f in sub)
            {
                var r = FindMailItemInFolderRecursive(f, id);
                if (r != null) return r;
            }
        }
        catch { }
        return null;
    }

    private static dynamic? FindFolderByIdOrName(dynamic ns, string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        try
        {
            // Try GetFolderFromID not generally available; instead search stores
            var stores = ns.Stores;
            foreach (dynamic store in stores)
            {
                dynamic root = store.GetRootFolder();
                var found = FindFolderRecursive(root, idOrName);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }

    private static dynamic? FindFolderRecursive(dynamic folder, string idOrName)
    {
        try
        {
            if (folder.Name != null && string.Equals((string)folder.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return folder;

            dynamic sub = folder.Folders;
            foreach (dynamic f in sub)
            {
                var r = FindFolderRecursive(f, idOrName);
                if (r != null) return r;
            }
        }
        catch { }
        return null;
    }

    private static dynamic? ResolveToMailItem(dynamic ns, IMailMessage msg)
    {
        if (msg == null) return null;
        try
        {
            var mm = msg as MapiMailMessage;
            if (mm != null && mm._item != null) return mm!._item!;

            // Try to get by id
            if (!string.IsNullOrWhiteSpace(msg.Id))
            {
                try { return ns.GetItemFromID(msg.Id); } catch { }
                var found = FindMailItemById(ns, msg.Id);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }

    private static IMailMessage WrapMailItem(dynamic item) => new MapiMailMessage(item);

    private static IMailFolder WrapFolder(dynamic folder) => new MapiMailFolder(folder);

    private sealed class MapiMailMessage : IMailMessage
    {
        public dynamic _item;
        public MapiMailMessage(dynamic item) { _item = item; }
        public string Id
        {
            get => (string)(_item?.EntryID ?? _item?.InternetMessageID ?? string.Empty);
        }
        public string ParentFolderId
        {
            get => (string)(_item?.Parent?.EntryID ?? string.Empty);
        }

        public string Subject
        {
            get => (string)(_item?.Subject ?? string.Empty);
        }

        public string BodyPreview
        {
            get => (string)(_item?.Body ?? string.Empty);
        }

        public DateTimeOffset ReceivedDateTime
        {
            get => null != _item.ReceivedTime ? DateTime.SpecifyKind((DateTime)(_item.ReceivedTime), DateTimeKind.Utc) : DateTimeOffset.MinValue;
        }

        public DateTimeOffset? SentDateTime
        {
            get => null != _item.SentOn ? DateTime.SpecifyKind((DateTime)(_item.SentOn), DateTimeKind.Utc) : null;
        }

        public bool IsRead
        {
            get => !((bool)(_item.UnRead == true));
        }

        public bool IsDraft
        {
            get => null == _item.SentOn || (DateTime)_item.SentOn == default;
        }

        public MailRecipient From
        {
            get
            {
                // Prefer a human-friendly display name when the sender is an EX/legacy DN.
                string email = (string)(_item?.SenderEmailAddress ?? _item?.Sender?.Address ?? string.Empty);
                string name  = (string)(_item?.SenderName        ?? _item?.Sender?.Name    ?? string.Empty);
                string type  = (string)(_item?.Sender?.Type      ?? string.Empty);

                bool looksLegacy =
                    (!string.IsNullOrWhiteSpace(type)  && type.Equals("EX", StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(email) && (email.StartsWith("/O=", StringComparison.OrdinalIgnoreCase)
                                                        || email.IndexOf("EXCHANGE ADMINISTRATIVE GROUP", StringComparison.OrdinalIgnoreCase) >= 0));

                var display = looksLegacy && !string.IsNullOrWhiteSpace(name) ? name : (string.IsNullOrWhiteSpace(email) ? name : email);
                return new MailRecipient(display);
            }
        }

        public List<MailRecipient> ToRecipients
        {
            get
            {
                var s = (string)(_item.To ?? string.Empty);
                return s
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => new MailRecipient(x.Trim())).ToList();
            }
        }

        public List<MailRecipient> CcRecipients
        {
            get
            {
                var s = (string)(_item.CC ?? string.Empty);
                return s
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => new MailRecipient(x.Trim())).ToList();
            }
        }

        public List<MailRecipient> BccRecipients
        {
            get
            {
                var s = (string)(_item.BCC ?? string.Empty);
                return s
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => new MailRecipient(x.Trim())).ToList();
            }
        }
    }

    private sealed class MapiMailFolder : IMailFolder
    {
        public dynamic _folder;
        public MapiMailFolder(dynamic folder) { _folder = folder; }
        public string DisplayName
        {
            get => (string)(_folder.Name ?? string.Empty);
        }

        public List<IMailFolder> ChildFolders
        {
            get
            {
                var list = new List<IMailFolder>();
                try
                {
                    dynamic subs = _folder.Folders;
                    foreach (dynamic f in subs) list.Add(new MapiMailFolder(f));
                }
                catch
                {
                }
                return list;
            }
        }

        public List<IMailMessage> Messages
        {
            get
            {
                var list = new List<IMailMessage>();
                try
                {
                    dynamic items = _folder.Items;
                    foreach (dynamic it in items)
                    {
                        try
                        {
                            if (it.MessageClass != null && it.MessageClass.ToString().StartsWith("IPM.Note"))
                            {
                                list.Add(new MapiMailMessage(it));
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                return list;
            }
        }

        public string ParentFolderId
        {
            get => (string)(_folder.Parent?.EntryID ?? string.Empty);            
        }

        public int TotalItemCount => (int)(_folder.Items?.Count ?? 0);
        public int UnreadItemCount => (int)(_folder.UnReadItemCount ?? 0);
    }
}
