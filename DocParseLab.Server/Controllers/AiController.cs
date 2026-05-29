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

[ApiController]
[Route("api/[controller]")]
public sealed class AiController : ControllerBase
{
    private readonly IGigaChatClient _client;
    private readonly GigaChatOptions _gigaChatOptions;
    private readonly AppDbContext _db;
    private readonly ILogger<AiController> _logger;

    public AiController(IGigaChatClient client, IOptions<GigaChatOptions> gigaChatOptions, AppDbContext db, ILogger<AiController> logger)
    {
        _client = client;
        _gigaChatOptions = gigaChatOptions.Value;
        _db = db;
        _logger = logger;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GigaChatStatusResponse), StatusCodes.Status200OK)]
    public ActionResult<GigaChatStatusResponse> GetStatus()
    {
        return Ok(new GigaChatStatusResponse
        {
            Configured = _gigaChatOptions.IsConfigured(),
        });
    }

    [HttpPost("summarize/{documentId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(DocumentSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentSummaryResponse>> SummarizeDocument(int documentId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new ErrorResponse { Message = "Требуется авторизация." });

        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (doc == null)
            return NotFound(new ErrorResponse { Message = "Документ не найден." });

        if (doc.OwnerId != null)
        {
            var allowed = doc.OwnerId == userId
                          || await _db.DocumentShares.AnyAsync(
                              s => s.DocumentId == documentId && s.ToUserId == userId,
                              cancellationToken);
            if (!allowed)
                return NotFound(new ErrorResponse { Message = "Документ не найден." });
        }

        var text = doc.EditedText ?? doc.FullText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(new ErrorResponse { Message = "В документе нет текста для описания." });

        var confidential = DocumentDataClassifications.Normalize(doc.DataClassification)
            == DocumentDataClassifications.Confidential;
        string summary;
        string source;

        if (confidential)
        {
            summary = "[local-summary]\n" + DocumentSummaryBuilder.BuildLocalSummary(text, doc.FileName);
            source = "local";
        }
        else if (_gigaChatOptions.IsConfigured())
        {
            try
            {
                var giga = await _client.GetStructuredJsonAsync(text, cancellationToken);
                summary = string.IsNullOrWhiteSpace(giga.HumanReadable)
                    ? "[local-summary]\n" + DocumentSummaryBuilder.BuildLocalSummary(text, doc.FileName)
                    : giga.HumanReadable;
                source = string.IsNullOrWhiteSpace(giga.HumanReadable) ? "local" : "gigachat";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GigaChat summary failed for document {DocumentId}, fallback to local summary", documentId);
                summary = "[local-summary]\n" + DocumentSummaryBuilder.BuildLocalSummary(text, doc.FileName)
                          + "\n\n(GigaChat временно недоступен)";
                source = "local";
            }
        }
        else
        {
            summary = "[local-summary]\n" + DocumentSummaryBuilder.BuildLocalSummary(text, doc.FileName);
            source = "local";
        }

        doc.AiSummary = summary;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentSummaryResponse
        {
            AiSummary = summary,
            Source = source,
        });
    }
}
