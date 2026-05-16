namespace DocParseLab.Server.Models;

public class AppUser
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string Role { get; set; } = UserRoles.Employee;

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ParsedDocument> Documents { get; set; } = new List<ParsedDocument>();

    public ICollection<DocumentShare> SentShares { get; set; } = new List<DocumentShare>();

    public ICollection<DocumentShare> ReceivedShares { get; set; } = new List<DocumentShare>();
}
