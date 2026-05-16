using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using DocParseLab.Server.Data;
using DocParseLab.Server.DTOs;
using DocParseLab.Server.Extensions;
using DocParseLab.Server.Models;
using DocParseLab.Server.Services;

namespace DocParseLab.Server.Controllers;

/// <summary>
/// Контроллер для управления PDF документами
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PdfController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPdfParserService _parserService;
    private readonly IDocumentExportService _exportService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<PdfController> _logger;
    private readonly IAuditService _audit;
    private readonly IWebhookService _webhook;
    private readonly IDocumentAccessService _access;
    private readonly IDocumentVersionService _versions;
    private readonly IDocumentSignatureService _signatures;

    public PdfController(
        AppDbContext db,
        IPdfParserService parserService,
        IDocumentExportService exportService,
        IEmailSender emailSender,
        ILogger<PdfController> logger,
        IAuditService audit,
        IWebhookService webhook,
        IDocumentAccessService access,
        IDocumentVersionService versions,
        IDocumentSignatureService signatures)
    {
        _db = db;
        _parserService = parserService;
        _exportService = exportService;
        _emailSender = emailSender;
        _logger = logger;
        _audit = audit;
        _webhook = webhook;
        _access = access;
        _versions = versions;
        _signatures = signatures;
    }

    /// <summary>
    /// Парсинг PDF или DOCX файла
    /// </summary>
    /// <param name="file">PDF или DOCX файл для парсинга</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Распарсенный документ</returns>
    [HttpPost("parse")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ParsedDocumentResponse>> Parse(
        IFormFile file,
        [FromQuery] string? processingProfile,
        [FromQuery] string? dataClassification,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Некорректные данные запроса.");
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("Файл не передан.");
        }

        var ext = Path.GetExtension(file.FileName);
        var isSupported = string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase);
        if (!isSupported)
        {
            return BadRequest("Ожидается PDF или DOCX-файл.");
        }

        try
        {
            if (!User.TryGetUserId(out var ownerUserId))
                return Unauthorized();
            var ownerId = ownerUserId;

            _logger.LogInformation("Начало парсинга файла {FileName}, размер {Size} байт", 
                file.FileName, file.Length);

            var result = await _parserService.ParseAndSaveAsync(
                file,
                ownerId,
                new DocumentImportContext
                {
                    ProcessingProfile = string.IsNullOrWhiteSpace(processingProfile) ? "general" : processingProfile!,
                    DataClassification = string.IsNullOrWhiteSpace(dataClassification) ? "Internal" : dataClassification!
                },
                cancellationToken);
            
            _logger.LogInformation("Файл {FileName} успешно распарсен, ID документа {DocumentId}", 
                file.FileName, result.Id);

            var ownerEmail = await _db.Users.Where(u => u.Id == ownerId)
                .Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

            await _audit.LogAsync("document.parse", $"document:{result.Id}", file.FileName, cancellationToken);

            return Ok(await MapDocumentResponseAsync(result, ownerUserId, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при парсинге PDF файла {FileName}", file.FileName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Message = "Ошибка при обработке файла",
                Details = ex.Message
            });
        }
    }

    /// <summary>
    /// Получение списка документов текущего пользователя
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Список документов</returns>
    [HttpGet("my")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<DocumentBriefResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<DocumentBriefResponse>>> GetMyDocuments(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var docs = await _db.ParsedDocuments
            .Where(d => d.OwnerId == userId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => DocumentBriefResponse.FromEntity(d))
            .ToListAsync(cancellationToken);

        return Ok(docs);
    }

    /// <summary>
    /// Предоставление доступа к документу другому пользователю
    /// </summary>
    /// <param name="request">Запрос на предоставление доступа</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат операции</returns>
    [HttpPost("share")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ShareDocument([FromBody] ShareDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Некорректные данные запроса.");
        }

        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        var document = await _db.ParsedDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken);
        if (document == null)
        {
            return NotFound("Документ не найден.");
        }

        if (document.OwnerId != currentUserId)
        {
            _logger.LogWarning("Пользователь {UserId} попытался поделиться чужим документом {DocumentId}", 
                currentUserId, document.Id);
            return Forbid("Вы можете делиться только своими документами.");
        }

        var targetEmail = request.TargetEmail.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(targetEmail))
        {
            return BadRequest("Укажите email получателя.");
        }

        var targetUser = await _db.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == targetEmail, cancellationToken);
        if (targetUser == null)
        {
            return NotFound("Пользователь-получатель не найден.");
        }

        if (targetUser.Id == currentUserId)
        {
            return BadRequest("Нельзя отправить документ самому себе.");
        }

        // Проверка на дублирование шары
        var existingShare = await _db.DocumentShares
            .AnyAsync(s => s.DocumentId == document.Id && s.ToUserId == targetUser.Id, cancellationToken);
        if (existingShare)
        {
            return BadRequest("Доступ к этому документу уже предоставлен данному пользователю.");
        }

        var share = new DocumentShare
        {
            DocumentId = document.Id,
            FromUserId = currentUserId,
            ToUserId = targetUser.Id,
            SharedAt = DateTime.UtcNow
        };

        _db.DocumentShares.Add(share);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync("document.share", $"document:{document.Id}", targetEmail, cancellationToken);
        await _webhook.NotifyAsync(
            "document.shared",
            new { documentId = document.Id, toUserId = targetUser.Id, targetEmail },
            cancellationToken);

        _logger.LogInformation("Пользователь {FromUserId} предоставил доступ к документу {DocumentId} пользователю {ToUserId}", 
            currentUserId, document.Id, targetUser.Id);

        return Ok();
    }

    [HttpPost("{id:int}/send-email")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SendDocumentByEmail(int id, [FromBody] SendDocumentEmailRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.TargetEmail))
        {
            return BadRequest("Укажите email получателя.");
        }

        var format = string.Equals(request.Format, "pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "docx";
        var targetEmail = request.TargetEmail.Trim();
        try
        {
            _ = new MailAddress(targetEmail);
        }
        catch
        {
            return BadRequest("Некорректный email получателя.");
        }

        var document = await _db.ParsedDocuments
            .Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document == null)
        {
            return NotFound("Документ не найден.");
        }

        var hasAccess = document.OwnerId == currentUserId || document.Shares.Any(s => s.ToUserId == currentUserId);
        if (!hasAccess)
        {
            return Forbid("У вас нет доступа к этому документу.");
        }

        var text = document.EditedText ?? document.FullText;
        var safeName = Path.GetFileNameWithoutExtension(document.FileName);
        byte[] bytes;
        string contentType;
        string fileName;

        if (format == "pdf")
        {
            bytes = _exportService.ExportToPdf(safeName, text);
            contentType = "application/pdf";
            fileName = $"{safeName}.pdf";
        }
        else
        {
            bytes = _exportService.ExportToDocx(safeName, text);
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            fileName = $"{safeName}.docx";
        }

        try
        {
            await _emailSender.SendDocumentAsync(
                targetEmail,
                $"DocParseLab: {safeName}",
                "Во вложении документ из DocParseLab.",
                bytes,
                fileName,
                contentType,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP ошибка при отправке документа {DocumentId} на {Email}", id, targetEmail);
            return StatusCode(StatusCodes.Status502BadGateway, "SMTP ошибка. Проверьте Host/Port/SSL/логин/пароль.");
        }

        await _audit.LogAsync("document.email_send", $"document:{id}", $"{format}:{targetEmail}", cancellationToken);
        return Ok();
    }

    /// <summary>
    /// Получение документа по ID
    /// </summary>
    /// <param name="id">ID документа</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Информация о документе</returns>
    [HttpGet("{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ParsedDocumentResponse>> GetDocument(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        var document = await LoadDocumentFullAsync(id, cancellationToken);

        if (document == null)
            return NotFound("Документ не найден.");

        if (!await _access.CanReadAsync(currentUserId, document, cancellationToken))
            return Forbid("У вас нет доступа к этому документу.");

        return Ok(await MapDocumentResponseAsync(document, currentUserId, cancellationToken));
    }

    /// <summary>
    /// Сохранение отредактированного текста документа
    /// </summary>
    [HttpPut("{id:int}/text")]
    [Authorize]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ParsedDocumentResponse>> UpdateDocumentText(int id, [FromBody] UpdateDocumentTextRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(new ErrorResponse { Message = "Некорректное тело запроса." });
        }

        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        var document = await LoadDocumentFullAsync(id, cancellationToken);

        if (document == null)
            return NotFound(new ErrorResponse { Message = "Документ не найден." });

        if (!await _access.CanEditContentAsync(currentUserId, document, cancellationToken))
            return Forbid("Редактирование недоступно: нет прав или документ на согласовании.");

        var newText = request.Text ?? string.Empty;
        if (newText.Length > 2_000_000)
        {
            return BadRequest(new ErrorResponse { Message = "Слишком большой текст. Максимум: 2000000 символов." });
        }

        // Если пользователь очистил поле — считаем это сбросом правок
        if (string.IsNullOrWhiteSpace(newText))
        {
            document.EditedText = null;
            document.EditedAt = null;
        }
        else
        {
            document.EditedText = newText;
            document.EditedAt = DateTime.UtcNow;
            await _versions.SaveVersionAsync(document, currentUserId, newText, "edit", cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync("document.text_update", $"document:{id}", $"len={newText.Length}", cancellationToken);
        return Ok(await MapDocumentResponseAsync(document, currentUserId, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteDocument(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        var document = await _db.ParsedDocuments
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document == null)
        {
            return NotFound("Документ не найден.");
        }

        if (!await _access.CanDeleteAsync(currentUserId, document, cancellationToken))
            return Forbid("Удаление недоступно.");

        _db.ParsedDocuments.Remove(document);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("document.delete", $"document:{id}", document.FileName, cancellationToken);
        await _webhook.NotifyAsync("document.deleted", new { documentId = id, fileName = document.FileName }, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:int}/export")]
    [Authorize]
    public async Task<IActionResult> ExportDocument(int id, [FromQuery] string format = "docx", CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        var document = await _db.ParsedDocuments
            .Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document == null)
        {
            return NotFound("Документ не найден.");
        }

        var hasAccess = document.OwnerId == currentUserId || document.Shares.Any(s => s.ToUserId == currentUserId);
        if (!hasAccess)
        {
            return Forbid("У вас нет доступа к этому документу.");
        }

        var text = document.EditedText ?? document.FullText;
        var safeName = Path.GetFileNameWithoutExtension(document.FileName);

        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = _exportService.ExportToPdf(safeName, text);
            await _audit.LogAsync("document.export", $"document:{id}", "pdf", cancellationToken);
            await _webhook.NotifyAsync("document.exported", new { documentId = id, format = "pdf" }, cancellationToken);
            return File(bytes, "application/pdf", $"{safeName}.pdf");
        }

        var docx = _exportService.ExportToDocx(safeName, text);
        await _audit.LogAsync("document.export", $"document:{id}", "docx", cancellationToken);
        await _webhook.NotifyAsync("document.exported", new { documentId = id, format = "docx" }, cancellationToken);
        return File(docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{safeName}.docx");
    }

    private async Task<ParsedDocument?> LoadDocumentFullAsync(int id, CancellationToken cancellationToken) =>
        await _db.ParsedDocuments
            .Include(d => d.Owner)
            .Include(d => d.Department)
            .Include(d => d.ResponsibleUser)
            .Include(d => d.CurrentApprover)
            .Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    private async Task<ParsedDocumentResponse> MapDocumentResponseAsync(
        ParsedDocument document,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        var verify = await _signatures.VerifyAsync(document, cancellationToken);
        var flags = await _signatures.GetUiFlagsAsync(currentUserId, document, cancellationToken);
        return ParsedDocumentResponse.FromEntity(
            document,
            document.Owner?.Email,
            document.Shares.Count,
            currentUserId,
            document.Department?.Name,
            document.ResponsibleUser?.Email,
            document.CurrentApprover?.Email,
            flags.CanSign,
            flags.SignatureCount,
            verify.TextMatchesLastSignature,
            verify.LastSignedAt,
            verify.LastSignerEmail);
    }
}
