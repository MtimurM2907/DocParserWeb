using System.Text.Json.Serialization;

namespace DocParseLab.Server.Services;

/// <summary>
/// 
/// </summary>
public sealed record GigaChatResult(
    string StructuredJson,
    string HumanReadable);

public sealed record RewriteResult(
    string RewrittenText,
    string ModelComment);

public interface IGigaChatClient
{
    /// <summary>
    /// </summary>
    Task<GigaChatResult> GetStructuredJsonAsync(string plainText, CancellationToken cancellationToken = default);

    Task<RewriteResult> RewriteTextAsync(
        string text,
        string mode,
        string? tone = null,
        string? length = null,
        CancellationToken cancellationToken = default);

    /// <summary>Проверка орфографии и явных грамматических ошибок моделью (JSON с позициями в переданном фрагменте).</summary>
    Task<IReadOnlyList<SpellcheckMistakeDto>> SpellcheckSegmentAsync(
        string textSegment,
        int maxSuggestions,
        int maxMistakes,
        CancellationToken cancellationToken = default);
}

/// <summary>Элемент ответа модели для spellcheck (совместим с <see cref="DocParseLab.Server.DTOs.SpellcheckMistake"/>).</summary>
public sealed class SpellcheckMistakeDto
{
    [JsonPropertyName("word")]
    public string Word { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("length")]
    public int Length { get; set; }

    [JsonPropertyName("suggestions")]
    public List<string> Suggestions { get; set; } = new();
}


