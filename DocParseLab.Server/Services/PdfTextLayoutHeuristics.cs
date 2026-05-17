using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

/// <summary>Оценка того, сохранилась ли табличная вёрстка после извлечения.</summary>
internal static class PdfTextLayoutHeuristics
{
    private static readonly Regex EducationalCode = new(@"\b\d{2}\.\d{2}\.\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex CodeMidLine = new(@"[а-яёА-ЯЁ]{3,}\s+\d{2}\.\d{2}\.\d{2}", RegexOptions.Compiled);

    public static double TableLayoutScore(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var codeLines = 0;
        var structuredCodeLines = 0;
        var tabLines = 0;
        var interleaved = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.Contains('\t')) tabLines++;

            if (!EducationalCode.IsMatch(line)) continue;

            codeLines++;
            if (line.Contains('\t') || CodeAtLineStart(line))
            {
                structuredCodeLines++;
            }

            if (CodeMidLine.IsMatch(line))
            {
                interleaved++;
            }
        }

        if (codeLines < 3)
        {
            return tabLines >= 2 ? 0.35 : 0;
        }

        var structureRatio = structuredCodeLines / (double)codeLines;
        var tabBonus = Math.Min(0.35, tabLines / (double)Math.Max(codeLines, 1) * 0.35);
        var interleavePenalty = Math.Min(0.55, interleaved / (double)Math.Max(codeLines, 1) * 0.55);

        return Math.Max(0, structureRatio * 0.75 + tabBonus - interleavePenalty);
    }

    public static bool HasWeakTableLayout(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var codeMatches = EducationalCode.Matches(text).Count;
        if (codeMatches < 4) return false;

        var score = TableLayoutScore(text);
        if (score < 0.38) return true;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var interleaved = lines.Count(l => CodeMidLine.IsMatch(l));
        return interleaved >= Math.Max(3, codeMatches / 8);
    }

    private static bool CodeAtLineStart(string line)
    {
        var trimmed = line.TrimStart();
        return EducationalCode.IsMatch(trimmed) && trimmed.IndexOf(EducationalCode.Match(trimmed).Value, StringComparison.Ordinal) <= 2;
    }
}
