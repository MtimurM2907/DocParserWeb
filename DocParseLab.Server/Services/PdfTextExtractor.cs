using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DocParseLab.Server.Services;

public sealed class PdfTextExtractor : IDocumentTextExtractor
{
    private readonly IOcrService _ocr;
    private readonly IPdfPageRenderer _pageRenderer;
    private readonly OcrOptions _options;
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(
        IOcrService ocr,
        IPdfPageRenderer pageRenderer,
        IOptions<OcrOptions> options,
        ILogger<PdfTextExtractor> logger)
    {
        _ocr = ocr;
        _pageRenderer = pageRenderer;
        _options = options.Value;
        _logger = logger;
    }

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        stream.Position = 0;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var pdfBytes = ms.ToArray();

        using var pdf = PdfDocument.Open(pdfBytes);
        var pages = pdf.GetPages().OrderBy(p => p.Number).ToList();
        var totalPages = pages.Count;
        var isScannedPdf = IsScannedPdf(pages);

        if (isScannedPdf)
        {
            _logger.LogInformation(
                "PDF без текстового слоя ({Pages} стр.): режим скана, рендер + быстрый OCR, параллелизм {Parallelism}.",
                totalPages,
                _options.PageParallelism);
        }

        var pageTexts = new ConcurrentDictionary<int, string>();
        var parallelism = isScannedPdf
            ? Math.Clamp(_options.PageParallelism, 1, 6)
            : 1;

