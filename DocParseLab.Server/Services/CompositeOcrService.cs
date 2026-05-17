using Microsoft.Extensions.Options;

namespace DocParseLab.Server.Services;

/// <summary>Несколько проходов Tesseract; выбирается лучший по качеству текста.</summary>
public sealed class CompositeOcrService : IOcrService
{
    private readonly TesseractOcrService _tesseract;
    private readonly OcrOptions _options;

    public CompositeOcrService(TesseractOcrService tesseract, IOptions<OcrOptions> options)
    {
        _tesseract = tesseract;
        _options = options.Value;
    }

    public Task<string> OcrPngAsync(byte[] pngBytes, CancellationToken cancellationToken = default) =>
        _tesseract.OcrPngAsync(pngBytes, cancellationToken);

    public Task<string> OcrPngStructuredAsync(byte[] pngBytes, CancellationToken cancellationToken = default) =>
        RecognizeBestAsync(pngBytes, structured: true, cancellationToken: cancellationToken);

    public Task<string> OcrScannedPageAsync(byte[] pngBytes, CancellationToken cancellationToken = default) =>
        RecognizeBestAsync(pngBytes, structured: false, fastScan: true, cancellationToken: cancellationToken);

    private async Task<string> RecognizeBestAsync(
        byte[] pngBytes,
        bool structured,
        bool fastScan = false,
        CancellationToken cancellationToken = default)
    {
        if (!_options.UseDualTesseractPass || pngBytes.Length == 0)
        {
            return fastScan
                ? await _tesseract.OcrScannedPageAsync(pngBytes, cancellationToken)
                : structured
                    ? await _tesseract.OcrPngStructuredAsync(pngBytes, cancellationToken)
                    : await _tesseract.OcrPngAsync(pngBytes, cancellationToken);
        }

        var a = fastScan
            ? await _tesseract.OcrScannedPageAsync(pngBytes, cancellationToken)
            : structured
                ? await _tesseract.OcrPngStructuredAsync(pngBytes, cancellationToken)
                : await _tesseract.OcrPngAsync(pngBytes, cancellationToken);

        var light = OcrImagePreprocessor.PrepareLight(pngBytes);
        var b = fastScan
            ? await _tesseract.OcrScannedPageAsync(light, cancellationToken)
            : await _tesseract.OcrPngAsync(light, cancellationToken);

        return PickBetter(a, b);
    }

    private static string PickBetter(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b ?? string.Empty;
        if (string.IsNullOrWhiteSpace(b)) return a;

        var scoreA = ScoreOcrText(a);
        var scoreB = ScoreOcrText(b);
        return scoreB > scoreA ? b : a;
    }

    private static double ScoreOcrText(string text)
    {
        if (PdfTextQualityHeuristics.IsSuspicious(text)) return double.NegativeInfinity;
        return PdfTextQualityHeuristics.Score(text) + text.Length * 0.05;
    }
}
