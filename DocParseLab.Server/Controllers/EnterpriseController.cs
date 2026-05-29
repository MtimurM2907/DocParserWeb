using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DocParseLab.Server.Data;
using DocParseLab.Server.DTOs;
using DocParseLab.Server.Extensions;
using DocParseLab.Server.Models;
using DocParseLab.Server.Services;

namespace DocParseLab.Server.Controllers;

/// <summary>Корпоративные функции: пакетный разбор, аудит, чек-листы, извлечение сущностей, вебхуки (настройка в appsettings).</summary>
[ApiController]
[Route("api/[controller]")]
public class EnterpriseController : ControllerBase
{
    private const int MaxBatchFiles = 30;

    private readonly AppDbContext _db;
    private readonly IPdfParserService _parser;
    private readonly IChecklistService _checklist;
    private readonly IEntityExtractionService _entities;
    private readonly IWebhookService _webhook;
    private readonly IOptions<EnterpriseOptions> _enterpriseOptions;
    private readonly IAuditService _audit;
    private readonly IFileScanService _fileScan;
    private readonly ILogger<EnterpriseController> _logger;

    public EnterpriseController(
        AppDbContext db,
        IPdfParserService parser,
        IChecklistService checklist,
        IEntityExtractionService entities,
        IWebhookService webhook,
        IOptions<EnterpriseOptions> enterpriseOptions,
        IAuditService audit,
        IFileScanService fileScan,
        ILogger<EnterpriseController> logger)
    {
        _db = db;
        _parser = parser;
        _checklist = checklist;
        _entities = entities;
        _webhook = webhook;
        _enterpriseOptions = enterpriseOptions;
        _audit = audit;
        _fileScan = fileScan;
        _logger = logger;
    }

    /// <summary>Пакетная загрузка PDF/DOCX (до 30 файлов за запрос).</summary>
    [HttpPost("parse-batch")]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    [ProducesResponseType(typeof(BatchParseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BatchParseResponse>> ParseBatch(
        [FromForm] List<IFormFile> files,
        [FromForm] string? processingProfile,
        [FromForm] string? dataClassification,
        CancellationToken cancellationToken)
    {
        var enterpriseOpts = _enterpriseOptions.Value;
        var providedBatchKey = Request.Headers["X-Enterprise-Batch-Key"].FirstOrDefault();
        var hasValidBatchKey = !string.IsNullOrWhiteSpace(enterpriseOpts.BatchApiKey)
            && FixedTimeStringEquals(enterpriseOpts.BatchApiKey, providedBatchKey);

        if (!string.IsNullOrWhiteSpace(enterpriseOpts.BatchApiKey) && !hasValidBatchKey)
        {
            return Unauthorized(new ErrorResponse
            {
                Message = "Неверный или отсутствует заголовок X-Enterprise-Batch-Key.",
            });
        }

        if (!hasValidBatchKey && !User.TryGetUserId(out _))
        {
            return Unauthorized(new ErrorResponse { Message = "Требуется вход в систему или ключ пакетной загрузки." });
        }

        if (files == null || files.Count == 0)
            return BadRequest(new ErrorResponse { Message = "Не переданы файлы." });
        if (files.Count > MaxBatchFiles)
            return BadRequest(new ErrorResponse { Message = $"Не более {MaxBatchFiles} файлов за один запрос." });

        int? ownerId = User.TryGetUserId(out var uid) ? uid : null;
        var ctx = new DocumentImportContext
        {
            ProcessingProfile = string.IsNullOrWhiteSpace(processingProfile) ? "general" : processingProfile!,
            DataClassification = DocumentDataClassifications.Normalize(dataClassification)
        };

        var resp = new BatchParseResponse();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            var ext = Path.GetExtension(file.FileName);
            if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase))
            {
                resp.Errors.Add($"{file.FileName}: ожидается PDF или DOCX.");
                continue;
            }

            try
            {
                _fileScan.ValidateUpload(file);
                var entity = await _parser.ParseAndSaveAsync(file, ownerId, ctx, cancellationToken);
                var ownerEmail = ownerId.HasValue
                    ? await _db.Users.Where(u => u.Id == ownerId.Value).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken)
                    : null;
                resp.Documents.Add(ParsedDocumentResponse.FromEntity(entity, ownerEmail));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка пакетного разбора {Name}", file.FileName);
                resp.Errors.Add($"{file.FileName}: не удалось обработать файл.");
            }
        }

