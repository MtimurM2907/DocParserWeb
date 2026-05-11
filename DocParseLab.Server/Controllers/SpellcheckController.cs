using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocParseLab.Server.DTOs;
using DocParseLab.Server.Services;

namespace DocParseLab.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class SpellcheckController : ControllerBase
{
    private readonly ISpellcheckService _spellcheck;
    private readonly ILogger<SpellcheckController> _logger;

    public SpellcheckController(ISpellcheckService spellcheck, ILogger<SpellcheckController> logger)
    {
        _spellcheck = spellcheck;
        _logger = logger;
    }

    [HttpPost("check")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SpellcheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SpellcheckResponse>> Check([FromBody] SpellcheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new ErrorResponse { Message = "Некорректное тело запроса." });
            }

            var response = await _spellcheck.CheckAsync(request, cancellationToken);
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
                Message = "Ошибка проверки орфографии",
                Details = ex.Message
            });
        }
    }
}

