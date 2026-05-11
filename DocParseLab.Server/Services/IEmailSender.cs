namespace DocParseLab.Server.Services;

public interface IEmailSender
{
    Task SendEmailAsync(
        string toEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default);

    Task SendDocumentAsync(
        string toEmail,
        string subject,
        string body,
        byte[] attachmentBytes,
        string attachmentFileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
