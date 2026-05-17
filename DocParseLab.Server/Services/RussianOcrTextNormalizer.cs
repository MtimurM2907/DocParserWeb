using System.Globalization;
using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

/// <summary>Постобработка типичных OCR-ошибок в русских официальных документах.</summary>
internal static class RussianOcrTextNormalizer
{
    private static readonly Regex CyrillicLetter = new(@"\p{IsCyrillic}", RegexOptions.Compiled);
    private static readonly Regex LatinInWord = new(@"[A-Za-z]", RegexOptions.Compiled);

    private static readonly (string Pattern, string Replacement)[] KnownPhraseFixes =
    {
        (@"\b[Сс][Оо][Оо]?[Тt][Вв][еЕ][Тt][Сc][Тt]?[Вв]?[Ии]?[Нn]?[Еe]?\b", "соответствие"),
        (@"\b[Сс][Оо][Оо]?[Тt][Вв][еЕ][Тt][Сc][Тt]?[Вв]?\b", "соответств"),
        (@"\bМатсематическ\p{IsCyrillic}*\b", "Математическ"),
        (@"\bобсспечен\p{IsCyrillic}*\b", "обеспечен"),
        (@"\bобсспеч\p{IsCyrillic}*\b", "обеспеч"),
        (@"\bспециальност\b(?!\p{IsCyrillic})", "специальности"),
        (@"\bнаименован\p{IsCyrillic}* специальност\b", "наименование специальности"),
    };

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (!RussianSpellcheckHomoglyphs.IsPredominantlyCyrillicText(text))
        {
            return text;
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            line = ApplyKnownPhraseFixes(line);
            line = NormalizeTokens(line);
            lines[i] = line;
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ApplyKnownPhraseFixes(string line)
    {
        foreach (var (pattern, replacement) in KnownPhraseFixes)
        {
            line = Regex.Replace(line, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return line;
    }

    private static string NormalizeTokens(string line)
    {
        var tokens = Regex.Split(line, @"(\s+)");
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrWhiteSpace(token) || token.All(char.IsWhiteSpace))
            {
                continue;
            }

            tokens[i] = NormalizeToken(token);
        }

        return string.Concat(tokens);
    }

    private static string NormalizeToken(string token)
    {
        if (token.Length == 0) return token;

        var letters = token.Count(char.IsLetter);
        if (letters < 3) return token;

        var cyrillic = token.Count(ch => CyrillicLetter.IsMatch(ch.ToString()));
        if (cyrillic < letters * 0.55) return token;

        if (LatinInWord.IsMatch(token))
        {
            token = RussianSpellcheckHomoglyphs.MapLatinHomoglyphs(token, includeULikeMappings: true);
        }

        if (ShouldForceUppercase(token))
        {
            token = ForceCyrillicUppercase(token);
        }

        return FixDoubledCyrillicLetters(token);
    }

    private static bool ShouldForceUppercase(string token)
    {
        var cyrLetters = token.Where(ch => CyrillicLetter.IsMatch(ch.ToString())).ToList();
        if (cyrLetters.Count < 5) return false;

        var upper = cyrLetters.Count(char.IsUpper);
        var lower = cyrLetters.Count(char.IsLower);
        if (lower < 2 || upper < 3) return false;

        var ratio = upper / (double)cyrLetters.Count;
        return ratio is >= 0.35 and <= 0.72;
    }

    private static string ForceCyrillicUppercase(string token)
    {
        Span<char> buf = stackalloc char[token.Length];
        for (var i = 0; i < token.Length; i++)
        {
            var ch = token[i];
            buf[i] = CyrillicLetter.IsMatch(ch.ToString())
                ? char.ToUpper(ch, CultureInfo.GetCultureInfo("ru-RU"))
                : ch;
        }

        return new string(buf);
    }

    private static string FixDoubledCyrillicLetters(string token)
    {
        return token switch
        {
            _ when token.Contains("сс", StringComparison.Ordinal) && token.Contains("обсспеч", StringComparison.OrdinalIgnoreCase)
                => token.Replace("обсспеч", "обеспеч", StringComparison.OrdinalIgnoreCase),
            _ when token.Contains("Матсемат", StringComparison.OrdinalIgnoreCase)
                => token.Replace("Матсемат", "Математ", StringComparison.OrdinalIgnoreCase),
            _ => token
        };
    }
}
