using System.Text.RegularExpressions;

namespace DocParseLab.Server.Services;

/// <summary>Удаление типичного мусора OCR (полоски таблиц, повторяющиеся буквы).</summary>
internal static class OcrTextLineFilter
{
    private static readonly Regex MultiSpace = new(@" {2,}", RegexOptions.Compiled);
    private static readonly Regex EducationalCode = new(@"^\d{2}\.\d{2}\.\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex CyrillicLetter = new(@"\p{IsCyrillic}", RegexOptions.Compiled);

    public static string CleanDocument(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>(lines.Length);

        foreach (var raw in lines)
        {
            var line = NormalizeLine(raw);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (IsGarbageLine(line)) continue;
            kept.Add(line);
        }

        return string.Join(Environment.NewLine, kept);
    }

    private static string NormalizeLine(string line)
    {
        line = line.Trim();
        line = Regex.Replace(line, @"^[\|\s]+", string.Empty);
        line = Regex.Replace(line, @"[\|\s]+$", string.Empty);
        line = MultiSpace.Replace(line, " ");

        if (EducationalCode.IsMatch(line))
        {
            var codeMatch = EducationalCode.Match(line);
            var rest = line[codeMatch.Length..].TrimStart();
            if (rest.Contains("  "))
            {
                rest = MultiSpace.Replace(rest, "\t");
            }

            return $"{codeMatch.Value}\t{rest}".TrimEnd();
        }

        if (line.Contains("  "))
        {
            line = MultiSpace.Replace(line, "\t");
        }

        return line.Trim();
    }

    private static bool IsGarbageLine(string line)
    {
        if (line.Length == 0) return true;

        var letters = line.Count(char.IsLetter);
        if (letters == 0)
        {
            return line.All(c => c is '|' or ' ' or '\t' or '-' or '_' or '.');
        }

        if (letters < 3 && line.Length < 8)
        {
            return true;
        }

        if (Regex.IsMatch(line, @"^[\|\s_iIl1]{4,}$"))
        {
            return true;
        }

        if (MaxSameLetterRun(line) >= Math.Max(6, letters * 2 / 5))
        {
            return true;
        }

        var distinctLetters = line.Where(char.IsLetter).Select(char.ToLowerInvariant).Distinct().Count();
        if (letters >= 12 && distinctLetters <= 4)
        {
            return true;
        }

        if (IsLatinOcrNoise(line))
        {
            return true;
        }

        return false;
    }

    /// <summary>Строки вроде «PHHHHHHP HHHEHHHHHHO» — латиница вместо кириллицы.</summary>
    private static bool IsLatinOcrNoise(string line)
    {
        if (line.Length < 6) return false;

        var letterChars = line.Where(char.IsLetter).ToList();
        if (letterChars.Count < 6) return false;

        var cyrillic = letterChars.Count(ch => CyrillicLetter.IsMatch(ch.ToString()));
        var latin = letterChars.Count(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

        if (latin >= letterChars.Count * 0.75 && cyrillic < letterChars.Count * 0.12)
        {
            return true;
        }

        var hpeo = letterChars.Count(ch => ch is 'H' or 'h' or 'P' or 'p' or 'E' or 'e' or 'O' or 'o' or 'N' or 'n' or 'C' or 'c');
        return hpeo >= letterChars.Count * 0.55 && cyrillic < letterChars.Count * 0.2;
    }

    private static int MaxSameLetterRun(string line)
    {
        var max = 1;
        var run = 1;
        for (var i = 1; i < line.Length; i++)
        {
            if (char.ToLowerInvariant(line[i]) == char.ToLowerInvariant(line[i - 1]) && char.IsLetter(line[i]))
            {
                run++;
                max = Math.Max(max, run);
            }
            else
            {
                run = 1;
            }
        }

        return max;
    }
}
