using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

#region Mail Subsystem Interfaces
public record MailRecipient(string EmailAddress);

public interface IMailMessage
{
    string Id { get; }
    string ParentFolderId { get; }
    string Subject { get; }
    string BodyPreview { get; }
    DateTimeOffset ReceivedDateTime { get; }
    DateTimeOffset? SentDateTime { get; }
    bool IsRead { get; }
    bool IsDraft { get; }
    MailRecipient From { get; }
    List<MailRecipient> ToRecipients { get; }
    List<MailRecipient> CcRecipients { get; }
    List<MailRecipient> BccRecipients { get; }
}

public interface IMailFolder
{
    string DisplayName { get; }
    List<IMailFolder> ChildFolders { get; }
    List<IMailMessage> Messages { get; }
    string ParentFolderId { get; }
    int TotalItemCount { get; }
    int UnreadItemCount { get; }
}

public interface IMailProvider
{
    Task<IMailMessage?> GetMessageAsync(string id, CancellationToken ct = default);
    Task<IMailFolder?> GetFolderByIdOrNameAsync(string idOrName, CancellationToken ct = default);
    
    Task<List<IMailFolder>> ListFoldersAsync(string? folderIdOrName = null, int top = 50, CancellationToken ct = default);
    Task<List<IMailMessage>> ListMessagesSinceAsync(IMailFolder? folder, TimeSpan window, int top = 50, CancellationToken ct = default);

    Task<IMailMessage> DraftMessage(string folderIdOrName, string subject, string body, List<MailRecipient> to, List<MailRecipient> cc, List<MailRecipient> bcc);
    Task<IMailMessage> DraftReplyAsync(IMailMessage message, string body, CancellationToken ct = default);
    Task<IMailMessage> DraftReplyAllAsync(IMailMessage message, string body, CancellationToken ct = default);
    Task<IMailMessage> DraftForwardAsync(IMailMessage message, List<MailRecipient> to, List<MailRecipient>? cc = null, List<MailRecipient>? bcc = null, string? body = null, CancellationToken ct = default);

    Task SendAsync(IMailMessage message, CancellationToken ct = default);
    Task MoveAsync(IMailMessage message, IMailFolder folder, CancellationToken ct = default);
    Task DeleteAsync(IMailMessage message, CancellationToken ct = default);
}

#endregion