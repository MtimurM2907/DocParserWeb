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
    public string? Comment { get; set; }
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

public sealed class UpdateUserProfileRequest
{
    public string? DisplayName { get; set; }
    public int? DepartmentId { get; set; }
}

public sealed class SetUserRoleRequest
{
    public string Role { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
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
