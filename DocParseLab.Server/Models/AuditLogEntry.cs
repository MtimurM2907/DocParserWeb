namespace DocParseLab.Server.Models;

/// <summary>Журнал действий для интеграции в контур организации (аудит).</summary>
public class AuditLogEntry
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Идентификатор пользователя или null для гостя.</summary>
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    public string? UserEmailSnapshot { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Resource { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
