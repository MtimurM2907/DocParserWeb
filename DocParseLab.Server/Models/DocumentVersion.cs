namespace DocParseLab.Server.Models;

public class DocumentVersion
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public ParsedDocument? Document { get; set; }

    public int VersionNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public string ChangeType { get; set; } = "edit";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
