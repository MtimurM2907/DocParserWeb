using DocParseLab.Server.Models;

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

    public static ParsedDocumentResponse FromEntity(ParsedDocument entity, string? ownerEmail = null, int shareCount = 0)
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
            ShareCount = shareCount
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
    public string OriginalFileType { get; set; } = "pdf";
    public DateTime UploadedAt { get; set; }
    public int TextLength { get; set; }
    public string? AiSummaryPreview { get; set; }

    public static DocumentBriefResponse FromEntity(ParsedDocument entity)
    {
        return new DocumentBriefResponse
        {
            Id = entity.Id,
            FileName = entity.FileName,
            OriginalFileType = entity.OriginalFileType,
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
