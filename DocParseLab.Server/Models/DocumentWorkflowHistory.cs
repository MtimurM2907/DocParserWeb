namespace DocParseLab.Server.Models;

public class DocumentWorkflowHistory
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public ParsedDocument? Document { get; set; }

    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
