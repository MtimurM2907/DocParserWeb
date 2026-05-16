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
public sealed class AiController : ControllerBase
{
    private readonly IGigaChatClient _client;
    private readonly AppDbContext _db;

    public AiController(IGigaChatClient client, AppDbContext db)
    {
        _client = client;
        _db = db;
    }

    [HttpPost("rewrite")]
    [Authorize]
    [ProducesResponseType(typeof(RewriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RewriteResponse>> Rewrite([FromBody] RewriteRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new ErrorResponse { Message = "Текст для переписывания пуст." });
        }

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
                        Message = "Для переписывания в контексте сохранённого документа войдите в аккаунт или уберите documentId из запроса.",
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

            if (string.Equals(doc.DataClassification, "Confidential", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Message = "Для документов с грифом Confidential переписывание через нейросеть недоступно.",
                });
            }
        }

        var rewrite = await _client.RewriteTextAsync(
            request.Text,
            request.Mode,
            request.Tone,
            request.Length,
            cancellationToken);

        return Ok(new RewriteResponse
        {
            RewrittenText = rewrite.RewrittenText,
            ModelComment = rewrite.ModelComment
        });
    }
}
