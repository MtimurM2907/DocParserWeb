using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Data;
using DocParseLab.Server.DTOs;
using DocParseLab.Server.Extensions;
using DocParseLab.Server.Services;

namespace DocParseLab.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class SpellcheckController : ControllerBase
{
    private readonly HunspellSpellcheckService _hunspell;
    private readonly AppDbContext _db;
    private readonly ILogger<SpellcheckController> _logger;

    public SpellcheckController(
        HunspellSpellcheckService hunspell,
        AppDbContext db,
        ILogger<SpellcheckController> logger)
    {
        _hunspell = hunspell;
        _db = db;
        _logger = logger;
    }

    [HttpPost("check")]
    [Authorize]
    [ProducesResponseType(typeof(SpellcheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SpellcheckResponse>> Check([FromBody] SpellcheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new ErrorResponse { Message = "Некорректное тело запроса." });
            }

            ISpellcheckService engine = _hunspell;
            var engineName = "hunspell";

            if (request.DocumentId is int docId)
            {
                var doc = await _db.ParsedDocuments.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == docId, cancellationToken);
                if (doc == null)
                {
                    return NotFound(new ErrorResponse { Message = "Документ не найден." });
                }

                if (doc.OwnerId != null)
                {
                    if (!User.TryGetUserId(out var userId))
                    {
                        return Unauthorized(new ErrorResponse
                        {
                            Message = "Для проверки в контексте сохранённого документа войдите в аккаунт или уберите documentId из запроса.",
                        });
                    }

                    var allowed = doc.OwnerId == userId
                                  || await _db.DocumentShares.AnyAsync(
                                      s => s.DocumentId == docId && s.ToUserId == userId,
                                      cancellationToken);
                    if (!allowed)
                    {
                        return NotFound(new ErrorResponse { Message = "Документ не найден." });
                    }
                }

            }

            var response = await engine.CheckAsync(request, cancellationToken);
            if (string.IsNullOrEmpty(response.SpellcheckEngine))
            {
                response.SpellcheckEngine = engineName;
            }

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка проверки орфографии");
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Message = "Ошибка проверки орфографии"
            });
        }
    }
}
