using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

public static class TextStructureFormatter
{
    private static readonly Regex NumberedSubSection = new(@"(?<!\n)(\d+\.\d+\.\s+)", RegexOptions.Compiled);
    private static readonly Regex NumberedSection = new(@"(?<!\n)(?<!\d\.)(\d+\.\s+[А-ЯЁA-Z])", RegexOptions.Compiled);
    private static readonly Regex TooManyEmptyLines = new(@"\n{3,}", RegexOptions.Compiled);

    public static string NormalizeForStorage(string text)
    {
        var source = text ?? string.Empty;
        if (!source.Contains('\f'))
        {
            return NormalizePageContent(source);
        }

        var pages = source.Split('\f', StringSplitOptions.None);
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i] = NormalizePageContent(pages[i]);
        }

        return string.Join("\f", pages);
    }

    public static IReadOnlyList<string> SplitParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        if (!text.Contains('\f'))
        {
            return SplitParagraphsForPage(text);
        }

        return text
            .Split('\f', StringSplitOptions.None)
            .SelectMany(SplitParagraphsForPage)
            .ToList();
    }

    private static string NormalizePageContent(string text)
    {
        var normalized = text
            .Replace("\u00A0", " ")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        var lineBreaks = normalized.Count(c => c == '\n');
        if (lineBreaks <= 3)
        {
            normalized = NumberedSubSection.Replace(normalized, "\n$1");
            normalized = NumberedSection.Replace(normalized, "\n$1");
        }

        normalized = TooManyEmptyLines.Replace(normalized, "\n\n");
        return normalized.Trim();
    }

    private static IReadOnlyList<string> SplitParagraphsForPage(string text)
    {
        var normalized = NormalizePageContent(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<string>();
        }

        return normalized
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();
    }

    public static bool IsSectionTitle(string line) =>
        Regex.IsMatch(line, @"^\d+\.\s+[А-ЯЁA-Z]");

    public static bool IsSubSection(string line) =>
        Regex.IsMatch(line, @"^\d+\.\d+\.\s+");

    public static bool IsCapsTitle(string line) =>
        Regex.IsMatch(line, @"^[А-ЯЁA-Z\s«»""()\-]{8,}$");
}
