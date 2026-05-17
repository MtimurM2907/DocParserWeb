namespace DocParseLab.Server.Models;

public class DocumentAccessLog
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public ParsedDocument Document { get; set; } = null!;
    public int? UserId { get; set; }
    public AppUser? User { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
}
