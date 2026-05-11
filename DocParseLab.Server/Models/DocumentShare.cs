namespace DocParseLab.Server.Models;

public class DocumentShare
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public ParsedDocument Document { get; set; } = null!;

    public int FromUserId { get; set; }
    public AppUser FromUser { get; set; } = null!;

    public int ToUserId { get; set; }
    public AppUser ToUser { get; set; } = null!;

    public DateTime SharedAt { get; set; } = DateTime.UtcNow;
}

