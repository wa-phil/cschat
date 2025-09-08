using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Graph;
using Microsoft.Graph.Models;

#nullable enable

[IsConfigurable("Mail")]
public class MailClient : ISubsystem
{
    private bool _connected;

    public bool IsAvailable => true;
    public bool IsEnabled
    {
        get => _connected;
        set
        {
            if (value && !_connected)
            {
                _connected = true;
                Register();
            }
            else if (!value && _connected)
            {
                Unregister();
                _connected = false;
            }
        }
    }

    private GraphServiceClient G(params string[] scopes)
        => new GraphCore().GetClient(scopes.Length == 0 ? new[] { "Mail.Read", "Mail.Send", "Mail.ReadWrite" } : scopes); // azcli → uses .default internally :contentReference[oaicite:8]{index=8}

    public void Register()
    {
        Program.commandManager.SubCommands.Add(MailCommands.Commands(this));               // CLI wrapper like KustoCommands
        ToolRegistry.RegisterTool("tool.mail.list_folders", new ListMailFoldersTool(this));
        ToolRegistry.RegisterTool("tool.mail.list_since", new ListMailSinceTool(this));
        ToolRegistry.RegisterTool("tool.mail.read", new ReadMailTool(this));
        ToolRegistry.RegisterTool("tool.mail.summarize", new SummarizeMailTool(this));
        ToolRegistry.RegisterTool("tool.mail.send", new SendMailTool(this));
        ToolRegistry.RegisterTool("tool.mail.reply", new ReplyMailTool(this));
        ToolRegistry.RegisterTool("tool.mail.move", new MoveMailTool(this));
        ToolRegistry.RegisterTool("tool.mail.triage_folder", new TriageFolderTool(this));
    }

    public void Unregister()
    {
        Program.commandManager.SubCommands.RemoveAll(c => c.Name.Equals("Mail", StringComparison.OrdinalIgnoreCase));
        new[] { "tool.mail.list_folders","tool.mail.list_since","tool.mail.read","tool.mail.summarize",
                "tool.mail.send","tool.mail.reply","tool.mail.move","tool.mail.triage_folder" }
            .ToList().ForEach(ToolRegistry.UnregisterTool);
    }

    // ===== Graph helpers (used by tools) =====

    public async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(string? parentId = null, int top = 50, CancellationToken ct = default)
    {
        var client = G("Mail.Read");
        if (string.IsNullOrWhiteSpace(parentId))
        {
            var page = await client.Me.MailFolders.GetAsync(req =>
            {
                req.QueryParameters.Top = top;
                req.QueryParameters.Select = new[] { "id","displayName","totalItemCount","childFolderCount" };
                req.QueryParameters.Orderby = new[] { "displayName" };
            }, ct);
            return page?.Value ?? new List<MailFolder>();
        }
        else
        {
            var page = await client.Me.MailFolders[parentId].ChildFolders.GetAsync(req =>
            {
                req.QueryParameters.Top = top;
                req.QueryParameters.Select = new[] { "id","displayName","totalItemCount","childFolderCount" };
                req.QueryParameters.Orderby = new[] { "displayName" };
            }, ct);
            return page?.Value ?? new List<MailFolder>();
        }
    }

    public async Task<IReadOnlyList<Message>> ListMessagesSinceAsync(string folderIdOrName, TimeSpan window, int top = 50, CancellationToken ct = default) => await Log.MethodAsync(async ctx =>
    {
        ctx.Append(Log.Data.Input, folderIdOrName);
        var client = G("Mail.Read");
        var since = DateTimeOffset.UtcNow.Subtract(window).ToString("o");
        var req = client.Me.MailFolders[folderIdOrName].Messages;

        ctx.Append(Log.Data.Message, "requesting messages");
        var page = await req.GetAsync(r =>
        {
            r.QueryParameters.Top = top;
            r.QueryParameters.Select = new[] { "id", "receivedDateTime", "from", "subject", "isRead", "hasAttachments", "bodyPreview" };
            r.QueryParameters.Orderby = new[] { "receivedDateTime DESC" };
            r.QueryParameters.Filter = $"receivedDateTime ge {since}";
        }, ct);

        var result = page?.Value ?? new List<Message>();
        ctx.Append(Log.Data.Message, $"Found {result.Count} messages in '{folderIdOrName}' since {since}");
        ctx.Succeeded();
        return result;
    });

    public Task<Message?> GetMessageAsync(string id, CancellationToken ct = default)
        => G("Mail.Read").Me.Messages[id].GetAsync(r =>
        {
            r.QueryParameters.Select = new[] { "id","subject","from","sender","toRecipients","ccRecipients","replyTo","receivedDateTime","bodyPreview","body","internetMessageId" };
        }, ct);

    public async Task SendAsync(string to, string subject, string body, bool html = false, CancellationToken ct = default)
    {
        var client = G("Mail.Send");
        var contentType = html ? BodyType.Html : BodyType.Text;
        var payload = new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject,
                Body = new ItemBody { ContentType = contentType, Content = body },
                ToRecipients = new List<Recipient> { new Recipient { EmailAddress = new EmailAddress { Address = to } } }
            },
            SaveToSentItems = true
        };
        await client.Me.SendMail.PostAsync(payload, cancellationToken: ct);
    }

    public async Task ReplyAsync(string messageId, string body, bool replyAll = false, bool html = false, CancellationToken ct = default)
    {
        var client = G("Mail.Send","Mail.Read");
        var contentType = html ? BodyType.Html : BodyType.Text;
        var b = new ItemBody { ContentType = contentType, Content = body };
        if (replyAll)
            await client.Me.Messages[messageId].ReplyAll.PostAsync(new Microsoft.Graph.Me.Messages.Item.ReplyAll.ReplyAllPostRequestBody { Comment = null, Message = new Message { Body = b } }, cancellationToken: ct);
        else
            await client.Me.Messages[messageId].Reply.PostAsync(new Microsoft.Graph.Me.Messages.Item.Reply.ReplyPostRequestBody { Comment = null, Message = new Message { Body = b } }, cancellationToken: ct);
    }

    public async Task<string?> ResolveFolderIdAsync(string displayOrWellKnown, CancellationToken ct = default)
    {
        // Well-known names like "Inbox", "SentItems", "DeletedItems" work directly.
        // For display names, search top-level folders.
        var wk = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Inbox","Drafts","SentItems","DeletedItems","Archive","JunkEmail" };
        if (wk.Contains(displayOrWellKnown)) return displayOrWellKnown;

        var top = await ListFoldersAsync(null, 200, ct);
        var match = top.FirstOrDefault(f => string.Equals(f.DisplayName, displayOrWellKnown, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    public async Task<string?> MoveAsync(string messageId, string destinationFolderIdOrName, CancellationToken ct = default)
    {
        var client = G("Mail.ReadWrite");
        var destId = await ResolveFolderIdAsync(destinationFolderIdOrName, ct);
        if (string.IsNullOrWhiteSpace(destId)) throw new InvalidOperationException($"Folder '{destinationFolderIdOrName}' not found.");
        var moved = await client.Me.Messages[messageId].Move.PostAsync(new Microsoft.Graph.Me.Messages.Item.Move.MovePostRequestBody { DestinationId = destId }, cancellationToken: ct);
        return moved?.Id;
    }
}