        await Parallel.ForEachAsync(
            pages,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = cancellationToken,
            },
            async (page, ct) =>
            {
                await DocumentParseProgressScope.ReportPageAsync(page.Number, totalPages, ct);
                var pageText = await ExtractPageTextAsync(page, pdfBytes, isScannedPdf, ct);
                pageTexts[page.Number] = string.IsNullOrWhiteSpace(pageText)
                    ? string.Empty
                    : OcrTextLineFilter.CleanDocument(pageText.Trim());
            });

        if (pageTexts.Values.All(string.IsNullOrWhiteSpace))
        {
            return string.Empty;
        }

        return string.Join("\f", Enumerable.Range(1, totalPages).Select(n => pageTexts.GetValueOrDefault(n, string.Empty)));
    }

    private static bool IsScannedPdf(IReadOnlyList<Page> pages) =>
        pages.Count > 0 && pages.All(p => (p.Text ?? string.Empty).Trim().Length < 10);

    private async Task<string> ExtractPageTextAsync(
        Page page,
        byte[] pdfBytes,
        bool isScannedPdf,
        CancellationToken cancellationToken)
    {
        var pigPlain = page.Text ?? string.Empty;
        var layoutWords = BuildLayoutWords(page);
        var pigFormatted = PdfPageLayoutFormatter.FormatFromWords(layoutWords, pigPlain, page.Width);

        string? bestText = null;
        var bestScore = double.NegativeInfinity;

        if (!string.IsNullOrWhiteSpace(pigFormatted) && !PdfTextQualityHeuristics.IsSuspicious(pigFormatted))
        {
            bestText = pigFormatted;
            bestScore = RankExtractedText(pigFormatted);
        }

        var needsOcr = _options.Enabled &&
            (isScannedPdf
                || string.IsNullOrWhiteSpace(pigFormatted)
                || PdfTextQualityHeuristics.IsSuspicious(pigFormatted)
                || PdfTextLayoutHeuristics.HasWeakTableLayout(pigFormatted)
                || pigPlain.Trim().Length < _options.MinTextCharsToSkipOcr);

        if (needsOcr)
        {
            if (!isScannedPdf)
            {
                _logger.LogInformation(
                    "Страница {Page}: OCR (текстовый слой {PigChars} симв.).",
                    page.Number,
                    pigPlain.Trim().Length);
            }

            var ocrText = await RunPageOcrAsync(page, pdfBytes, isScannedPdf, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                var ocrScore = RankExtractedText(ocrText);
                if (ShouldPreferCandidate(ocrText, ocrScore, bestText, bestScore))
                {
                    bestText = ocrText;
                }
            }
        }

        return bestText ?? pigFormatted ?? string.Empty;
    }

    private async Task<string> RunPageOcrAsync(
        Page page,
        byte[] pdfBytes,
        bool isScannedPdf,
        CancellationToken cancellationToken)
    {
        var pageIndex = page.Number - 1;
        byte[]? png = null;
        try
        {
            png = _pageRenderer.RenderPageToPng(pdfBytes, pageIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отрендерить страницу {Page}.", page.Number);
        }

        string? best = null;
        var bestScore = double.NegativeInfinity;

        if (png is { Length: > 0 } &&
            OcrImagePreprocessor.IsValidForOcr(png, _options.MinImageWidthForOcr, _options.MinImageHeightForOcr))
        {
            var fromRender = await RecognizeRenderedPageAsync(png, isScannedPdf, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fromRender))
            {
                var score = RankExtractedText(fromRender);
                best = fromRender;
                bestScore = score;
            }
            else
            {
                _logger.LogWarning(
                    "Страница {Page}: OCR по рендеру пустой, повтор с другим режимом.",
                    page.Number);

                var retryPng = OcrImagePreprocessor.PrepareForOcr(png);
                fromRender = isScannedPdf
                    ? await _ocr.OcrPngStructuredAsync(retryPng, cancellationToken)
                    : await _ocr.OcrPngAsync(retryPng, cancellationToken);

                if (!string.IsNullOrWhiteSpace(fromRender))
                {
                    best = fromRender;
                    bestScore = RankExtractedText(fromRender);
                }
            }
        }

        if (TryGetLargestEmbeddedImageBytes(page, out var embedded) &&
            OcrImagePreprocessor.IsValidForOcr(embedded, _options.MinImageWidthForOcr, _options.MinImageHeightForOcr) &&
            (!isScannedPdf || !_options.ScannedPdfRenderOnly || string.IsNullOrWhiteSpace(best)))
        {
            var fromEmbedded = await _ocr.OcrScannedPageAsync(embedded, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fromEmbedded))
            {
                var embScore = RankExtractedText(fromEmbedded);
                if (ShouldPreferCandidate(fromEmbedded, embScore, best, bestScore))
                {
                    best = fromEmbedded;
                }
            }
        }

        return best ?? string.Empty;
    }

    private async Task<string> RecognizeRenderedPageAsync(
        byte[] png,
        bool isScannedPdf,
        CancellationToken cancellationToken)
    {
        if (isScannedPdf)
        {
            var fast = await _ocr.OcrScannedPageAsync(png, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fast) && !OcrCyrillicSpacingFixer.NeedsSpacing(fast))
            {
                return fast;
            }

            var structured = await _ocr.OcrPngStructuredAsync(png, cancellationToken);
            if (string.IsNullOrWhiteSpace(structured))
            {
                return fast;
            }

            if (string.IsNullOrWhiteSpace(fast))
            {
                return structured;
            }

            return RankExtractedText(structured) >= RankExtractedText(fast) ? structured : fast;
        }

        return await _ocr.OcrPngStructuredAsync(png, cancellationToken);
    }

    private static bool ShouldPreferCandidate(string candidate, double candidateScore, string? current, double currentScore)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (current == null) return true;

        var candidateSuspicious = PdfTextQualityHeuristics.IsSuspicious(candidate);
        var currentSuspicious = PdfTextQualityHeuristics.IsSuspicious(current);
        if (candidateSuspicious && !currentSuspicious) return false;
        if (!candidateSuspicious && currentSuspicious) return true;

        return candidateScore > currentScore
               || (PdfTextLayoutHeuristics.HasWeakTableLayout(current)
                   && !PdfTextLayoutHeuristics.HasWeakTableLayout(candidate));
    }

    private static double RankExtractedText(string text) =>
        PdfTextQualityHeuristics.Score(text) + PdfTextLayoutHeuristics.TableLayoutScore(text) * 120.0;

    private static List<PdfPageLayoutFormatter.LayoutWord> BuildLayoutWords(Page page)
    {
        var result = new List<PdfPageLayoutFormatter.LayoutWord>();
        var pageHeight = page.Height;
        try
        {
            foreach (var word in page.GetWords())
            {
                var text = word.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var box = word.BoundingBox;
                var top = pageHeight - box.Top;
                var bottom = pageHeight - box.Bottom;
                if (top > bottom)
                {
                    (top, bottom) = (bottom, top);
                }

                result.Add(new PdfPageLayoutFormatter.LayoutWord(text, box.Left, top, box.Right, bottom));
            }
        }
        catch
        {
            // ignore
        }

        return result;
    }

    private bool TryGetLargestEmbeddedImageBytes(Page page, out byte[] imageBytes)
    {
        imageBytes = Array.Empty<byte>();
        long bestArea = 0;

        foreach (var img in page.GetImages())
        {
            if (!TryGetImageBytes(img, out var candidate) || candidate.Length == 0)
            {
                continue;
            }

            if (!OcrImagePreprocessor.IsValidForOcr(candidate, _options.MinImageWidthForOcr, _options.MinImageHeightForOcr))
            {
                continue;
            }

            var area = OcrImagePreprocessor.EstimatePixelArea(candidate);
            if (area > bestArea)
            {
                bestArea = area;
                imageBytes = candidate;
            }
        }

        return imageBytes.Length > 0;
    }

    private static bool TryGetImageBytes(IPdfImage image, out byte[] imageBytes)
    {
        imageBytes = Array.Empty<byte>();
        try
        {
            if (image.TryGetPng(out var pngBytes) && pngBytes is { Length: > 0 })
            {
                imageBytes = pngBytes;
                return true;
            }

            if (image.TryGetBytes(out var decodedBytes) && decodedBytes is { Count: > 0 })
            {
                var candidate = decodedBytes as byte[] ?? decodedBytes.ToArray();
                if (IsSupportedEncodedImage(candidate))
                {
                    imageBytes = candidate;
                    return true;
                }
            }

            if (image.RawBytes is { Count: > 0 })
            {
                var rawBytes = image.RawBytes as byte[] ?? image.RawBytes.ToArray();
                if (IsSupportedEncodedImage(rawBytes))
                {
                    imageBytes = rawBytes;
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool IsSupportedEncodedImage(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return true;
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
        if ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A) ||
            (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)) return true;
        return bytes.Length >= 12 &&
               bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
               bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;
    }
}
