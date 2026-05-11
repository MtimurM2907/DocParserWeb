using DocParseLab.Server.Data;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Services;

public class PdfParserService : IPdfParserService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PdfParserService> _logger;
    private readonly IGigaChatClient _gigaChatClient;
    private readonly IReadOnlyList<IDocumentTextExtractor> _extractors;

    public PdfParserService(
        AppDbContext db,
        ILogger<PdfParserService> logger,
        IGigaChatClient gigaChatClient,
        IEnumerable<IDocumentTextExtractor> extractors)
    {
        _db = db;
        _logger = logger;
        _gigaChatClient = gigaChatClient;
        _extractors = extractors.ToList();
    }

    public async Task<ParsedDocument> ParseAndSaveAsync(IFormFile file, int? ownerId = null, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            throw new InvalidOperationException("Пустой файл.");
        }

        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(extension));
        if (extractor == null)
        {
            throw new InvalidOperationException($"Формат файла {extension} не поддерживается.");
        }

        string fullText;
        try
        {
            fullText = await extractor.ExtractTextAsync(memoryStream, cancellationToken);
            fullText = TextStructureFormatter.NormalizeForStorage(fullText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при извлечении текста из {FileName}", file.FileName);
            fullText = string.Empty;
        }

        string structuredJson;
        string aiSummary;
        try
        {
            var gigaResult = await _gigaChatClient.GetStructuredJsonAsync(fullText, cancellationToken);
            structuredJson = gigaResult.StructuredJson;
            aiSummary = string.IsNullOrWhiteSpace(gigaResult.HumanReadable)
                ? BuildFallbackSummary(fullText)
                : gigaResult.HumanReadable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обращении к GigaChat. Используется резервная структура JSON.");

            var fallback = new
            {
                fileName = file.FileName,
                textPreview = fullText.Length > 2000 ? fullText[..2000] : fullText,
                error = "GigaChat call failed, fallback structure is used."
            };

            structuredJson = System.Text.Json.JsonSerializer.Serialize(fallback,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            aiSummary = "GigaChat недоступен, показана резервная структура по страницам без AI-описания.";
        }

        var entity = new ParsedDocument
        {
            FileName = file.FileName,
            OriginalFileType = extension.Trim('.'),
            OwnerId = ownerId,
            FullText = fullText,
            StructuredJson = structuredJson,
            AiSummary = aiSummary,
            UploadedAt = DateTime.UtcNow
        };

        _db.ParsedDocuments.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return entity;
    }

    private static string BuildFallbackSummary(string fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return "Описание не получено и текст документа пуст или не распознан.";
        }

        var maxLength = 600;
        var normalized = fullText.Replace("\r\n", " ").Replace("\n", " ");

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var cut = normalized[..maxLength];
        var lastDot = cut.LastIndexOfAny(new[] { '.', '!', '?' });
        if (lastDot > 100)
        {
            cut = cut[..(lastDot + 1)];
        }

        return cut + " …";
    }
}


