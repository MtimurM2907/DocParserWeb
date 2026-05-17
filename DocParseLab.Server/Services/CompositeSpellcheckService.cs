using DocParseLab.Server.DTOs;
using Microsoft.Extensions.Options;

namespace DocParseLab.Server.Services;

/// <summary>Орфография через GigaChat, при отсутствии ключей — Hunspell.</summary>
public sealed class CompositeSpellcheckService : ISpellcheckService
{
    private readonly GigaChatOptions _options;
    private readonly AiSpellcheckService _ai;
    private readonly HunspellSpellcheckService _hunspell;
    private readonly ILogger<CompositeSpellcheckService> _logger;

    public CompositeSpellcheckService(
        IOptions<GigaChatOptions> options,
        AiSpellcheckService ai,
        HunspellSpellcheckService hunspell,
        ILogger<CompositeSpellcheckService> logger)
    {
        _options = options.Value;
        _ai = ai;
        _hunspell = hunspell;
        _logger = logger;
    }

    public async Task<SpellcheckResponse> CheckAsync(SpellcheckRequest request, CancellationToken cancellationToken = default)
    {
        if (_options.IsConfigured())
        {
            try
            {
                var response = await _ai.CheckAsync(request, cancellationToken);
                response.SpellcheckEngine = "gigachat";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GigaChat недоступен, переключение на Hunspell");
            }
        }

        var local = await _hunspell.CheckAsync(request, cancellationToken);
        local.SpellcheckEngine = "hunspell";
        return local;
    }
}
