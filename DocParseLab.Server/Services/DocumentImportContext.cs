namespace DocParseLab.Server.Services;

/// <summary>Параметры импорта документа (корпоративный контур).</summary>
public sealed class DocumentImportContext
{
    public string ProcessingProfile { get; init; } = "general";
    public string DataClassification { get; init; } = "Internal";
}
