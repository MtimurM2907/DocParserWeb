using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

/// <summary>Восстановление пропущенных пробелов после OCR (слипшиеся слова).</summary>
internal static class OcrCyrillicSpacingFixer
{
    private static readonly Regex LowerThenUpperWord = new(
        @"([а-яё])([А-ЯЁ][а-яё]{1,})",
        RegexOptions.Compiled);

    private static readonly Regex PunctuationThenLetter = new(
        @"([,.;:!?])([А-ЯЁа-яё])",
        RegexOptions.Compiled);

    private static readonly Regex DigitOrCommaThenCyrillic = new(
        @"([\d][\d.,]*)([А-ЯЁа-яё]{2,})",
        RegexOptions.Compiled);

    private static readonly Regex CyrillicThenDigit = new(
        @"([а-яё]{2,})(\d)",
        RegexOptions.Compiled);

    private static readonly Regex ClosingQuoteThenLetter = new(
        @"([»""])([А-ЯЁа-яё])",
        RegexOptions.Compiled);

    private static readonly Regex EducationalCode = new(@"^\d{2}\.\d{2}(\.\d{2})?", RegexOptions.Compiled);

    public static string Fix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (!RussianSpellcheckHomoglyphs.IsPredominantlyCyrillicText(text)) return text;

        if (NeedsSpacing(text))
        {
            text = ApplyLineFixes(text);
        }

        return text;
    }

    internal static bool NeedsSpacing(string text)
    {
        var letters = text.Count(char.IsLetter);
        if (letters < 40) return false;

        var spaces = text.Count(char.IsWhiteSpace);
        return spaces < letters / 14;
    }

    private static string ApplyLineFixes(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            line = LowerThenUpperWord.Replace(line, "$1 $2");
            line = PunctuationThenLetter.Replace(line, "$1 $2");
            line = ClosingQuoteThenLetter.Replace(line, "$1 $2");
            line = DigitOrCommaThenCyrillic.Replace(line, "$1 $2");

            if (!EducationalCode.IsMatch(line.Trim()))
            {
                line = CyrillicThenDigit.Replace(line, "$1 $2");
            }

            lines[i] = line;
        }

        return string.Join(Environment.NewLine, lines);
    }
}
