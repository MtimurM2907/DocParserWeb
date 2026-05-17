using System.Text;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace DocParseLab.Server.Services;

/// <summary>Автоправка типичных OCR-ошибок по словарю Hunspell (без изменения кодов и чисел).</summary>
internal static class RussianOcrSpellCorrector
{
    private static readonly Regex WordRegex = new(
        @"[\p{L}\u00AD\u200B\u200C\u200D\u200E\u200F\uFEFF]+(?:[-'’][\p{L}\u00AD\u200B\u200C\u200D\u200E\u200F\uFEFF]+)*",
        RegexOptions.Compiled);

    private static readonly Regex CyrillicWord = new(@"^[\p{IsCyrillic}\-]+$", RegexOptions.Compiled);
    private static readonly Regex EducationalCode = new(@"^\d{2}\.\d{2}(\.\d{2})?$", RegexOptions.Compiled);

    public static string CorrectDocument(string? text, WordList wordList, bool enabled = true)
    {
        if (!enabled || string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        if (!RussianSpellcheckHomoglyphs.IsPredominantlyCyrillicText(text))
        {
            return text;
        }

        var edits = new List<(int Start, int Length, string Replacement)>();

        foreach (Match match in WordRegex.Matches(text))
        {
            if (!match.Success) continue;

            var raw = match.Value;
            if (raw.Length < 4) continue;
            if (EducationalCode.IsMatch(raw)) continue;

            if (TryAutoCorrect(raw, text, match.Index, wordList, out var corrected)
                && !string.Equals(raw, corrected, StringComparison.Ordinal))
            {
                edits.Add((match.Index, match.Length, corrected));
            }
        }

        if (edits.Count == 0) return text;

        edits.Sort((a, b) => b.Start.CompareTo(a.Start));
        var sb = new StringBuilder(text);
        foreach (var (start, length, replacement) in edits)
        {
            sb.Remove(start, length);
            sb.Insert(start, replacement);
        }

        return sb.ToString();
    }

    private static bool TryAutoCorrect(string raw, string fullText, int tokenStart, WordList wordList, out string corrected)
    {
        corrected = raw;

        if (RussianSpellcheckHomoglyphs.TryContextualLatinOcrReplacement(raw, fullText, tokenStart) is { } ocrFix)
        {
            corrected = ocrFix;
            return true;
        }

        var word = RussianSpellcheckHomoglyphs.NormalizeTokenForRuHunspell(raw, fullText, tokenStart);
        if (word.Length < 4 || !CyrillicWord.IsMatch(word)) return false;

        for (var i = 0; i < word.Length; i++)
        {
            if (char.IsDigit(word[i])) return false;
        }

        if (IsCorrect(wordList, word)) return false;

        if (word.Length <= 3) return false;

        var lower = word.ToLowerInvariant();
        var candidates = wordList.Suggest(word)
            .Concat(wordList.Suggest(lower))
            .Concat(wordList.Suggest(Capitalize(lower)))
            .Where(s => !string.IsNullOrWhiteSpace(s) && CyrillicWord.IsMatch(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Trim())
            .Where(s => IsCorrect(wordList, s))
            .ToList();

        if (candidates.Count == 0) return false;

        var best = PickBestCandidate(word, candidates);
        if (best == null) return false;

        corrected = ApplyCasePattern(raw, best);
        return !string.Equals(raw, corrected, StringComparison.Ordinal);
    }

    private static string? PickBestCandidate(string word, List<string> candidates)
    {
        var lower = word.ToLowerInvariant();
        string? best = null;
        var bestDist = int.MaxValue;

        foreach (var c in candidates)
        {
            var d = Levenshtein(lower, c.ToLowerInvariant());
            var maxAllowed = word.Length <= 6 ? 1 : word.Length <= 10 ? 2 : word.Length <= 16 ? 2 : 3;
            if (d > maxAllowed) continue;

            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        if (best != null) return best;

        if (candidates.Count == 1 && word.Length >= 6)
        {
            var only = candidates[0];
            if (Levenshtein(lower, only.ToLowerInvariant()) <= 3)
            {
                return only;
            }
        }

        return null;
    }

    private static bool IsCorrect(WordList wordList, string word)
    {
        if (RussianSpellcheckLexicon.IsKnown(word)) return true;
        if (wordList.Check(word)) return true;
        var lower = word.ToLowerInvariant();
        if (wordList.Check(lower)) return true;
        return wordList.Check(Capitalize(lower));
    }

    private static string ApplyCasePattern(string source, string target)
    {
        if (string.IsNullOrEmpty(target)) return target;
        if (source.All(char.IsUpper)) return target.ToUpperInvariant();
        if (char.IsUpper(source[0])) return Capitalize(target);
        return target.ToLowerInvariant();
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }
}