        await _webhook.NotifyAsync("batch.completed", new
        {
            count = resp.Documents.Count,
            errors = resp.Errors.Count,
            profile = ctx.ProcessingProfile
        }, cancellationToken);

        await _audit.LogAsync(
            "enterprise.batch",
            null,
            $"documents={resp.Documents.Count},errors={resp.Errors.Count}",
            cancellationToken);

        return Ok(resp);
    }

    [HttpGet("audit")]
    [Authorize]
    [ProducesResponseType(typeof(List<AuditLogEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<AuditLogEntryResponse>>> GetMyAudit(
        CancellationToken cancellationToken,
        [FromQuery] int take = 100,
        [FromQuery] bool all = false)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        if (all && !User.IsAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Message = "Просмотр журнала всех пользователей доступен только администратору.",
            });
        }

        take = Math.Clamp(take, 1, 500);
        var q = _db.AuditLogEntries.AsQueryable();
        if (!all)
            q = q.Where(a => a.UserId == userId);

        var list = await q
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new AuditLogEntryResponse
            {
                Id = a.Id,
                CreatedAt = a.CreatedAt,
                UserId = a.UserId,
                UserEmailSnapshot = a.UserEmailSnapshot,
                Action = a.Action,
                Resource = a.Resource,
                Details = a.Details,
                IpAddress = a.IpAddress
            })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    [HttpPost("documents/{id:int}/checklist")]
    [Authorize]
    [ProducesResponseType(typeof(ChecklistValidateResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChecklistValidateResponse>> ValidateChecklist(
        int id,
        [FromQuery] string checklistId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checklistId))
            return BadRequest(new ErrorResponse { Message = "Укажите checklistId." });

        if (!await CanAccessDocumentAsync(id, cancellationToken))
            return Forbid();

        var doc = await _db.ParsedDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();

        var text = doc.EditedText ?? doc.FullText;
        var r = _checklist.Validate(text, checklistId);
        await _audit.LogAsync(
            "enterprise.checklist",
            $"document:{id}",
            $"{checklistId},ok={r.Ok}",
            cancellationToken);
        return Ok(new ChecklistValidateResponse
        {
            ChecklistId = r.ChecklistId,
            Ok = r.Ok,
            Missing = r.Missing
        });
    }

    [HttpGet("documents/{id:int}/entities")]
    [Authorize]
    [ProducesResponseType(typeof(ExtractedEntitiesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExtractedEntitiesResponse>> GetEntities(int id, CancellationToken cancellationToken)
    {
        if (!await CanAccessDocumentAsync(id, cancellationToken))
            return Forbid();

        var doc = await _db.ParsedDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();

        var text = doc.EditedText ?? doc.FullText;
        var e = _entities.Extract(text);
        await _audit.LogAsync("enterprise.entities", $"document:{id}", null, cancellationToken);
        return Ok(new ExtractedEntitiesResponse
        {
            Dates = e.Dates,
            Money = e.Money,
            Inn = e.Inn,
            Emails = e.Emails
        });
    }

    private static bool FixedTimeStringEquals(string expected, string? actual)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual ?? string.Empty);
        if (a.Length != b.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private async Task<bool> CanAccessDocumentAsync(int documentId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return false;

        var ok = await _db.ParsedDocuments.AnyAsync(
            d => d.Id == documentId && (d.OwnerId == userId || d.Shares.Any(s => s.ToUserId == userId)),
            cancellationToken);
        return ok;
    }
}
