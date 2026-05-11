using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace DocParseLab.Server.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(
        string toEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        using var message = CreateBaseMessage(toEmail, subject, body);
        await SendInternalAsync(message, toEmail, cancellationToken);
    }

    public async Task SendDocumentAsync(
        string toEmail,
        string subject,
        string body,
        byte[] attachmentBytes,
        string attachmentFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.Host)) missing.Add("Smtp:Host");
        if (string.IsNullOrWhiteSpace(_options.Username)) missing.Add("Smtp:Username");
        if (string.IsNullOrWhiteSpace(_options.Password)) missing.Add("Smtp:Password");
        if (string.IsNullOrWhiteSpace(_options.FromEmail)) missing.Add("Smtp:FromEmail");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"SMTP не настроен. Заполните: {string.Join(", ", missing)}.");
        }

        using var message = CreateBaseMessage(toEmail, subject, body);

        using var attachmentStream = new MemoryStream(attachmentBytes);
        using var attachment = new Attachment(attachmentStream, attachmentFileName, contentType);
        message.Attachments.Add(attachment);

        await SendInternalAsync(message, toEmail, cancellationToken);
    }

    private MailMessage CreateBaseMessage(string toEmail, string subject, string body)
    {
        var fromEmail = _options.FromEmail.Trim();
        var message = new MailMessage
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
            From = new MailAddress(fromEmail, _options.FromName)
        };
        message.To.Add(new MailAddress(toEmail));
        return message;
    }

    private async Task SendInternalAsync(MailMessage message, string toEmail, CancellationToken cancellationToken)
    {
        var host = _options.Host.Trim();
        var username = _options.Username.Trim();
        var password = _options.Password.Trim();

        using var client = new SmtpClient(host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось отправить email на {Email}", toEmail);
            throw;
        }
    }
}
