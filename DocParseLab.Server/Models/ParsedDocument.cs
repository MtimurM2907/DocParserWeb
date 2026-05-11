namespace DocParseLab.Server.Models;

public class ParsedDocument
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string OriginalFileType { get; set; } = "pdf";

    public int? OwnerId { get; set; }
    public AppUser? Owner { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// </summary>
    public string FullText { get; set; } = string.Empty;

    /// <summary>
    /// Отредактированный пользователем текст (если есть). Оригинал остаётся в FullText.
    /// </summary>
    public string? EditedText { get; set; }

    public DateTime? EditedAt { get; set; }

    /// <summary>
    /// </summary>
    public string StructuredJson { get; set; } = string.Empty;

    /// <summary>
    /// </summary>
    public string? AiSummary { get; set; }

    public ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
}


