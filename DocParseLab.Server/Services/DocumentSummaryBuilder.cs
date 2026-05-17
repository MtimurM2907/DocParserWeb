using System.Text;
using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

/// <summary>Структурированное описание документа без внешней LLM (когда GigaChat недоступен).</summary>
public static class DocumentSummaryBuilder
{
    private static readonly Regex SectionHeadingRegex = new(
        @"^\s*(\d+(?:\.\d+)*\.?)\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static string BuildLocalSummary(string fullText, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return "Текст документа пуст или не распознан.";
        }

        var normalized = fullText.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var lines = normalized.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            sb.Append("Файл: ").Append(fileName.Trim()).AppendLine();
        }

        var title = DetectTitle(lines);
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append("Название: ").Append(title).AppendLine();
        }

        sb.Append("Объём: ").Append(normalized.Length).Append(" символов");
        var pageBreaks = normalized.Count(c => c == '\f');
        if (pageBreaks > 0)
        {
            sb.Append(", страниц: ").Append(pageBreaks + 1);
        }

        sb.AppendLine();

        var sections = SectionHeadingRegex.Matches(normalized)
            .Select(m => $"{m.Groups[1].Value.Trim()} {m.Groups[2].Value.Trim()}")
            .Take(12)
            .ToList();

        if (sections.Count > 0)
        {
            sb.AppendLine().AppendLine("Основные разделы:");
            foreach (var s in sections)
            {
                sb.Append("• ").AppendLine(s);
            }
        }

        var excerpt = BuildExcerpt(normalized, 480);
        if (!string.IsNullOrWhiteSpace(excerpt))
        {
            sb.AppendLine().AppendLine("Начало текста:").Append(excerpt);
            if (normalized.Length > excerpt.Length + 20)
            {
                sb.Append(" …");
            }
        }

        return sb.ToString().Trim();
    }

    private static string? DetectTitle(IReadOnlyList<string> lines)
    {
        foreach (var line in lines.Take(8))
        {
            if (line.Length < 8 || line.Length > 200) continue;
            var letters = line.Count(char.IsLetter);
            if (letters < line.Length * 0.6) continue;

            var upper = line.Count(char.IsUpper);
            if (upper >= letters * 0.7)
            {
                return line;
            }
        }

        return lines.FirstOrDefault(l => l.Length >= 10 && l.Length <= 160);
    }

    private static string BuildExcerpt(string text, int maxLength)
    {
        var flat = text.Replace('\f', ' ').Replace('\n', ' ');
        while (flat.Contains("  ", StringComparison.Ordinal))
        {
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);
        }

        flat = flat.Trim();
        if (flat.Length <= maxLength) return flat;

        var cut = flat[..maxLength];
        var lastDot = cut.LastIndexOfAny(new[] { '.', '!', '?' });
        if (lastDot > maxLength / 3)
        {
            cut = cut[..(lastDot + 1)];
        }

        return cut.Trim();
    }
}
