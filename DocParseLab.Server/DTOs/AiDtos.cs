namespace DocParseLab.Server.DTOs;

public sealed class GigaChatStatusResponse
{
    public bool Configured { get; set; }
}

public sealed class DocumentSummaryResponse
{
    public string AiSummary { get; set; } = string.Empty;

    /// <summary>gigachat | local</summary>
    public string Source { get; set; } = "local";
}
