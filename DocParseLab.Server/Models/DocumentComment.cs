namespace DocParseLab.Server.Models;

public class DocumentComment
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public ParsedDocument Document { get; set; } = null!;
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
