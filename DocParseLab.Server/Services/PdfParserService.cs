using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DocParseLab.Server.Data;
using DocParseLab.Server.Hubs;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Services;

public class PdfParserService : IPdfParserService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PdfParserService> _logger;
    private readonly IGigaChatClient _gigaChatClient;
    private readonly GigaChatOptions _gigaChatOptions;
    private readonly IReadOnlyList<IDocumentTextExtractor> _extractors;
    private readonly IWebhookService _webhook;
    private readonly IDocumentFileStorageService _fileStorage;
    private readonly IDocumentRealtimeService _realtime;
    private readonly RussianHunspellDictionary _hunspell;
    private readonly OcrOptions _ocrOptions;

    public PdfParserService(
        AppDbContext db,
        ILogger<PdfParserService> logger,
        IGigaChatClient gigaChatClient,
        IOptions<GigaChatOptions> gigaChatOptions,
        IEnumerable<IDocumentTextExtractor> extractors,
        IWebhookService webhook,
        IDocumentFileStorageService fileStorage,
        IDocumentRealtimeService realtime,
        RussianHunspellDictionary hunspell,
        IOptions<OcrOptions> ocrOptions)
    {
        _db = db;
        _logger = logger;
        _gigaChatClient = gigaChatClient;
        _gigaChatOptions = gigaChatOptions.Value;
        _extractors = extractors.ToList();
        _webhook = webhook;
        _fileStorage = fileStorage;
        _realtime = realtime;
        _hunspell = hunspell;
        _ocrOptions = ocrOptions.Value;
    }

    public async Task<ParsedDocument> ParseAndSaveAsync(
        IFormFile file,
        int? ownerId = null,
        DocumentImportContext? import = null,
        CancellationToken cancellationToken = default)
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
        using var parseProgress = DocumentParseProgressScope.TryBegin(ownerId, _realtime);
        string? rawExtractedText = null;
        try
        {
            fullText = await extractor.ExtractTextAsync(memoryStream, cancellationToken);
            fullText = TextStructureFormatter.NormalizeForStorage(fullText);
            rawExtractedText = fullText;
            fullText = PdfTextPostProcessor.NormalizeExtractedText(fullText);
            if (_ocrOptions.ApplyHunspellCorrection)
            {
                fullText = RussianOcrSpellCorrector.CorrectDocument(fullText, _hunspell.WordList);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при извлечении текста из {FileName}", file.FileName);
            fullText = string.Empty;
        }

        import ??= new DocumentImportContext();
        var confidential = string.Equals(import.DataClassification, "Confidential", StringComparison.OrdinalIgnoreCase);

        string structuredJson;
        string aiSummary;
        if (confidential)
        {
            structuredJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                fileName = file.FileName,
                dataClassification = import.DataClassification,
                note = "Внешние LLM отключены политикой конфиденциальности."
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            aiSummary = "Документ с грифом конфиденциальности: AI-описание и внешние сервисы не использовались.";
        }
        else if (!_gigaChatOptions.IsConfigured())
        {
            _logger.LogInformation(
                "GigaChat не настроен — формируется локальное описание документа (см. gigachat.secrets.json.example).");
            structuredJson = BuildGigaChatFallbackJson(file.FileName, fullText, "GigaChat not configured");
            aiSummary = "[local-summary]\n" + DocumentSummaryBuilder.BuildLocalSummary(fullText, file.FileName);
        }
        else
        {
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
                structuredJson = BuildGigaChatFallbackJson(file.FileName, fullText, ex.Message);
                aiSummary = "GigaChat недоступен, показана резервная структура по страницам без AI-описания.";
            }
        }

        var entity = new ParsedDocument
        {
            FileName = file.FileName,
            OriginalFileType = extension.Trim('.'),
            OwnerId = ownerId,
            FullText = fullText,
            RawExtractedText = rawExtractedText,
            StructuredJson = structuredJson,
            AiSummary = aiSummary,
            UploadedAt = DateTime.UtcNow,
            ProcessingProfile = string.IsNullOrWhiteSpace(import.ProcessingProfile) ? "general" : import.ProcessingProfile.Trim(),
            DataClassification = string.IsNullOrWhiteSpace(import.DataClassification) ? "Internal" : import.DataClassification.Trim()
        };

        if (ownerId.HasValue)
        {
            var owner = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ownerId.Value, cancellationToken);
            if (owner != null)
            {
                entity.DepartmentId = owner.DepartmentId;
                entity.ResponsibleUserId = ownerId;
            }
        }

        _db.ParsedDocuments.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        if (ownerId.HasValue)
        {
            try
            {
                memoryStream.Position = 0;
                entity.OriginalStorageKey = await _fileStorage.SaveOriginalAsync(
                    entity.Id, file.FileName, memoryStream, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось сохранить оригинал файла {FileName}", file.FileName);
            }
        }

        if (ownerId.HasValue && !string.IsNullOrWhiteSpace(entity.FullText))
        {
            _db.DocumentVersions.Add(new Models.DocumentVersion
            {
                DocumentId = entity.Id,
                VersionNumber = 1,
                Text = entity.FullText,
                ChangeType = "import",
                CreatedByUserId = ownerId,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _webhook.NotifyAsync("document.parsed", new
        {
            documentId = entity.Id,
            fileName = entity.FileName,
            profile = entity.ProcessingProfile,
            classification = entity.DataClassification
        }, cancellationToken);

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

    private static string BuildGigaChatFallbackJson(string fileName, string fullText, string error) =>
        System.Text.Json.JsonSerializer.Serialize(
            new
            {
                fileName,
                textPreview = fullText.Length > 2000 ? fullText[..2000] : fullText,
                error,
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}


