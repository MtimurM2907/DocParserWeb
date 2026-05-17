using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DocParseLab.Server.Data;
using DocParseLab.Server.Hubs;
using DocParseLab.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocParseLab.Server.Services;

public static class DocumentEditLock
{
    public static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(5);
}

public sealed class LdapOptions
{
    public const string SectionName = "Ldap";
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string BaseDn { get; set; } = string.Empty;
    public string BindDnTemplate { get; set; } = "{email}";
    public string SearchFilter { get; set; } = "(mail={email})";
    public string? ServiceAccountDn { get; set; }
    public string? ServiceAccountPassword { get; set; }
}

public interface ILdapAuthenticationService
{
    Task<(bool Ok, string? Email)> TryAuthenticateAsync(string email, string password, CancellationToken cancellationToken = default);
}

public sealed class LdapAuthenticationService : ILdapAuthenticationService
{
    private readonly LdapOptions _options;
    private readonly ILogger<LdapAuthenticationService> _logger;

    public LdapAuthenticationService(IOptions<LdapOptions> options, ILogger<LdapAuthenticationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<(bool Ok, string? Email)> TryAuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Task.FromResult((false, (string?)null));

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Task.FromResult((false, (string?)null));

        try
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(_options.Host, _options.Port));
            connection.SessionOptions.ProtocolVersion = 3;
            if (_options.UseSsl)
                connection.SessionOptions.SecureSocketLayer = true;

            var bindDn = _options.BindDnTemplate.Replace("{email}", normalizedEmail, StringComparison.OrdinalIgnoreCase);
            connection.Credential = new NetworkCredential(bindDn, password);
            connection.AuthType = AuthType.Basic;
            connection.Bind();

            return Task.FromResult<(bool, string?)>((true, normalizedEmail));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LDAP authentication failed for {Email}", email);
            return Task.FromResult((false, (string?)null));
        }
    }
}

public sealed class FileScanOptions
{
    public const string SectionName = "FileScan";
    public bool Enabled { get; set; } = true;
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".docx"];
}

public interface IFileScanService
{
    void ValidateUpload(IFormFile file);
}

public sealed class FileScanService : IFileScanService
{
    private readonly FileScanOptions _options;

    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    public FileScanService(IOptions<FileScanOptions> options) => _options = options.Value;

    public void ValidateUpload(IFormFile file)
    {
        if (!_options.Enabled)
            return;

        if (file.Length == 0)
            throw new InvalidOperationException("Пустой файл.");
        if (file.Length > _options.MaxFileSizeBytes)
            throw new InvalidOperationException($"Файл слишком большой (макс. {_options.MaxFileSizeBytes / (1024 * 1024)} МБ).");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Формат {ext} не разрешён политикой загрузки.");

        using var stream = file.OpenReadStream();
        var header = new byte[4];
        var read = stream.Read(header, 0, 4);
        if (read < 4)
            throw new InvalidOperationException("Не удалось прочитать файл.");

        var valid = ext switch
        {
            ".pdf" => header.AsSpan().StartsWith(PdfMagic),
            ".docx" => header.AsSpan().StartsWith(ZipMagic),
            _ => false,
        };
        if (!valid)
            throw new InvalidOperationException("Содержимое файла не соответствует расширению (проверка сигнатуры).");
    }
}

public sealed class DocumentEditLockStatus
{
    public bool IsLocked { get; set; }
    public bool CanEdit { get; set; }
    public int? LockedByUserId { get; set; }
    public string? LockedByEmail { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public interface IDocumentEditLockService
{
    Task<DocumentEditLockStatus> GetStatusAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
    Task<DocumentEditLockStatus> AcquireAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
    Task ReleaseAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
    Task RenewAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
    Task EnsureCanEditAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
}

public sealed class DocumentEditLockService : IDocumentEditLockService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<DocumentHub> _hub;

    public DocumentEditLockService(AppDbContext db, IHubContext<DocumentHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<DocumentEditLockStatus> GetStatusAsync(
        ParsedDocument doc,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await ClearExpiredAsync(doc, cancellationToken);
        return BuildStatus(doc, userId);
    }

    public async Task<DocumentEditLockStatus> AcquireAsync(
        ParsedDocument doc,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await ClearExpiredAsync(doc, cancellationToken);
        if (doc.EditLockedByUserId is int lockUser && lockUser != userId
            && doc.EditLockExpiresAt > DateTime.UtcNow)
        {
            var locker = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == lockUser, cancellationToken);
            throw new InvalidOperationException(
                $"Документ редактирует {locker?.Email ?? $"пользователь #{lockUser}"}.");
        }

        doc.EditLockedByUserId = userId;
        doc.EditLockExpiresAt = DateTime.UtcNow.Add(DocumentEditLock.DefaultLease);
        await _hub.Clients.Group(DocumentHub.DocumentGroup(doc.Id))
            .SendAsync("lockChanged", BuildStatus(doc, userId), cancellationToken);
        return BuildStatus(doc, userId);
    }

    public async Task ReleaseAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default)
    {
        if (doc.EditLockedByUserId == userId || doc.EditLockedByUserId == null)
        {
            doc.EditLockedByUserId = null;
            doc.EditLockExpiresAt = null;
            await _hub.Clients.Group(DocumentHub.DocumentGroup(doc.Id))
                .SendAsync("lockChanged", BuildStatus(doc, userId), cancellationToken);
        }
    }

    public async Task RenewAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default)
    {
        if (doc.EditLockedByUserId == userId)
        {
            doc.EditLockExpiresAt = DateTime.UtcNow.Add(DocumentEditLock.DefaultLease);
            await _hub.Clients.Group(DocumentHub.DocumentGroup(doc.Id))
                .SendAsync("lockChanged", BuildStatus(doc, userId), cancellationToken);
        }
    }

