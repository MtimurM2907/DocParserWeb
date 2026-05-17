using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Data;
using DocParseLab.Server.Hubs;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Services;

public interface IDocumentFileStorageService
{
    Task<string?> SaveOriginalAsync(int documentId, string fileName, Stream content, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType, string FileName)?> OpenOriginalAsync(ParsedDocument doc, CancellationToken cancellationToken = default);
}

public sealed class LocalDocumentFileStorageService : IDocumentFileStorageService
{
    private readonly string _root;

    public LocalDocumentFileStorageService(IWebHostEnvironment env)
    {
        _root = Path.Combine(env.ContentRootPath, "Data", "uploads");
        Directory.CreateDirectory(_root);
    }

    public async Task<string?> SaveOriginalAsync(int documentId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) ext = ".bin";
        var key = $"{documentId}{ext}";
        var path = Path.Combine(_root, key);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, cancellationToken);
        return key;
    }

    public Task<(Stream Stream, string ContentType, string FileName)?> OpenOriginalAsync(
        ParsedDocument doc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(doc.OriginalStorageKey))
            return Task.FromResult<(Stream, string, string)?>(null);

        var path = Path.Combine(_root, doc.OriginalStorageKey);
        if (!File.Exists(path))
            return Task.FromResult<(Stream, string, string)?>(null);

        var contentType = doc.OriginalFileType switch
        {
            "pdf" => "application/pdf",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream",
        };
        Stream stream = File.OpenRead(path);
        return Task.FromResult<(Stream, string, string)?>((stream, contentType, doc.FileName));
    }
}

public interface INotificationService
{
    Task NotifyUserAsync(int userId, string title, string body, int? documentId = null, CancellationToken cancellationToken = default);
    Task NotifyWorkflowAsync(ParsedDocument doc, string title, string body, IEnumerable<int> userIds, CancellationToken cancellationToken = default);
}

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly IDocumentRealtimeService _realtime;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext db,
        IEmailSender email,
        IDocumentRealtimeService realtime,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _email = email;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task NotifyUserAsync(int userId, string title, string body, int? documentId = null, CancellationToken cancellationToken = default)
    {
        var entry = new UserNotification
        {
            UserId = userId,
            Title = title,
            Body = body,
            DocumentId = documentId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.UserNotifications.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        await _realtime.PushNotificationAsync(
            userId,
            new { entry.Id, entry.Title, entry.Body, entry.DocumentId, entry.CreatedAt },
            cancellationToken);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user != null)
        {
            try
            {
                await _email.SendEmailAsync(user.Email, title, body, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить email-уведомление пользователю {Email}", user.Email);
            }
        }
    }

    public async Task NotifyWorkflowAsync(
        ParsedDocument doc,
        string title,
        string body,
        IEnumerable<int> userIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var id in userIds.Distinct())
            await NotifyUserAsync(id, title, body, doc.Id, cancellationToken);
    }
}

public interface IDocumentAccessLogService
{
    Task LogAsync(int documentId, int? userId, string action, string? ipAddress, CancellationToken cancellationToken = default);
}

public sealed class DocumentAccessLogService : IDocumentAccessLogService
{
    private readonly AppDbContext _db;

    public DocumentAccessLogService(AppDbContext db) => _db = db;

    public async Task LogAsync(int documentId, int? userId, string action, string? ipAddress, CancellationToken cancellationToken = default)
    {
        _db.DocumentAccessLogs.Add(new DocumentAccessLog
        {
            DocumentId = documentId,
            UserId = userId,
            Action = action,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public interface ITextDiffService
{
    IReadOnlyList<TextDiffLine> Diff(string oldText, string newText);
}

public sealed record TextDiffLine(string Kind, string Text);

public sealed class TextDiffService : ITextDiffService
{
    public IReadOnlyList<TextDiffLine> Diff(string oldText, string newText)
    {
        var a = (oldText ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var b = (newText ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var result = new List<TextDiffLine>();
        var n = Math.Max(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var lineA = i < a.Length ? a[i] : null;
            var lineB = i < b.Length ? b[i] : null;
            if (lineA == lineB)
            {
                if (lineA != null) result.Add(new TextDiffLine("same", lineA));
            }
            else
            {
                if (lineA != null) result.Add(new TextDiffLine("removed", lineA));
                if (lineB != null) result.Add(new TextDiffLine("added", lineB));
            }
        }
        return result;
    }
}

public static class GigaChatCacheKeys
{
    public static string ForText(string plainText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainText ?? string.Empty));
        return Convert.ToHexString(bytes);
    }
}
