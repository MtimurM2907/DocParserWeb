using System.Text.RegularExpressions;
using DocParseLab.Server.DTOs;
using WeCantSpell.Hunspell;

namespace DocParseLab.Server.Services;

public sealed class HunspellSpellcheckService : ISpellcheckService
{
    private readonly RussianHunspellDictionary _dictionary;

    // В PDF/OCR часто встречаются "невидимые" разделители (soft hyphen / zero-width / BOM),
    // а также смешение латиницы и кириллицы (a/e/o/c/p/x и т.п.). Мы включаем невидимые
    // символы в матч, но для проверки нормализуем токен отдельно.
    private static readonly Regex WordRegex = new(
        @"[\p{L}\u00AD\u200B\u200C\u200D\u200E\u200F\uFEFF]+(?:[-'’][\p{L}\u00AD\u200B\u200C\u200D\u200E\u200F\uFEFF]+)*",
        RegexOptions.Compiled);
    private static readonly Regex CyrillicWordRegex = new(@"^[\p{IsCyrillic}\-]+$", RegexOptions.Compiled);

    public HunspellSpellcheckService(RussianHunspellDictionary dictionary)
    {
        _dictionary = dictionary;
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

        var wordList = _dictionary.WordList;

        foreach (Match match in WordRegex.Matches(text))
        {
            if (result.Mistakes.Count >= maxMistakes)
            {
                break;
            }

            if (!match.Success) continue;

            var raw = match.Value;
            if (raw.Length < 2) continue;

            // Латинские «ux», «mo» и т.п. в русском тексте — ошибка OCR, показываем исходное слово и кириллическую замену.
            if (RussianSpellcheckHomoglyphs.TryContextualLatinOcrReplacement(raw, text, match.Index) is { } ocrFix
                && !string.Equals(raw, ocrFix, StringComparison.OrdinalIgnoreCase))
            {
                result.Mistakes.Add(new SpellcheckMistake
                {
                    Word = raw,
                    Start = match.Index,
                    Length = match.Length,
                    Suggestions = new List<string> { ocrFix },
                });
                continue;
            }

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

            if (IsCorrectRussianWord(wordList, word))
            {
                continue;
            }

            // Короткие кириллические слова — почти всегда предлоги/союзы; Hunspell даёт ложные срабатывания.
            if (word.Length <= 3 && CyrillicWordRegex.IsMatch(word))
            {
                continue;
            }

            var lower = word.ToLowerInvariant();
            var suggestions = CollectSuggestions(wordList, word, lower, maxSuggestions);

            // Подсказка отличается только регистром — не ошибка («За» ↔ «за»).
            if (suggestions.Any(s => string.Equals(s, word, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Короткие кириллические токены без осмысленных подсказок — ложное срабатывание словаря.
            if (suggestions.Count == 0 && LooksLikePlainCyrillicWord(word))
            {
                continue;
            }

            // Слитное написание («спецпредложений» → «спец предложений») — не ошибка.
            if (IsCompoundWordFalsePositive(wordList, word, suggestions))
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

    private static bool IsCorrectRussianWord(WordList wordList, string word)
    {
        if (RussianSpellcheckLexicon.IsKnown(word))
        {
            return true;
        }

        if (wordList.Check(word))
        {
            return true;
        }

        var lower = word.ToLowerInvariant();
        if (lower != word && wordList.Check(lower))
        {
            return true;
        }

        var capitalized = Capitalize(lower);
        if (capitalized != word && wordList.Check(capitalized))
        {
            return true;
        }

        if (word.Length <= 5)
        {
            var upper = word.ToUpperInvariant();
            if (upper != word && wordList.Check(upper))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectSuggestions(WordList wordList, string word, string lower, int maxSuggestions) =>
        wordList.Suggest(word)
            .Concat(wordList.Suggest(lower))
            .Concat(wordList.Suggest(Capitalize(lower)))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxSuggestions)
            .ToList();

    private static bool LooksLikePlainCyrillicWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;
        if (word.Length < 2) return false;
        return CyrillicWordRegex.IsMatch(word);
    }

    private static bool IsCompoundWordFalsePositive(WordList wordList, string word, IReadOnlyList<string> suggestions)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;

        foreach (var suggestion in suggestions)
        {
            if (string.IsNullOrWhiteSpace(suggestion)) continue;
            var merged = suggestion.Replace(" ", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal);
            if (merged.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        ReadOnlySpan<string> prefixes =
        [
            "спец", "меж", "супер", "мини", "авто", "вне", "внутр", "гос", "общ", "недо", "пере", "под", "над", "пред",
        ];

        foreach (var prefix in prefixes)
        {
            if (!word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || word.Length <= prefix.Length + 3)
            {
                continue;
            }

            var stem = word[prefix.Length..];
            var lowerStem = stem.ToLowerInvariant();
            if (wordList.Check(stem) || wordList.Check(lowerStem) || wordList.Check(Capitalize(lowerStem)))
            {
                return true;
            }
        }

        return false;
    }
}

