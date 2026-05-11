using DocParseLab.Server.DTOs;

namespace DocParseLab.Server.Services;

/// <summary>Проверка орфографии через GigaChat (нейросеть).</summary>
public sealed class AiSpellcheckService : ISpellcheckService
{
    private const int ChunkSize = 6500;

    private readonly IGigaChatClient _gigaChat;
    private readonly ILogger<AiSpellcheckService> _logger;

    public AiSpellcheckService(IGigaChatClient gigaChat, ILogger<AiSpellcheckService> logger)
    {
        _gigaChat = gigaChat;
        _logger = logger;
    }

    public async Task<SpellcheckResponse> CheckAsync(SpellcheckRequest request, CancellationToken cancellationToken = default)
    {
        var language = string.IsNullOrWhiteSpace(request.Language) ? "ru_RU" : request.Language.Trim();
        if (!string.Equals(language, "ru_RU", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Сейчас поддерживается только язык ru_RU.");
        }

        var text = NormalizeNewlines(request.Text ?? string.Empty);
        var maxSuggestions = request.MaxSuggestions <= 0 ? 5 : Math.Min(request.MaxSuggestions, 20);
        var maxMistakes = request.MaxMistakes <= 0 ? 200 : Math.Min(request.MaxMistakes, 1000);

        var result = new SpellcheckResponse
        {
            Language = "ru_RU",
            TextLength = text.Length
        };

        if (text.Length == 0)
        {
            return result;
        }

        if (text.Length > 500_000)
        {
            throw new InvalidOperationException("Слишком большой текст для проверки. Максимум: 500000 символов.");
        }

        var mistakes = new List<SpellcheckMistake>();
        var offset = 0;

        while (offset < text.Length && mistakes.Count < maxMistakes)
        {
            var len = Math.Min(ChunkSize, text.Length - offset);
            var segment = text.Substring(offset, len);
            var remainingBudget = maxMistakes - mistakes.Count;

            IReadOnlyList<SpellcheckMistakeDto> chunkMistakes;
            try
            {
                chunkMistakes = await _gigaChat.SpellcheckSegmentAsync(
                    segment,
                    maxSuggestions,
                    remainingBudget,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка нейросетевой проверки орфографии (offset {Offset})", offset);
                throw new InvalidOperationException(
                    "Не удалось выполнить проверку через GigaChat. Проверьте ключи API, сертификат и доступ к сети.",
                    ex);
            }

            foreach (var dto in chunkMistakes)
            {
                if (mistakes.Count >= maxMistakes)
                {
                    break;
                }

                var globalStart = offset + dto.Start;
                if (globalStart < 0 || dto.Length <= 0 || globalStart + dto.Length > text.Length)
                {
                    continue;
                }

                var suggestions = dto.Suggestions.ToList();
                var slice = text.AsSpan(globalStart, dto.Length).ToString();
                if (RussianSpellcheckHomoglyphs.TryContextualLatinOcrReplacement(slice, text, globalStart)
                    is { } ocrFix
                    && !suggestions.Any(s => string.Equals(s, ocrFix, StringComparison.Ordinal)))
                {
                    suggestions.Insert(0, ocrFix);
                }

                mistakes.Add(new SpellcheckMistake
                {
                    Word = dto.Word,
                    Start = globalStart,
                    Length = dto.Length,
                    Suggestions = suggestions
                });
            }

            offset += len;
        }

        result.Mistakes = mistakes
            .OrderBy(m => m.Start)
            .ThenBy(m => m.Length)
            .Take(maxMistakes)
            .ToList();

        return result;
    }

    private static string NormalizeNewlines(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
