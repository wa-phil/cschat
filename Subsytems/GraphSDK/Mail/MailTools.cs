using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Graph;

public sealed class ListMailFoldersInput
{
    [UserField(display: "Parent Folder Id or blank for top")] public string? ParentId { get; set; }
    [UserField(display: "Top")] public int? Top { get; set; } = 50;
}

[IsConfigurable("tool.mail.list_folders")]
public sealed class ListMailFoldersTool : ITool
{
    private readonly MailClient _mail;
    public ListMailFoldersTool(MailClient mail) => _mail = mail;
    public string Description => "List mail folders (top-level or children of a folder).";
    public string Usage => "ListMailFolders({ \"ParentId\": null, \"Top\": 50 })";
    public Type InputType => typeof(ListMailFoldersInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"ParentId\":{\"type\":\"string\"},\"Top\":{\"type\":\"integer\"}}}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (ListMailFoldersInput)input;
        var list = await _mail.ListFoldersAsync(p.ParentId, p.Top ?? 50);
        var text = string.Join("\n", list.Select(f => $"{f.DisplayName} (id:{f.Id}) items:{f.TotalItemCount} children:{f.ChildFolderCount}"));
        ctx.AddToolMessage(text);
        return ToolResult.Success(text, ctx);
    }
}

public sealed class ListMailSinceInput
{
    [UserField(required:true, display:"Folder (id or well-known name)")] public string Folder { get; set; } = "Inbox";
    [UserField(required:true, display:"Lookback (minutes)")] public int Minutes { get; set; } = 60;
    [UserField(display:"Top")] public int Top { get; set; } = 50;
}

[IsConfigurable("tool.mail.list_since")]
public sealed class ListMailSinceTool : ITool
{
    private readonly MailClient _mail;
    public ListMailSinceTool(MailClient mail) => _mail = mail;
    public string Description => "List messages received in a folder within the last N minutes.";
    public string Usage => "ListMailSince({ \"Folder\":\"Inbox\", \"Minutes\":60, \"Top\":50 })";
    public Type InputType => typeof(ListMailSinceInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"Folder\":{\"type\":\"string\"},\"Minutes\":{\"type\":\"integer\"},\"Top\":{\"type\":\"integer\"}},\"required\":[\"Folder\",\"Minutes\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (ListMailSinceInput)input;
        var msgs = await _mail.ListMessagesSinceAsync(p.Folder, TimeSpan.FromMinutes(p.Minutes), p.Top);
        var lines = msgs.Select(m => $"{m.ReceivedDateTime:yyyy-MM-dd HH:mm} {(m.IsRead==true?" ":"*")} {m.Subject} — {m.From?.EmailAddress?.Address} (id:{m.Id})");
        var text = string.Join("\n", lines);
        ctx.AddToolMessage(text);
        return ToolResult.Success(text, ctx);
    }
}

public sealed class ReadMailInput { [UserField(required:true, display:"Message Id")] public string MessageId { get; set; } = ""; }

[IsConfigurable("tool.mail.read")]
public sealed class ReadMailTool : ITool
{
    private readonly MailClient _mail;
    public ReadMailTool(MailClient mail) => _mail = mail;
    public string Description => "Read a message (headers + preview + body).";
    public string Usage => "ReadMail({ \"MessageId\":\"<id>\" })";
    public Type InputType => typeof(ReadMailInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"MessageId\":{\"type\":\"string\"}},\"required\":[\"MessageId\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var id = ((ReadMailInput)input).MessageId;
        var m = await _mail.GetMessageAsync(id);
        if (m is null) return ToolResult.Failure("Message not found.", ctx);
        var to = string.Join(", ", m.ToRecipients?.Select(r => r.EmailAddress?.Address) ?? Array.Empty<string>());
        var text =
$@"Subject : {m.Subject}
From    : {m.From?.EmailAddress?.Address}
To      : {to}
When    : {m.ReceivedDateTime}
Preview :
{m.BodyPreview}

---- Body ({m.Body?.ContentType}) ----
{m.Body?.Content}";
        ctx.AddToolMessage(text);
        return ToolResult.Success(text, ctx);
    }
}

public sealed class SummarizeMailInput { [UserField(required:true, display:"Message Id")] public string MessageId { get; set; } = ""; }

