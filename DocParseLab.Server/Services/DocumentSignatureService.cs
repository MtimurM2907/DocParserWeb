using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Data;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Services;

public sealed class SignatureVerificationResult
{
    public bool HasSignatures { get; set; }
    public bool TextMatchesLastSignature { get; set; }
    public string CurrentTextHashSha256 { get; set; } = string.Empty;
    public string? LastSignatureHashSha256 { get; set; }
    public DateTime? LastSignedAt { get; set; }
    public string? LastSignerEmail { get; set; }
}

public interface IDocumentSignatureService
{
    string GetCanonicalText(ParsedDocument document);
    string ComputeTextHash(string canonicalText);
    Task<bool> CanSignAsync(int userId, ParsedDocument document, CancellationToken cancellationToken = default);
    Task<DocumentSignature> SignAsync(
        ParsedDocument document,
        AppUser signer,
        string? comment,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentSignature>> GetSignaturesAsync(int documentId, CancellationToken cancellationToken = default);
    Task<SignatureVerificationResult> VerifyAsync(ParsedDocument document, CancellationToken cancellationToken = default);
    Task<(bool CanSign, int SignatureCount, bool TextIntegrityValid)> GetUiFlagsAsync(
        int? userId,
        ParsedDocument document,
        CancellationToken cancellationToken = default);
    Task RevokeLastAsync(ParsedDocument document, int adminUserId, CancellationToken cancellationToken = default);
}

public sealed class DocumentSignatureService : IDocumentSignatureService
{
    private readonly AppDbContext _db;
    private readonly IDocumentAccessService _access;
    private readonly INotificationService _notifications;

    public DocumentSignatureService(AppDbContext db, IDocumentAccessService access, INotificationService notifications)
    {
        _db = db;
        _access = access;
        _notifications = notifications;
    }

    public string GetCanonicalText(ParsedDocument document) =>
        TextStructureFormatter.NormalizeForStorage(document.EditedText ?? document.FullText ?? string.Empty);

    public string ComputeTextHash(string canonicalText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<bool> CanSignAsync(int userId, ParsedDocument document, CancellationToken cancellationToken = default)
    {
        if (document.WorkflowStatus != DocumentWorkflowStatuses.Approved)
            return false;

        if (!await _access.CanReadAsync(userId, document, cancellationToken))
            return false;

        var user = await _access.GetUserAsync(userId, cancellationToken);
        if (user == null || user.Role == UserRoles.Viewer)
            return false;

        if (user.Role == UserRoles.Admin)
            return true;

        if (document.OwnerId == userId || document.ResponsibleUserId == userId)
            return true;

        if (user.Role == UserRoles.Manager
            && document.DepartmentId != null
            && document.DepartmentId == user.DepartmentId)
            return true;

        return false;
    }

    public async Task<DocumentSignature> SignAsync(
        ParsedDocument document,
        AppUser signer,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        if (document.WorkflowStatus != DocumentWorkflowStatuses.Approved)
            throw new InvalidOperationException("Подписать можно только согласованный документ.");

        if (!await CanSignAsync(signer.Id, document, cancellationToken))
            throw new InvalidOperationException("Недостаточно прав для подписания документа.");

        var canonical = GetCanonicalText(document);
        if (string.IsNullOrWhiteSpace(canonical))
            throw new InvalidOperationException("Нельзя подписать документ с пустым текстом.");

        var hash = ComputeTextHash(canonical);
        var signature = new DocumentSignature
        {
            DocumentId = document.Id,
            SignedByUserId = signer.Id,
            TextHashSha256 = hash,
            SignedAt = DateTime.UtcNow,
            SignerEmailSnapshot = signer.Email,
            SignerDisplayNameSnapshot = signer.DisplayName,
            SignerRoleSnapshot = signer.Role,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            SignatureKind = SignatureKinds.Internal,
        };

        _db.DocumentSignatures.Add(signature);
        document.WorkflowStatus = DocumentWorkflowStatuses.Signed;
        document.WorkflowCompletedAt = DateTime.UtcNow;

        _db.DocumentWorkflowHistory.Add(new DocumentWorkflowHistory
        {
            DocumentId = document.Id,
            UserId = signer.Id,
            Action = WorkflowActions.Signed,
            Comment = signature.Comment,
            CreatedAt = DateTime.UtcNow,
        });

        if (document.OwnerId.HasValue && document.OwnerId != signer.Id)
        {
            await _notifications.NotifyUserAsync(
                document.OwnerId.Value,
                "Документ подписан",
                $"Документ «{document.Title ?? document.FileName}» подписан пользователем {signer.Email}.",
                document.Id,
                cancellationToken);
        }

        return signature;
    }

    public async Task RevokeLastAsync(ParsedDocument document, int adminUserId, CancellationToken cancellationToken = default)
    {
        if (document.WorkflowStatus != DocumentWorkflowStatuses.Signed)
            throw new InvalidOperationException("Отменить подпись можно только у подписанного документа.");

        var last = await _db.DocumentSignatures
            .Where(s => s.DocumentId == document.Id)
            .OrderByDescending(s => s.SignedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (last == null)
            throw new InvalidOperationException("Подписей не найдено.");

        _db.DocumentSignatures.Remove(last);
        document.WorkflowStatus = DocumentWorkflowStatuses.Approved;
        document.WorkflowCompletedAt = null;

        _db.DocumentWorkflowHistory.Add(new DocumentWorkflowHistory
        {
            DocumentId = document.Id,
            UserId = adminUserId,
            Action = WorkflowActions.SignatureRevoked,
            Comment = $"Отменена подпись {last.SignerEmailSnapshot}",
            CreatedAt = DateTime.UtcNow,
        });
    }

    public async Task<IReadOnlyList<DocumentSignature>> GetSignaturesAsync(
        int documentId,
        CancellationToken cancellationToken = default) =>
        await _db.DocumentSignatures
            .AsNoTracking()
            .Where(s => s.DocumentId == documentId)
            .OrderByDescending(s => s.SignedAt)
            .ToListAsync(cancellationToken);

    public async Task<SignatureVerificationResult> VerifyAsync(
        ParsedDocument document,
        CancellationToken cancellationToken = default)
    {
        var currentHash = ComputeTextHash(GetCanonicalText(document));
        var last = await _db.DocumentSignatures
            .AsNoTracking()
            .Where(s => s.DocumentId == document.Id)
            .OrderByDescending(s => s.SignedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new SignatureVerificationResult
        {
            HasSignatures = last != null,
            TextMatchesLastSignature = last != null && last.TextHashSha256 == currentHash,
            CurrentTextHashSha256 = currentHash,
            LastSignatureHashSha256 = last?.TextHashSha256,
            LastSignedAt = last?.SignedAt,
            LastSignerEmail = last?.SignerEmailSnapshot,
        };
    }

    public async Task<(bool CanSign, int SignatureCount, bool TextIntegrityValid)> GetUiFlagsAsync(
        int? userId,
        ParsedDocument document,
        CancellationToken cancellationToken = default)
    {
        var count = await _db.DocumentSignatures.CountAsync(s => s.DocumentId == document.Id, cancellationToken);
        var verify = await VerifyAsync(document, cancellationToken);
        var canSign = userId.HasValue && await CanSignAsync(userId.Value, document, cancellationToken);
        return (canSign, count, verify.TextMatchesLastSignature);
    }
}
