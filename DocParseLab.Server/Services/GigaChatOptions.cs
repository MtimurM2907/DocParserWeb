namespace DocParseLab.Server.Services;

public class GigaChatOptions
{
    public const string SectionName = "GigaChat";

    public string AuthUrl { get; set; } = string.Empty;

    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    public string Authorization { get; set; } = string.Empty;

    public string Scope { get; set; } = "GIGACHAT_API_PERS";

    public string Model { get; set; } = "GigaChat";
}