    public async Task EnsureCanEditAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default)
    {
        await ClearExpiredAsync(doc, cancellationToken);
        if (doc.EditLockedByUserId is int lockUser && lockUser != userId
            && doc.EditLockExpiresAt > DateTime.UtcNow)
            throw new InvalidOperationException("Документ заблокирован другим пользователем.");
    }

    private async Task ClearExpiredAsync(ParsedDocument doc, CancellationToken cancellationToken)
    {
        if (doc.EditLockExpiresAt != null && doc.EditLockExpiresAt <= DateTime.UtcNow)
        {
            doc.EditLockedByUserId = null;
            doc.EditLockExpiresAt = null;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private DocumentEditLockStatus BuildStatus(ParsedDocument doc, int userId)
    {
        var locked = doc.EditLockedByUserId is int id && doc.EditLockExpiresAt > DateTime.UtcNow;
        return new DocumentEditLockStatus
        {
            IsLocked = locked,
            CanEdit = !locked || doc.EditLockedByUserId == userId,
            LockedByUserId = doc.EditLockedByUserId,
            ExpiresAt = doc.EditLockExpiresAt,
        };
    }
}

public interface IExternalSignatureService
{
    Task<DocumentSignature> RegisterExternalAsync(
        ParsedDocument document,
        AppUser signer,
        IFormFile? signatureFile,
        string? certificateSubject,
        string? certificateThumbprint,
        string? comment,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalSignatureService : IExternalSignatureService
{
    private readonly AppDbContext _db;
    private readonly IDocumentSignatureService _signatures;

    public ExternalSignatureService(AppDbContext db, IDocumentSignatureService signatures)
    {
        _db = db;
        _signatures = signatures;
    }

    public async Task<DocumentSignature> RegisterExternalAsync(
        ParsedDocument document,
        AppUser signer,
        IFormFile? signatureFile,
        string? certificateSubject,
        string? certificateThumbprint,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        if (document.WorkflowStatus != DocumentWorkflowStatuses.Approved
            && document.WorkflowStatus != DocumentWorkflowStatuses.Signed)
            throw new InvalidOperationException("Внешнюю подпись можно добавить к согласованному или подписанному документу.");

        if (!await _signatures.CanSignAsync(signer.Id, document, cancellationToken))
            throw new InvalidOperationException("Недостаточно прав для подписания.");

        string? payloadB64 = null;
        bool? verified = null;
        if (signatureFile != null && signatureFile.Length > 0)
        {
            await using var ms = new MemoryStream();
            await signatureFile.CopyToAsync(ms, cancellationToken);
            payloadB64 = Convert.ToBase64String(ms.ToArray());
            var canonicalForVerify = _signatures.GetCanonicalText(document);
            verified = TryVerifyCms(ms.ToArray(), certificateThumbprint, canonicalForVerify);
        }

        var canonical = _signatures.GetCanonicalText(document);
        var hash = _signatures.ComputeTextHash(canonical);
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
            SignatureKind = SignatureKinds.External,
            CertificateSubject = certificateSubject,
            CertificateThumbprint = certificateThumbprint,
            ExternalPayloadBase64 = payloadB64,
            ExternalCryptoVerified = verified,
        };

        _db.DocumentSignatures.Add(signature);
        if (document.WorkflowStatus == DocumentWorkflowStatuses.Approved)
        {
            document.WorkflowStatus = DocumentWorkflowStatuses.Signed;
            document.WorkflowCompletedAt = DateTime.UtcNow;
        }

        _db.DocumentWorkflowHistory.Add(new DocumentWorkflowHistory
        {
            DocumentId = document.Id,
            UserId = signer.Id,
            Action = WorkflowActions.Signed,
            Comment = $"УКЭП/внешняя: {certificateSubject ?? certificateThumbprint ?? "файл подписи"}",
            CreatedAt = DateTime.UtcNow,
        });

        return signature;
    }

    private static bool? TryVerifyCms(byte[] cmsBytes, string? thumbprint, string? detachedContentUtf8 = null)
    {
        try
        {
            if (cmsBytes.Length == 0)
                return false;

            SignedCms signedCms;
            if (!string.IsNullOrEmpty(detachedContentUtf8))
            {
                var content = new ContentInfo(Encoding.UTF8.GetBytes(detachedContentUtf8));
                signedCms = new SignedCms(content, detached: true);
                signedCms.Decode(cmsBytes);
            }
            else
            {
                signedCms = new SignedCms();
                signedCms.Decode(cmsBytes);
            }

            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                signedCms.CheckSignature(true);
                return true;
            }

            var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            try
            {
                var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint.Replace(" ", ""), false);
                if (certs.Count == 0)
                    return false;
                signedCms.CheckSignature(certs, true);
                return true;
            }
            finally
            {
                store.Close();
            }
        }
        catch
        {
            return false;
        }
    }
}
