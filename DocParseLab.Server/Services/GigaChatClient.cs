using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DocParseLab.Server.Services;

public class GigaChatClient : IGigaChatClient
{
    private readonly HttpClient _httpClient;
    private readonly GigaChatOptions _options;
    private readonly IMemoryCache _cache;

    public GigaChatClient(HttpClient httpClient, IOptions<GigaChatOptions> options, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
    }

    public async Task<GigaChatResult> GetStructuredJsonAsync(string plainText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return new GigaChatResult("{}", "Текст документа пуст, описание не сформировано.");
        }

        var maxChars = 12000;
        var truncated = plainText.Length > maxChars ? plainText[..maxChars] : plainText;
        var cacheKey = "gigachat:" + GigaChatCacheKeys.ForText(truncated);
        if (_cache.TryGetValue(cacheKey, out GigaChatResult? cached) && cached != null)
            return cached;

        var token = await GetAccessTokenAsync(cancellationToken);

        var descriptionPrompt =
            "Ты эксперт по анализу документов. На вход ты получаешь ПОЛНЫЙ текст PDF-документа. " +
            "Сформируй ОЧЕНЬ ПОДРОБНОЕ, РАЗВЁРНУТОЕ текстовое описание этого документа на русском языке. " +
            "Описание должно быть ДЛИННЫМ (ориентируйся минимум на 1500–2000 слов) и охватывать КАЖДЫЙ важный фрагмент текста. " +
            "Обязательно:\n" +
            "1) Подробно опиши общую цель и назначение документа.\n" +
            "2) Перечисли ВСЕ разделы и подпункты, для каждого сделай небольшой пересказ, НЕ пропускай пункты.\n" +
            "3) Развёрнуто распиши обязанности, права, ограничения и ответственность сторон.\n" +
            "4) Опиши требования к условиям труда, режиму работы, внешнему виду, санитарным нормам, технике безопасности и т.п.\n" +
            "5) Укажи все важные числовые параметры: сроки, суммы, проценты, интервалы времени, штрафы, уровни ответственности и т.д.\n" +
            "6) Опиши, какие действия должен выполнять сотрудник в типичных рабочих ситуациях, приведённых в документе.\n" +
            "7) Если есть разделы про ответственность, дисциплинарные меры, порядок увольнения — опиши их максимально подробно.\n" +
            "Пиши в виде обычного связного текста с абзацами, НЕ используй формат JSON и не оформляй ответ как блок кода.";

        var description = await SendChatRequestAsync(token, descriptionPrompt, truncated, cancellationToken);

        if (string.IsNullOrWhiteSpace(description))
        {
            // Если GigaChat не дал описания — дальше нет смысла пытаться строить JSON.
            return new GigaChatResult("{}", string.Empty);
        }

        var jsonPrompt =
            "На вход ты получаешь ПОДРОБНОЕ текстовое описание документа. " +
            "Преобразуй его в структурированный JSON, подходящий для дальнейшей обработки программой. " +
            "Верни ТОЛЬКО ОДИН JSON‑объект без комментариев и объяснений. ";

        var jsonRaw = await SendChatRequestAsync(token, jsonPrompt, description, cancellationToken);

        if (string.IsNullOrWhiteSpace(jsonRaw))
        {
            return new GigaChatResult("{}", description);
        }

        string structuredJson;
        try
        {
            using var doc = JsonDocument.Parse(jsonRaw);
            structuredJson = JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            structuredJson = jsonRaw;
        }

