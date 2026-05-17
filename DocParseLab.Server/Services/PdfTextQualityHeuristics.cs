using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

/// <summary>Оценка качества текста, извлечённого из PDF (битый слой шрифтов vs нормальный текст).</summary>
internal static class PdfTextQualityHeuristics
{
    private static readonly Regex CyrillicLetter = new(@"\p{IsCyrillic}", RegexOptions.Compiled);

    public static bool IsSuspicious(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        var s = text;
        var len = s.Length;
        var letters = 0;
        var cyrillic = 0;
        var latin = 0;
        var digits = 0;
        var suspiciousSymbols = 0;

        foreach (var ch in s)
        {
            if (char.IsLetter(ch))
            {
                letters++;
                if (CyrillicLetter.IsMatch(ch.ToString())) cyrillic++;
                else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z') latin++;
            }
            else if (char.IsDigit(ch))
            {
                digits++;
            }

            if (ch is '"' or '\'' or '?' or ']' or '[' or '{' or '}' or '|' or '`' or '@' or ']' or '„' or '“' or '”')
            {
                suspiciousSymbols++;
            }
        }

        if (letters < 12) return true;

        var latinInCyrillicDoc = cyrillic > 30 && latin > 0 && latin >= cyrillic * 0.06;
        var symbolNoise = suspiciousSymbols >= Math.Max(2, letters / 50);
        var mixedCaseChaos = HasChaoticCyrillicCasing(s);
        var garbageTokens = CountGarbageTokens(s) >= Math.Max(1, letters / 100);

        return latinInCyrillicDoc || symbolNoise || mixedCaseChaos || garbageTokens;
    }

    public static double Score(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NegativeInfinity;

        var letters = text.Count(char.IsLetter);
        if (letters == 0) return double.NegativeInfinity;

        var cyrillic = text.Count(ch => CyrillicLetter.IsMatch(ch.ToString()));
        var latin = text.Count(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        var suspicious = text.Count(ch => ch is '"' or '?' or ']' or '[');

        var cyrillicRatio = cyrillic / (double)Math.Max(1, letters);
        var latinPenalty = latin * 2.5;
        var symbolPenalty = suspicious * 4.0;
        var garbagePenalty = CountGarbageTokens(text) * 25.0;
        var lengthBoost = Math.Min(500.0, text.Length * 0.05);

        return cyrillicRatio * 120.0 + lengthBoost - latinPenalty - symbolPenalty - garbagePenalty;
    }

    private static bool HasChaoticCyrillicCasing(string text)
    {
        var tokens = text.Split((char[]?)[' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var chaotic = 0;
        foreach (var token in tokens)
        {
            if (token.Length < 5) continue;
            var cyr = token.Count(ch => CyrillicLetter.IsMatch(ch.ToString()));
            if (cyr < token.Length * 0.7) continue;

            var upper = token.Count(char.IsUpper);
            var lower = token.Count(char.IsLower);
            if (upper >= 2 && lower >= 2 && upper < token.Length * 0.85)
            {
                chaotic++;
            }
        }

        return chaotic >= 2;
    }

    private static int CountGarbageTokens(string text)
    {
        var count = 0;
        foreach (var token in text.Split((char[]?)[' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 4) continue;

            var letters = token.Count(char.IsLetter);
            if (letters < 3) continue;

            var latin = token.Count(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            var cyr = token.Count(ch => CyrillicLetter.IsMatch(ch.ToString()));
            var weird = token.Count(ch => ch is '"' or '?' or ']' or '[' or '1' or '0');

            if (cyr >= 2 && latin >= 1) count++;
            else if (weird >= 2) count++;
            else if (token.Any(char.IsDigit) && cyr >= 2) count++;
            else if (cyr >= 4 && token.Length >= 7 && (latin >= 1 || token.Count(char.IsDigit) >= 1)) count++;
        }

        return count;
    }
}
