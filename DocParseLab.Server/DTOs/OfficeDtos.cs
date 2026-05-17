namespace DocParseLab.Server.DTOs;

public sealed class DepartmentResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class UserBriefResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
}

public sealed class CurrentUserResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
}

public sealed class DocumentRegistryItemResponse
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string WorkflowStatus { get; set; } = string.Empty;
    public string DataClassification { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public int? OwnerId { get; set; }
    public string? OwnerEmail { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int? ResponsibleUserId { get; set; }
    public string? ResponsibleUserEmail { get; set; }
    public int? CurrentApproverUserId { get; set; }
    public string? CurrentApproverEmail { get; set; }
    public string? Tags { get; set; }
    public string? AiSummaryPreview { get; set; }
}

public sealed class DocumentRegistryQuery
{
    public string? Status { get; set; }
    public string? DocumentType { get; set; }
    public int? DepartmentId { get; set; }
    public string? Search { get; set; }
    public bool MineOnly { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}

public sealed class DocumentRegistryPageResponse
{
    public List<DocumentRegistryItemResponse> Items { get; set; } = new();
    public int Total { get; set; }
}

public sealed class UpdateDocumentMetadataRequest
{
    public string? Title { get; set; }
    public string? DocumentType { get; set; }
    public int? DepartmentId { get; set; }
    public int? ResponsibleUserId { get; set; }
    public string? Tags { get; set; }
    public string? DataClassification { get; set; }
}

public sealed class SubmitForApprovalRequest
{
    public int ApproverUserId { get; set; }
    public List<int>? ApproverUserIds { get; set; }
    public string? Comment { get; set; }
    public DateTime? ApprovalDueAt { get; set; }
}

public sealed class WorkflowDecisionRequest
{
    public string? Comment { get; set; }
}

public sealed class DocumentVersionResponse
{
    public int Id { get; set; }
    public int VersionNumber { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public string? CreatedByEmail { get; set; }
    public int TextLength { get; set; }
}

public sealed class DocumentVersionDetailResponse
{
    public int Id { get; set; }
    public int VersionNumber { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public string? CreatedByEmail { get; set; }
    public int TextLength { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class WorkflowHistoryItemResponse
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UserEmail { get; set; }
}

public sealed class ApprovalTaskResponse
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string WorkflowStatus { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public string? OwnerEmail { get; set; }
    public string? WorkflowComment { get; set; }
}

public sealed class UpdateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string? Password { get; set; }
}

public sealed class SignDocumentRequest
{
    public string? Comment { get; set; }
}

public sealed class DocumentSignatureResponse
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public string TextHashSha256 { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string SignerEmail { get; set; } = string.Empty;
    public string? SignerDisplayName { get; set; }
    public string SignerRole { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string SignatureKind { get; set; } = string.Empty;
    public string? CertificateSubject { get; set; }
    public string? CertificateThumbprint { get; set; }
    public bool? ExternalCryptoVerified { get; set; }
}

public sealed class SignatureVerificationResponse
{
    public bool HasSignatures { get; set; }
    public bool TextMatchesLastSignature { get; set; }
    public string CurrentTextHashSha256 { get; set; } = string.Empty;
    public string? LastSignatureHashSha256 { get; set; }
    public DateTime? LastSignedAt { get; set; }
    public string? LastSignerEmail { get; set; }
}

/// <summary>Данные для подписи УКЭП через КриптоПро (отсоединённая CMS над каноническим текстом).</summary>
public sealed class DocumentSigningPayloadResponse
{
    public string TextHashSha256 { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public int ContentByteLength { get; set; }
}

public sealed class CreateDepartmentRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class BulkArchiveRequest
{
    public List<int> DocumentIds { get; set; } = new();
}

public sealed class BulkArchiveResponse
{
    public int ArchivedCount { get; set; }
}

public sealed class DocumentCommentResponse
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UserEmail { get; set; }
    public string? UserDisplayName { get; set; }
}

public sealed class AddDocumentCommentRequest
{
    public string Text { get; set; } = string.Empty;
}

public sealed class UserNotificationResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? DocumentId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class DocumentAccessLogResponse
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UserEmail { get; set; }
    public string? IpAddress { get; set; }
}

public sealed class VersionDiffResponse
{
    public int FromVersionId { get; set; }
    public int ToVersionId { get; set; }
    public List<TextDiffLineResponse> Lines { get; set; } = new();
}

public sealed class TextDiffLineResponse
{
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class ApprovalStepResponse
{
    public int StepOrder { get; set; }
    public int ApproverUserId { get; set; }
    public string? ApproverEmail { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime? ActedAt { get; set; }
}

public sealed class DocumentShareItemResponse
{
    public int ShareId { get; set; }
    public int ToUserId { get; set; }
    public string ToUserEmail { get; set; } = string.Empty;
    public DateTime SharedAt { get; set; }
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class DocumentEditLockStatusResponse
{
    public bool IsLocked { get; set; }
    public bool CanEdit { get; set; }
    public int? LockedByUserId { get; set; }
    public string? LockedByEmail { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed class ExternalSignRequest
{
    public string? CertificateSubject { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string? Comment { get; set; }
}
