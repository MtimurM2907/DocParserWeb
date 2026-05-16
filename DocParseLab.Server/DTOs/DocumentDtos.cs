using DocParseLab.Server.Models;
using DocParseLab.Server.Services;

namespace DocParseLab.Server.DTOs;

/// <summary>
/// DTO ответа с информацией о документе
/// </summary>
public sealed class ParsedDocumentResponse
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileType { get; set; } = "pdf";
    public int? OwnerId { get; set; }
    public string? OwnerEmail { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? AiSummary { get; set; }
    public int TextLength => FullText?.Length ?? 0;
    public string OriginalText { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public string? EditedText { get; set; }
    public DateTime? EditedAt { get; set; }
    public string StructuredJson { get; set; } = string.Empty;
    public int ShareCount { get; set; }
    public string ProcessingProfile { get; set; } = "general";
    public string DataClassification { get; set; } = "Internal";
    public string? Title { get; set; }
    public string DocumentType { get; set; } = "general";
    public string WorkflowStatus { get; set; } = "Draft";
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int? ResponsibleUserId { get; set; }
    public string? ResponsibleUserEmail { get; set; }
    public int? CurrentApproverUserId { get; set; }
    public string? CurrentApproverEmail { get; set; }
    public string? WorkflowComment { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? Tags { get; set; }
    public bool CanEdit { get; set; }
    public bool CanApprove { get; set; }
    public bool CanSign { get; set; }
    public int SignatureCount { get; set; }
    public bool TextIntegrityValid { get; set; } = true;
    public DateTime? LastSignedAt { get; set; }
    public string? LastSignerEmail { get; set; }

    public static ParsedDocumentResponse FromEntity(
        ParsedDocument entity,
        string? ownerEmail = null,
        int shareCount = 0,
        int? currentUserId = null,
        string? departmentName = null,
        string? responsibleEmail = null,
        string? approverEmail = null,
        bool canSign = false,
        int signatureCount = 0,
        bool textIntegrityValid = true,
        DateTime? lastSignedAt = null,
        string? lastSignerEmail = null)
    {
        return new ParsedDocumentResponse
        {
            Id = entity.Id,
            FileName = entity.FileName,
            OriginalFileType = entity.OriginalFileType,
            OwnerId = entity.OwnerId,
            OwnerEmail = ownerEmail,
            UploadedAt = entity.UploadedAt,
            AiSummary = entity.AiSummary,
            // Для отображения возвращаем "актуальный" текст (с учётом правок),
            // но также отдаём EditedText отдельно, чтобы UI мог показать состояние.
            OriginalText = entity.FullText,
            FullText = entity.EditedText ?? entity.FullText,
            EditedText = entity.EditedText,
            EditedAt = entity.EditedAt,
            StructuredJson = entity.StructuredJson,
            ShareCount = shareCount,
            ProcessingProfile = entity.ProcessingProfile,
            DataClassification = entity.DataClassification,
            Title = entity.Title,
            DocumentType = entity.DocumentType,
            WorkflowStatus = entity.WorkflowStatus,
            DepartmentId = entity.DepartmentId,
            DepartmentName = departmentName ?? entity.Department?.Name,
            ResponsibleUserId = entity.ResponsibleUserId,
            ResponsibleUserEmail = responsibleEmail ?? entity.ResponsibleUser?.Email,
            CurrentApproverUserId = entity.CurrentApproverUserId,
            CurrentApproverEmail = approverEmail ?? entity.CurrentApprover?.Email,
            WorkflowComment = entity.WorkflowComment,
            SubmittedAt = entity.SubmittedAt,
            Tags = entity.Tags,
            CanEdit = currentUserId == null
                ? false
                : entity.WorkflowStatus is not ("OnApproval" or "Approved" or "Signed" or "Archived")
                  && (entity.OwnerId == currentUserId || entity.ResponsibleUserId == currentUserId),
            CanApprove = currentUserId != null
                && entity.WorkflowStatus == "OnApproval"
                && entity.CurrentApproverUserId == currentUserId,
            CanSign = canSign,
            SignatureCount = signatureCount,
            TextIntegrityValid = textIntegrityValid,
            LastSignedAt = lastSignedAt,
            LastSignerEmail = lastSignerEmail,
        };
    }
}

/// <summary>
/// DTO краткой информации о документе (для списков)
/// </summary>
public sealed class DocumentBriefResponse
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string OriginalFileType { get; set; } = "pdf";
    public string DocumentType { get; set; } = string.Empty;
    public string WorkflowStatus { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public int TextLength { get; set; }
    public string? AiSummaryPreview { get; set; }

    public static DocumentBriefResponse FromEntity(ParsedDocument entity)
    {
        return new DocumentBriefResponse
        {
            Id = entity.Id,
            FileName = entity.FileName,
            Title = OfficeDtoMapper.DisplayTitle(entity),
            OriginalFileType = entity.OriginalFileType,
            DocumentType = entity.DocumentType,
            WorkflowStatus = entity.WorkflowStatus,
            UploadedAt = entity.UploadedAt,
            TextLength = entity.FullText?.Length ?? 0,
            AiSummaryPreview = entity.AiSummary?.Length > 200 
                ? entity.AiSummary[..200] + "..." 
                : entity.AiSummary
        };
    }
}

public sealed class SendDocumentEmailRequest
{
    public string TargetEmail { get; set; } = string.Empty;
    public string Format { get; set; } = "docx";
}
