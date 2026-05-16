namespace DocParseLab.Server.Models;

/// <summary>Запись о цифровой (внутренней) подписи версии текста документа.</summary>
public class DocumentSignature
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public ParsedDocument Document { get; set; } = null!;

    public int SignedByUserId { get; set; }
    public AppUser SignedByUser { get; set; } = null!;

    /// <summary>SHA-256 (hex) канонического текста на момент подписания.</summary>
    public string TextHashSha256 { get; set; } = string.Empty;

    public DateTime SignedAt { get; set; } = DateTime.UtcNow;

    public string SignerEmailSnapshot { get; set; } = string.Empty;
    public string? SignerDisplayNameSnapshot { get; set; }
    public string SignerRoleSnapshot { get; set; } = string.Empty;

    public string? Comment { get; set; }

    /// <summary>internal — подпись в системе с фиксацией хеша; external — зарезервировано под УКЭП.</summary>
    public string SignatureKind { get; set; } = SignatureKinds.Internal;
}

public static class SignatureKinds
{
    public const string Internal = "internal";
    public const string External = "external";
}
