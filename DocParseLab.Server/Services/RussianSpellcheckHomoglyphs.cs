using System.Globalization;
using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

/// <summary>Правки латинских «переключений раскладки» / OCR рядом с русским текстом.</summary>
internal static class RussianSpellcheckHomoglyphs
{
    private static readonly Regex CyrillicCharTest = new(@"\p{IsCyrillic}", RegexOptions.Compiled);

    public static string StripInvisibleCharacters(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        return raw
            .Replace("\u00AD", "")
            .Replace("\uFEFF", "")
            .Replace("\u200B", "")
            .Replace("\u200C", "")
            .Replace("\u200D", "")
            .Replace("\u200E", "")
            .Replace("\u200F", "");
    }

    /// <summary>Латинские буквы A–Z / a–z в токене (без учёта дефисов внутри слова).</summary>
    public static bool IsLatinLettersOnly(string strippedToken)
    {
        if (string.IsNullOrEmpty(strippedToken)) return false;
        foreach (var ch in strippedToken)
        {
            if (ch is '-' or '\'' or '’') continue;
            if (!IsBasicLatinLetter(ch)) return false;
        }
        return true;
    }

    public static bool HasLatinLetters(string strippedToken)
    {
        foreach (var ch in strippedToken)
        {
            if (IsBasicLatinLetter(ch)) return true;
        }
        return false;
    }

    public static bool HasCyrillicLetters(string strippedToken) =>
        !string.IsNullOrEmpty(strippedToken) && CyrillicCharTest.IsMatch(strippedToken);

    public static bool HasAdjacentCyrillic(string text, int tokenStart, int tokenLength)
    {
        if (text.Length == 0 || tokenLength <= 0) return false;

        int i = tokenStart - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }
        bool leftOk = i >= 0 && IsCyrillicLetter(text[i]);

        int j = tokenStart + tokenLength;
        while (j < text.Length && char.IsWhiteSpace(text[j]))
        {
            j++;
        }
        bool rightOk = j < text.Length && IsCyrillicLetter(text[j]);

        return leftOk || rightOk;
    }

    /// <summary>
    /// Типичные OCR/раскладка: латиница вместо кириллицы в русском тексте (mo→по, ux→их).
    /// </summary>
    public static string? TryContextualLatinOcrReplacement(string rawToken, string fullText, int tokenStart)
    {
        var inner = StripInvisibleCharacters(rawToken);
        if (inner.Length is < 2 or > 4) return null;
        if (!IsLatinLettersOnly(inner)) return null;

        var inRussianContext = HasAdjacentCyrillic(fullText, tokenStart, rawToken.Length)
                               || IsPredominantlyCyrillicText(fullText);

        if (!inRussianContext) return null;

        var lower = inner.ToLowerInvariant();
        return lower switch
        {
            "mo" => MatchCase(inner, "по"),
            "ux" or "ix" => MatchCase(inner, "их"),
            "ho" => MatchCase(inner, "но"),
            "ee" => MatchCase(inner, "ее"),
            "cb" => MatchCase(inner, "сб"),
            _ => null
        };
    }

    /// <summary>Документ в основном на кириллице (для OCR-правок коротких латинских вставок).</summary>
    public static bool IsPredominantlyCyrillicText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var cyrillic = 0;
        var latin = 0;
        foreach (var ch in text)
        {
            if (IsCyrillicLetter(ch)) cyrillic++;
            else if (IsBasicLatinLetter(ch)) latin++;
        }

        return cyrillic >= 40 && cyrillic > latin * 4;
    }

    /// <summary>Нормализация токена для Hunspell: невидимые символы, латиница как кириллица, смешанные токены.</summary>
    public static string NormalizeTokenForRuHunspell(string raw, string fullText, int tokenStart)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var s = StripInvisibleCharacters(raw);
        if (s.Length == 0) return string.Empty;

        if (TryContextualLatinOcrReplacement(raw, fullText, tokenStart) is { } replaced)
        {
            return replaced;
        }

        if (HasLatinLetters(s) && HasCyrillicLetters(s))
        {
            return MapLatinCharsInMixedToken(s, includeULikeMappings: true);
        }

        return MapLatinHomoglyphs(s, includeULikeMappings: false);
    }

    private static string MapLatinCharsInMixedToken(string s, bool includeULikeMappings)
    {
        Span<char> buffer = s.Length <= 512 ? stackalloc char[s.Length] : new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            buffer[i] = IsBasicLatinLetter(ch)
                ? MapLatinHomoglyphToCyrillic(ch, includeULikeMappings)
                : ch;
        }
        return new string(buffer);
    }

    public static string MapLatinHomoglyphs(string s, bool includeULikeMappings)
    {
        Span<char> buffer = s.Length <= 512 ? stackalloc char[s.Length] : new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            buffer[i] = MapLatinHomoglyphToCyrillic(s[i], includeULikeMappings);
        }
        return new string(buffer);
    }

    public static char MapLatinHomoglyphToCyrillic(char ch, bool includeULikeMappings)
    {
        if (includeULikeMappings)
        {
            switch (ch)
            {
                case 'u': return 'и';
                case 'U': return 'И';
            }
        }

        return ch switch
        {
            'a' => 'а',
            'c' => 'с',
            'e' => 'е',
            'o' => 'о',
            'p' => 'р',
            'x' => 'х',
            'y' => 'у',
            'k' => 'к',
            'm' => 'м',
            't' => 'т',
            'h' => 'н',
            'b' => 'ь',
            'A' => 'А',
            'C' => 'С',
            'E' => 'Е',
            'O' => 'О',
            'P' => 'Р',
            'X' => 'Х',
            'Y' => 'У',
            'K' => 'К',
            'M' => 'М',
            'T' => 'Т',
            'H' => 'Н',
            'B' => 'В',
            _ => ch
        };
    }

    private static bool IsBasicLatinLetter(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsCyrillicLetter(char c) =>
        char.IsLetter(c) && CyrillicCharTest.IsMatch(c.ToString());

    private static string MatchCase(string asciiSource, string correctCyrillic)
    {
        if (asciiSource.Length != correctCyrillic.Length)
        {
            return correctCyrillic;
        }

        Span<char> buf = stackalloc char[correctCyrillic.Length];
        for (var i = 0; i < correctCyrillic.Length; i++)
        {
            var src = asciiSource[i];
            var target = correctCyrillic[i];
            buf[i] = char.IsUpper(src)
                ? char.ToUpper(target, CultureInfo.InvariantCulture)
                : char.ToLower(target, CultureInfo.InvariantCulture);
        }
        return new string(buf);
    }
}