[IsConfigurable("tool.mail.summarize")]
public sealed class SummarizeMailTool : ITool
{
    private readonly MailClient _mail;
    public SummarizeMailTool(MailClient mail) => _mail = mail;
    public string Description => "Lightweight summary of an email (subject, sender, key bullets from preview/body).";
    public string Usage => "SummarizeMail({ \"MessageId\":\"<id>\" })";
    public Type InputType => typeof(SummarizeMailInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"MessageId\":{\"type\":\"string\"}},\"required\":[\"MessageId\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var id = ((SummarizeMailInput)input).MessageId;
        var m = await _mail.GetMessageAsync(id);
        if (m is null) return ToolResult.Failure("Message not found.", ctx);
        // Simple extractive summary (keeps you provider-agnostic). You can swap to your chat provider later. :contentReference[oaicite:12]{index=12}
        var body = (m.Body?.Content ?? m.BodyPreview ?? "").Replace("\r","").Trim();
        var first = string.Join("\n", body.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).Take(6));
        var text =
$@"Summary:
- Subject: {m.Subject}
- From: {m.From?.EmailAddress?.Address}
- Received: {m.ReceivedDateTime}
- Snippet:
{first}";
        ctx.AddToolMessage(text);
        return ToolResult.Success(text, ctx);
    }
}

public sealed class SendMailInput
{
    [UserField(required:true, display:"To")] public string To { get; set; } = "";
    [UserField(required:true, display:"Subject")] public string Subject { get; set; } = "";
    [UserField(required:true, display:"Body")] public string Body { get; set; } = "";
    [UserField(display:"HTML")] public bool Html { get; set; } = false;
}

[IsConfigurable("tool.mail.send")]
public sealed class SendMailTool : ITool
{
    private readonly MailClient _mail;
    public SendMailTool(MailClient mail) => _mail = mail;
    public string Description => "Send an email as me.";
    public string Usage => "SendMail({ \"To\":\"a@b.com\", \"Subject\":\"Hi\", \"Body\":\"Hello\" })";
    public Type InputType => typeof(SendMailInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"To\":{\"type\":\"string\"},\"Subject\":{\"type\":\"string\"},\"Body\":{\"type\":\"string\"},\"Html\":{\"type\":\"boolean\"}},\"required\":[\"To\",\"Subject\",\"Body\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (SendMailInput)input;
        await _mail.SendAsync(p.To, p.Subject, p.Body, p.Html);
        ctx.AddToolMessage("Sent.");
        return ToolResult.Success("Sent.", ctx);
    }
}

public sealed class ReplyMailInput
{
    [UserField(required:true, display:"Message Id")] public string MessageId { get; set; } = "";
    [UserField(required:true, display:"Body")] public string Body { get; set; } = "";
    [UserField(display:"Reply All")] public bool ReplyAll { get; set; } = false;
    [UserField(display:"HTML")] public bool Html { get; set; } = false;
}

[IsConfigurable("tool.mail.reply")]
public sealed class ReplyMailTool : ITool
{
    private readonly MailClient _mail;
    public ReplyMailTool(MailClient mail) => _mail = mail;
    public string Description => "Reply to an email (optionally Reply All).";
    public string Usage => "ReplyMail({ \"MessageId\":\"<id>\", \"Body\":\"Thanks!\", \"ReplyAll\":false })";
    public Type InputType => typeof(ReplyMailInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"MessageId\":{\"type\":\"string\"},\"Body\":{\"type\":\"string\"},\"ReplyAll\":{\"type\":\"boolean\"},\"Html\":{\"type\":\"boolean\"}},\"required\":[\"MessageId\",\"Body\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (ReplyMailInput)input;
        await _mail.ReplyAsync(p.MessageId, p.Body, p.ReplyAll, p.Html);
        ctx.AddToolMessage("Replied.");
        return ToolResult.Success("Replied.", ctx);
    }
}

public sealed class MoveMailInput
{
    [UserField(required:true, display:"Message Id")] public string MessageId { get; set; } = "";
    [UserField(required:true, display:"Destination folder (name or id)")] public string Destination { get; set; } = "DeletedItems";
}

[IsConfigurable("tool.mail.move")]
public sealed class MoveMailTool : ITool
{
    private readonly MailClient _mail;
    public MoveMailTool(MailClient mail) => _mail = mail;
    public string Description => "Move a message to a folder (e.g., Inbox→MyFolder or to DeletedItems).";
    public string Usage => "MoveMail({ \"MessageId\":\"<id>\", \"Destination\":\"MyFolder\" })";
    public Type InputType => typeof(MoveMailInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"MessageId\":{\"type\":\"string\"},\"Destination\":{\"type\":\"string\"}},\"required\":[\"MessageId\",\"Destination\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (MoveMailInput)input;
        var id = await _mail.MoveAsync(p.MessageId, p.Destination);
        var msg = $"Moved to '{p.Destination}' (new id:{id ?? "?"})";
        ctx.AddToolMessage(msg);
        return ToolResult.Success(msg, ctx);
    }
}
