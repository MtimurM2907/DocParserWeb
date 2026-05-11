using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocParseLab.Server.DTOs;
using DocParseLab.Server.Services;

namespace DocParseLab.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AiController : ControllerBase
{
    private readonly IGigaChatClient _client;

    public AiController(IGigaChatClient client)
    {
        _client = client;
    }

    [HttpPost("rewrite")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RewriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RewriteResponse>> Rewrite([FromBody] RewriteRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new ErrorResponse { Message = "Текст для переписывания пуст." });
        }

        var result = await _client.RewriteTextAsync(
            request.Text,
            request.Mode,
            request.Tone,
            request.Length,
            cancellationToken);

        return Ok(new RewriteResponse
        {
            RewrittenText = result.RewrittenText,
            ModelComment = result.ModelComment
        });
    }
}

