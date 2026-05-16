namespace DocParseLab.Server.DTOs;

public sealed class BatchParseResponse
{
    public List<ParsedDocumentResponse> Documents { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public sealed class ChecklistValidateResponse
{
    public string ChecklistId { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public List<string> Missing { get; set; } = new();
}

public sealed class ExtractedEntitiesResponse
{
    public List<string> Dates { get; set; } = new();
    public List<string> Money { get; set; } = new();
    public List<string> Inn { get; set; } = new();
    public List<string> Emails { get; set; } = new();
}

public sealed class AuditLogEntryResponse
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? UserId { get; set; }
    public string? UserEmailSnapshot { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Resource { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
