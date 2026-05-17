namespace DocParseLab.Server.Services;



/// <summary>Лёгкая постобработка извлечённого текста перед сохранением.</summary>

internal static class PdfTextPostProcessor

{

    public static string NormalizeExtractedText(string? text)

    {

        if (string.IsNullOrWhiteSpace(text)) return string.Empty;



        text = OcrTextLineFilter.CleanDocument(text);
        text = OcrCyrillicSpacingFixer.Fix(text);
        text = RussianOcrTitlePageNormalizer.Normalize(text);
        text = RussianOcrTextNormalizer.Normalize(text);

        if (!RussianSpellcheckHomoglyphs.IsPredominantlyCyrillicText(text))

        {

            return text;

        }



        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var i = 0; i < lines.Length; i++)

        {

            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line)) continue;



            if (RussianSpellcheckHomoglyphs.HasLatinLetters(line) &&

                RussianSpellcheckHomoglyphs.HasCyrillicLetters(line))

            {

                lines[i] = RussianSpellcheckHomoglyphs.MapLatinHomoglyphs(line, includeULikeMappings: true);

            }

        }



        return string.Join(Environment.NewLine, lines);

    }

}