        var result = new GigaChatResult(structuredJson, description);
        _cache.Set(cacheKey, result, TimeSpan.FromHours(12));
        return result;
    }

    public async Task<IReadOnlyList<SpellcheckMistakeDto>> SpellcheckSegmentAsync(
        string textSegment,
        int maxSuggestions,
        int maxMistakes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(textSegment))
        {
            return Array.Empty<SpellcheckMistakeDto>();
        }

        var safeMax = Math.Clamp(maxMistakes, 1, 300);
        var safeSugg = Math.Clamp(maxSuggestions, 1, 10);

        var token = await GetAccessTokenAsync(cancellationToken);

        var systemPrompt =
            "Ты редактор русского языка. Тебе передан фрагмент текста между маркерами <<<BEGIN>>> и <<<END>>>. " +
            "Найди только реальные орфографические ошибки и явные грамматические ошибки в словоформах. " +
            "Частая ошибка в OCR/PDF: латинские буквы вместо кириллицы в русских словах (например лат. mo рядом с русским текстом — предлог «по»; лат. ux/ix — «их» с местоимением). " +
            "Учитывай контекст и предлагай первым наиболее верное кириллическое слово, а не только визуально похожие буквы. " +
            "Не помечай как ошибку: корректные предлоги (по, на, со, из, к), местоимения (он, она, их, мы), союзы, частицы, если они написаны правильно. " +
            "Не придирайся к стилю и канцеляриту — только орфография/грамматика. " +
            "Если фрагмент из OCR/PDF со склеенными словами — укажи это как одну проблемную область с коротким исправлением в suggestions. " +
            $"Верни не более {safeMax} элементов. Для каждой ошибки дай до {safeSugg} вариантов исправления в suggestions. " +
            "Верни ТОЛЬКО JSON-массив без markdown и без текста до/после. Формат строго: " +
            "[{\"word\":\"...\",\"start\":0,\"length\":0,\"suggestions\":[\"...\"]}]. " +
            "Поля start и length — позиция ошибочного фрагмента в символах (как в C# string, UTF-16 кодовые единицы), индексация с 0, относительно текста СТРОГО между <<<BEGIN>>> и <<<END>>> без учёта самих маркеров.";

        var userContent = "<<<BEGIN>>>\n" + textSegment + "\n<<<END>>>";

        var raw = await SendChatRequestAsync(token, systemPrompt, userContent, cancellationToken);
        return ParseSpellcheckJson(raw, textSegment, safeMax, safeSugg);
    }

    private static IReadOnlyList<SpellcheckMistakeDto> ParseSpellcheckJson(
        string raw,
        string textSegment,
        int maxMistakes,
        int maxSuggestions)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<SpellcheckMistakeDto>();
        }

        var json = ExtractJsonArray(raw);
        if (json is null)
        {
            return Array.Empty<SpellcheckMistakeDto>();
        }

        List<SpellcheckMistakeDto>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<SpellcheckMistakeDto>>(json, JsonOptions);
        }
        catch
        {
            return Array.Empty<SpellcheckMistakeDto>();
        }

        if (list is null || list.Count == 0)
        {
            return Array.Empty<SpellcheckMistakeDto>();
        }

        var result = new List<SpellcheckMistakeDto>();
        foreach (var item in list.Take(maxMistakes))
        {
            if (string.IsNullOrEmpty(item.Word)) continue;

            var start = Math.Clamp(item.Start, 0, textSegment.Length);
            var len = Math.Clamp(item.Length, 0, textSegment.Length - start);
            if (len <= 0) continue;

            var slice = textSegment.AsSpan(start, len).ToString();
            if (!string.Equals(slice, item.Word, StringComparison.Ordinal) &&
                !string.Equals(slice.Trim(), item.Word.Trim(), StringComparison.Ordinal))
            {
                var found = textSegment.IndexOf(item.Word, Math.Max(0, start - 80), StringComparison.Ordinal);
                if (found < 0)
                {
                    found = textSegment.IndexOf(item.Word, StringComparison.Ordinal);
                }
                if (found >= 0)
                {
                    start = found;
                    len = item.Word.Length;
                    if (start + len > textSegment.Length) continue;
                }
                else
                {
                    continue;
                }
            }

            result.Add(new SpellcheckMistakeDto
            {
                Word = textSegment.Substring(start, len),
                Start = start,
                Length = len,
                Suggestions = item.Suggestions?
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(maxSuggestions)
                    .ToList() ?? new List<string>()
            });
        }

        return result;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string? ExtractJsonArray(string raw)
    {
        var t = raw.Trim();
        var fence = Regex.Match(t, @"```(?:json)?\s*(\[.*?\])\s*```", RegexOptions.Singleline);
        if (fence.Success)
        {
            return fence.Groups[1].Value;
        }

        var start = t.IndexOf('[');
        var end = t.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return t.Substring(start, end - start + 1);
        }

        return null;
    }

    private async Task<string> SendChatRequestAsync(string token, string systemPrompt, string userContent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new ChatRequest
        {
            Model = _options.Model,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userContent }
            ]
        };

        var json = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var chatResponse = await JsonSerializer.DeserializeAsync<ChatResponse>(stream, cancellationToken: cancellationToken);

        return chatResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var authorization = _options.ResolveAuthorization();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new InvalidOperationException(
                "GigaChat не настроен. Скопируйте gigachat.secrets.json.example → gigachat.secrets.json " +
                "и укажите ClientId и ClientSecret из https://developers.sber.ru/ (проект GigaChat API). " +
                "Либо задайте GigaChat__ClientId и GigaChat__ClientSecret в переменных окружения.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.AuthUrl);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        request.Headers.Add("RqUID", Guid.NewGuid().ToString());

        request.Content = new StringContent($"scope={_options.Scope}", Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"GigaChat OAuth: {(int)response.StatusCode} {response.ReasonPhrase}. {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var tokenResponse = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, cancellationToken: cancellationToken);

        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("Не удалось получить токен доступа GigaChat.");
        }

        return tokenResponse.AccessToken;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "GigaChat";

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = new();
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice> Choices { get; set; } = new();
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}


