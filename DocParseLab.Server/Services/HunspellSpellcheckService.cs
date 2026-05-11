using System.Text.RegularExpressions;
using DocParseLab.Server.DTOs;
using WeCantSpell.Hunspell;

namespace DocParseLab.Server.Services;

public sealed class HunspellSpellcheckService : ISpellcheckService
{
    // Сервис singleton: держим загруженный словарь в памяти.
    private readonly Lazy<WordList> _ruWordList;
    private readonly ILogger<HunspellSpellcheckService> _logger;

    // В PDF/OCR часто встречаются "невидимые" разделители (soft hyphen / zero-width / BOM),
    // а также смешение латиницы и кириллицы (a/e/o/c/p/x и т.п.). Мы включаем невидимые
    // символы в матч, но для проверки нормализуем токен отдельно.
    private static readonly Regex WordRegex = new(
        @"[\p{L}\u00AD\u200B\u200C\u200D\u200E\u200F\uFEFF]+(?:[-'’][\p{L}\u00AD\u200B\u200C\u200D\u200E\u200F\uFEFF]+)*",
        RegexOptions.Compiled);
    private static readonly Regex CyrillicWordRegex = new(@"^[\p{IsCyrillic}\-]+$", RegexOptions.Compiled);

    public HunspellSpellcheckService(IWebHostEnvironment env, ILogger<HunspellSpellcheckService> logger)
    {
        _logger = logger;
        _ruWordList = new Lazy<WordList>(() =>
        {
            var affPath = Path.Combine(env.ContentRootPath, "Resources", "Hunspell", "ru_RU", "ru_RU.aff");
            var dicPath = Path.Combine(env.ContentRootPath, "Resources", "Hunspell", "ru_RU", "ru_RU.dic");

            if (!File.Exists(affPath) || !File.Exists(dicPath))
            {
                throw new FileNotFoundException(
                    $"Не найдены файлы словаря Hunspell: '{affPath}' и/или '{dicPath}'. Проверьте, что они копируются в Output.");
            }

            // Важно: сначала AFF (правила), затем DIC (словарь)
            var wl = WordList.CreateFromFiles(affPath, dicPath);

            // Быстрая самопроверка: если словарь загрузился некорректно, Hunspell помечает почти все слова как ошибки.
            // Это чаще всего происходит из-за перепутанного порядка файлов или проблем с кодировкой.
            if (!wl.Check("документ") && !wl.Check("официант"))
            {
                _logger.LogWarning("Hunspell ru_RU выглядит некорректно загруженным: тестовые слова не распознаны.");
            }

            return wl;
        });
    }

    public Task<SpellcheckResponse> CheckAsync(SpellcheckRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Check(request));
    }

    private SpellcheckResponse Check(SpellcheckRequest request)
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

        // Ограничение на вход, чтобы не уронить сервер очень большим текстом
        if (text.Length > 500_000)
        {
            throw new InvalidOperationException("Слишком большой текст для проверки. Максимум: 500000 символов.");
        }

        var wordList = _ruWordList.Value;

        foreach (Match match in WordRegex.Matches(text))
        {
            if (result.Mistakes.Count >= maxMistakes)
            {
                break;
            }

            if (!match.Success) continue;

            var raw = match.Value;
            if (raw.Length < 2) continue;

            var word = RussianSpellcheckHomoglyphs.NormalizeTokenForRuHunspell(raw, text, match.Index);
            if (word.Length < 2) continue;

            // Не ругаемся на "слова" с цифрами и т.п.
            bool hasDigit = false;
            for (int i = 0; i < word.Length; i++)
            {
                if (char.IsDigit(word[i]))
                {
                    hasDigit = true;
                    break;
                }
            }
            if (hasDigit) continue;

            // Hunspell чувствителен к регистру; для русского нормализуем первый символ.
            // (При этом полностью lower может ухудшать подсказки для аббревиатур.)
            if (!wordList.Check(word))
            {
                // Попробуем ещё lower-case вариант
                var lower = word.ToLowerInvariant();
                if (lower != word && wordList.Check(lower))
                {
                    continue;
                }

                // Для подсказок лучше пробовать несколько вариантов регистра:
                // - исходное слово
                // - lower (часто даёт лучшие результаты для ALL CAPS)
                // - Capitalized
                var suggestions = wordList.Suggest(word)
                    .Concat(wordList.Suggest(lower))
                    .Concat(wordList.Suggest(Capitalize(lower)))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(maxSuggestions)
                    .ToList();

                // Если слово полностью кириллическое, но Hunspell не дал ни одной подсказки,
                // это часто артефакт словаря/морфологии, а не реальная орфографическая ошибка.
                // Чтобы не засыпать UI ложными срабатываниями, такие токены пропускаем.
                if (suggestions.Count == 0 && LooksLikePlainCyrillicWord(word))
                {
                    continue;
                }

                result.Mistakes.Add(new SpellcheckMistake
                {
                    Word = word,
                    Start = match.Index,
                    Length = match.Length,
                    Suggestions = suggestions
                });
            }
        }

        return result;
    }

    private static string NormalizeNewlines(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length == 1) return value.ToUpperInvariant();
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static bool LooksLikePlainCyrillicWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;
        if (word.Length < 3) return false;
        return CyrillicWordRegex.IsMatch(word);
    }
}

