using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

/// <summary>Исправление типичных OCR-ошибок на титульных листах (обрыв заголовков, разрыв слов).</summary>
internal static class RussianOcrTitlePageNormalizer
{
    private static readonly (string Pattern, string Replacement)[] LineFixes =
    [
        (@"\bПРОГРАММА\s+ВСТУПИТЕЛЬНОГО\s+Э\b", "ПРОГРАММА ВСТУПИТЕЛЬНОГО ЭКЗАМЕНА"),
        (@"\bВСТУПИТЕЛЬНОГО\s+Э\b", "ВСТУПИТЕЛЬНОГО ЭКЗАМЕНА"),
        (@"\bВСТУПИТЕЛЬНЫЙ\s+Э\b", "ВСТУПИТЕЛЬНЫЙ ЭКЗАМЕН"),
        (@"\bПО\s+РУССКОМУ\s+ЯЗЫК\b(?!\p{IsCyrillic})", "ПО РУССКОМУ ЯЗЫКУ"),
        (@"\bКурганский\s+государственный\b", "Курганский государственный"),
        (@"\bгосударственное\s+бюджетное\b", "государственное бюджетное"),
        (@"\bобразовательное\s+учреждение\b", "образовательное учреждение"),
        (@"\bвысшего\s+образования\b", "высшего образования"),
        (@"\bМинистерство\s+науки\b", "Министерство науки"),
    ];

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = MergeSplitTitleLines(text);

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            foreach (var (pattern, replacement) in LineFixes)
            {
                line = Regex.Replace(line, pattern, replacement, RegexOptions.IgnoreCase);
            }

            lines[i] = line;
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string MergeSplitTitleLines(string text)
    {
        var lines = text.Split('\n').ToList();
        for (var i = 0; i < lines.Count - 1; i++)
        {
            var cur = lines[i].Trim();
            var next = lines[i + 1].Trim();
            if (cur.Length == 0 || next.Length == 0) continue;

            if (Regex.IsMatch(cur, @"ВСТУПИТЕЛЬНОГО\s+Э\s*$", RegexOptions.IgnoreCase)
                && Regex.IsMatch(next, @"^ПО\s+РУССКОМУ", RegexOptions.IgnoreCase))
            {
                lines[i] = Regex.Replace(cur, @"\s+Э\s*$", " ЭКЗАМЕНА", RegexOptions.IgnoreCase);
            }

            if (Regex.IsMatch(cur, @"ПРОГРАММА\s+ВСТУПИТЕЛЬНОГО\s*$", RegexOptions.IgnoreCase)
                && Regex.IsMatch(next, @"^ЭКЗАМЕНА", RegexOptions.IgnoreCase))
            {
                lines[i] = $"{cur} {next}";
                lines.RemoveAt(i + 1);
                i--;
            }
        }

        return string.Join("\n", lines);
    }
}
