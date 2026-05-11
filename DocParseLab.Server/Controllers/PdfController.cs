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

    public PdfController(
        AppDbContext db,
        IPdfParserService parserService,
        IDocumentExportService exportService,
        IEmailSender emailSender,
        ILogger<PdfController> logger)
    {
        _db = db;
        _parserService = parserService;
        _exportService = exportService;
        _emailSender = emailSender;
        _logger = logger;
    }

    /// <summary>
    /// Парсинг PDF или DOCX файла
    /// </summary>
    /// <param name="file">PDF или DOCX файл для парсинга</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Распарсенный документ</returns>
    [HttpPost("parse")]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ParsedDocumentResponse>> Parse(IFormFile file, CancellationToken cancellationToken)
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
            int? ownerId = User.TryGetUserId(out var ownerUserId) ? ownerUserId : null;

            _logger.LogInformation("Начало парсинга файла {FileName}, размер {Size} байт", 
                file.FileName, file.Length);

            var result = await _parserService.ParseAndSaveAsync(file, ownerId, cancellationToken);
            
            _logger.LogInformation("Файл {FileName} успешно распарсен, ID документа {DocumentId}", 
                file.FileName, result.Id);

            var ownerEmail = ownerId.HasValue 
                ? await _db.Users.Where(u => u.Id == ownerId.Value)
                    .Select(u => u.Email).FirstOrDefaultAsync(cancellationToken) 
                : null;

            return Ok(ParsedDocumentResponse.FromEntity(result, ownerEmail));
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

        var document = await _db.ParsedDocuments
            .Include(d => d.Owner)
            .Include(d => d.Shares)
                .ThenInclude(s => s.ToUser)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
        {
            return NotFound("Документ не найден.");
        }

        // Проверка прав доступа
        bool hasAccess = document.OwnerId == currentUserId || 
            document.Shares.Any(s => s.ToUserId == currentUserId);

        if (!hasAccess)
        {
            return Forbid("У вас нет доступа к этому документу.");
        }

        var ownerEmail = document.Owner?.Email;
        var shareCount = document.Shares.Count;

        return Ok(ParsedDocumentResponse.FromEntity(document, ownerEmail, shareCount));
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

        var document = await _db.ParsedDocuments
            .Include(d => d.Owner)
            .Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
        {
            return NotFound(new ErrorResponse { Message = "Документ не найден." });
        }

        bool hasAccess = document.OwnerId == currentUserId ||
                         document.Shares.Any(s => s.ToUserId == currentUserId);

        if (!hasAccess)
        {
            return Forbid("У вас нет доступа к этому документу.");
        }

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
        }

        await _db.SaveChangesAsync(cancellationToken);

        var ownerEmail = document.Owner?.Email;
        var shareCount = document.Shares.Count;
        return Ok(ParsedDocumentResponse.FromEntity(document, ownerEmail, shareCount));
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

        if (document.OwnerId != currentUserId)
        {
            return Forbid("Удалять документ может только владелец.");
        }

        _db.ParsedDocuments.Remove(document);
        await _db.SaveChangesAsync(cancellationToken);
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
            return File(bytes, "application/pdf", $"{safeName}.pdf");
        }

        var docx = _exportService.ExportToDocx(safeName, text);
        return File(docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{safeName}.docx");
    }
}
