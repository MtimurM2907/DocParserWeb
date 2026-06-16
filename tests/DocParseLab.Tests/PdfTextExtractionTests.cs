using DocParseLab.Server.Services;
using UglyToad.PdfPig;

namespace DocParseLab.Tests;

public class PdfTextExtractionTests
{
    private static readonly string SamplePdf = @"c:\Users\User\Desktop\вопросыPDF.pdf";

    [Fact]
    public void NativePdfLayer_IsNotEmpty_AndContainsExpectedTerms()
    {
        if (!File.Exists(SamplePdf))
        {
            return;
        }

        using var pdf = PdfDocument.Open(SamplePdf);
        var pages = pdf.GetPages().OrderBy(p => p.Number).ToList();
        Assert.NotEmpty(pages);

        var allText = string.Join("\n", pages.Select(p => p.Text ?? string.Empty));
        Assert.True(allText.Length > 200, $"Expected substantial text, got {allText.Length} chars");
        Assert.Contains("DocParseLab", allText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QualityHeuristic_ShouldNotFlagNativeTextWithLatinTerms()
    {
        if (!File.Exists(SamplePdf))
        {
            return;
        }

        using var pdf = PdfDocument.Open(SamplePdf);
        var pageText = pdf.GetPages().First().Text ?? string.Empty;
        Assert.True(pageText.Length > 80);

        Assert.False(
            PdfTextQualityHeuristics.IsSuspicious(pageText),
            "Native PDF text with Latin tech terms must not trigger OCR");
    }

    [Fact]
    public void PostProcessor_PreservesLatinTermsOnMixedLines()
    {
        const string line = "Работа с PDF и DOCX в DocParseLab.";
        var processed = PdfTextPostProcessor.NormalizeExtractedText(line);

        Assert.Contains("PDF", processed, StringComparison.Ordinal);
        Assert.Contains("DOCX", processed, StringComparison.Ordinal);
        Assert.Contains("DocParseLab", processed, StringComparison.Ordinal);
    }
}
