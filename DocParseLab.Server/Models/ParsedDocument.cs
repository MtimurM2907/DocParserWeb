namespace DocParseLab.Server.Models;

public class ParsedDocument
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Название в реестре (если пусто — используется имя файла).</summary>
    public string? Title { get; set; }

    public string OriginalFileType { get; set; } = "pdf";

    /// <summary>Тип документа в реестре: general, letter, memo, order, contract, instruction, regulation.</summary>
    public string DocumentType { get; set; } = DocumentTypes.General;

    public string ProcessingProfile { get; set; } = "general";

    public string DataClassification { get; set; } = DocumentDataClassifications.Default;

    public string WorkflowStatus { get; set; } = DocumentWorkflowStatuses.Draft;

    public int? CurrentApproverUserId { get; set; }
    public AppUser? CurrentApprover { get; set; }

    public string? WorkflowComment { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? WorkflowCompletedAt { get; set; }

    /// <summary>Срок согласования (опционально).</summary>
    public DateTime? ApprovalDueAt { get; set; }

    /// <summary>Относительный путь к сохранённому оригиналу файла.</summary>
    public string? OriginalStorageKey { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int? ResponsibleUserId { get; set; }
    public AppUser? ResponsibleUser { get; set; }

    /// <summary>Теги через запятую.</summary>
    public string? Tags { get; set; }

    public int? OwnerId { get; set; }
    public AppUser? Owner { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public string FullText { get; set; } = string.Empty;

    /// <summary>Текст сразу после OCR/извлечения, до постобработки и Hunspell.</summary>
    public string? RawExtractedText { get; set; }

    public string? EditedText { get; set; }

    public DateTime? EditedAt { get; set; }

    public string StructuredJson { get; set; } = string.Empty;

    public string? AiSummary { get; set; }

    public int? EditLockedByUserId { get; set; }
    public AppUser? EditLockedByUser { get; set; }
    public DateTime? EditLockExpiresAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    public ICollection<DocumentWorkflowHistory> WorkflowHistory { get; set; } = new List<DocumentWorkflowHistory>();
    public ICollection<DocumentSignature> Signatures { get; set; } = new List<DocumentSignature>();
    public ICollection<DocumentApprovalStep> ApprovalSteps { get; set; } = new List<DocumentApprovalStep>();
    public ICollection<DocumentComment> Comments { get; set; } = new List<DocumentComment>();
    public ICollection<DocumentAccessLog> AccessLogs { get; set; } = new List<DocumentAccessLog>();
}
