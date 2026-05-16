namespace DocParseLab.Server.DTOs;

public sealed class RewriteRequest
{
    public string Text { get; set; } = string.Empty;
    public string Mode { get; set; } = "Более формально";
    public string? Tone { get; set; }
    public string? Length { get; set; }

    /// <summary>При указании и авторизации: для Confidential переписывание через GigaChat запрещено.</summary>
    public int? DocumentId { get; set; }
}

public sealed class RewriteResponse
{
    public string RewrittenText { get; set; } = string.Empty;
    public string ModelComment { get; set; } = string.Empty;
}

