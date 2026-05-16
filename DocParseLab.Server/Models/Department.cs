namespace DocParseLab.Server.Models;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<ParsedDocument> Documents { get; set; } = new List<ParsedDocument>();
}
