using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Tesseract;

namespace DocParseLab.Server.Services;

public sealed class TesseractOcrService : IOcrService, IDisposable
{
    private readonly OcrOptions _options;
    private readonly ILogger<TesseractOcrService> _logger;
    private readonly ConcurrentBag<TesseractEngine> _enginePool = new();
    private readonly object _initLock = new();
    private bool _initialized;

    public TesseractOcrService(IOptions<OcrOptions> options, ILogger<TesseractOcrService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<string> OcrPngAsync(byte[] pngBytes, CancellationToken cancellationToken = default) =>
        OcrInternal(pngBytes, structured: false, fastScan: false, cancellationToken);

    public Task<string> OcrPngStructuredAsync(byte[] pngBytes, CancellationToken cancellationToken = default) =>
        OcrInternal(pngBytes, structured: true, fastScan: false, cancellationToken);

    public Task<string> OcrScannedPageAsync(byte[] pngBytes, CancellationToken cancellationToken = default) =>
        OcrInternal(pngBytes, structured: false, fastScan: true, cancellationToken);

    private Task<string> OcrInternal(byte[] pngBytes, bool structured, bool fastScan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pngBytes.Length == 0) return Task.FromResult(string.Empty);

        if (!OcrImagePreprocessor.IsValidForOcr(pngBytes, _options.MinImageWidthForOcr, _options.MinImageHeightForOcr))
        {
            OcrImagePreprocessor.TryGetSize(pngBytes, out var w, out var h);
            _logger.LogDebug("OCR skipped: image too small ({W}x{H})", w, h);
            return Task.FromResult(string.Empty);
        }

        try
        {
            EnsurePool();
            var engine = RentEngine();
            try
            {
                var text = RecognizeWithFallback(engine, pngBytes, structured, fastScan);
                return Task.FromResult(text);
            }
            finally
            {
                ReturnEngine(engine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR failed for image ({Bytes} bytes)", pngBytes.Length);
            return Task.FromResult(string.Empty);
        }
    }

    private string RecognizeWithFallback(TesseractEngine engine, byte[] sourcePng, bool structured, bool fastScan)
    {
        var variants = new[]
        {
            OcrImagePreprocessor.PrepareForOcr(sourcePng),
            OcrImagePreprocessor.PrepareLight(sourcePng),
            sourcePng,
        };

        string? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var prepared in variants)
        {
            if (OcrImagePreprocessor.EstimateInkRatio(prepared) < 0.001)
            {
                continue;
            }

            using var image = Pix.LoadFromMemory(prepared);
            var text = fastScan
                ? RunScannedPageOcr(engine, image)
                : RunOcr(engine, image, structured);

            if (string.IsNullOrWhiteSpace(text)) continue;

            var score = ScoreText(text, 0.5f);
            if (score > bestScore)
            {
                best = text;
                bestScore = score;
            }

            if (!PdfTextQualityHeuristics.IsSuspicious(text) && text.Length > 80)
            {
                return text;
            }
        }

        return best ?? string.Empty;
    }

    private void EnsurePool()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            var count = Math.Clamp(_options.PageParallelism, 1, 6);
            for (var i = 0; i < count; i++)
            {
                _enginePool.Add(CreateEngine());
            }

            _initialized = true;
        }
    }

    private TesseractEngine RentEngine() => _enginePool.TryTake(out var engine) ? engine : CreateEngine();

    private void ReturnEngine(TesseractEngine engine) => _enginePool.Add(engine);

    private TesseractEngine CreateEngine()
    {
        var path = ResolveTessdataPath(_options.TessdataPath);
        var lang = string.IsNullOrWhiteSpace(_options.Languages) ? "rus" : _options.Languages;

        _logger.LogInformation("Tesseract engine: tessdata={Path}, lang={Lang}", path, lang);
        var engine = new TesseractEngine(path, lang, EngineMode.Default);
        engine.SetVariable("preserve_interword_spaces", "1");
        engine.SetVariable("user_defined_dpi", "300");
        engine.SetVariable("tessedit_char_blacklist", "|¦‖`");
        return engine;
    }

    private static string ResolveTessdataPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            if (Directory.Exists(configuredPath)) return configuredPath;
            throw new InvalidOperationException($"Tesseract tessdata path not found: {configuredPath}");
        }

        var candidates = new[]
        {
            configuredPath,
            Path.Combine(Directory.GetCurrentDirectory(), configuredPath),
            Path.Combine(AppContext.BaseDirectory, configuredPath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", configuredPath),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new InvalidOperationException(
            $"Tesseract tessdata path not found. Checked: {string.Join(", ", candidates.Select(Path.GetFullPath))}");
    }

    private static string RunScannedPageOcr(TesseractEngine engine, Pix source)
    {
        var candidates = new List<(string Text, float Confidence)>
        {
            ReadPage(engine, source, PageSegMode.Auto),
            ReadStructured(engine, source),
            ReadPage(engine, source, PageSegMode.SingleColumn),
            ReadPage(engine, source, PageSegMode.SingleBlock),
            ReadPage(engine, source, PageSegMode.SparseText),
        };

        var ranked = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Text))
            .Select(c => (c.Text, Score: ScoreScannedCandidate(c.Text, c.Confidence)))
            .OrderByDescending(c => c.Score)
            .ToList();

        if (ranked.Count == 0) return string.Empty;

        var best = ranked[0].Text;
        if (OcrCyrillicSpacingFixer.NeedsSpacing(best))
        {
            var structured = candidates
                .Select(c => c.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .OrderByDescending(t => ScoreScannedCandidate(t, 0.5f))
                .FirstOrDefault(t => !OcrCyrillicSpacingFixer.NeedsSpacing(t));
            if (!string.IsNullOrWhiteSpace(structured))
            {
                best = structured;
            }
        }

        return best;
    }

    private static double ScoreScannedCandidate(string text, float confidence)
    {
        var spacingBoost = OcrCyrillicSpacingFixer.NeedsSpacing(text) ? -40.0 : 12.0;
        return confidence * 100.0 + PdfTextQualityHeuristics.Score(text) + spacingBoost;
    }

    private static string RunOcr(TesseractEngine engine, Pix source, bool structured)
    {
        var candidates = new List<(string Text, float Confidence)>
        {
            ReadPage(engine, source, PageSegMode.Auto),
            ReadPage(engine, source, PageSegMode.SingleBlock),
        };

        if (structured)
        {
            candidates.Add(ReadStructured(engine, source));
        }

        var ranked = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Text))
            .Select(c => (c.Text, Score: ScoreText(c.Text, c.Confidence)))
            .OrderByDescending(c => c.Score)
            .ToList();

        if (ranked.Count == 0) return string.Empty;

        var clean = ranked.Where(c => !PdfTextQualityHeuristics.IsSuspicious(c.Text)).ToList();
        return (clean.Count > 0 ? clean : ranked)[0].Text;
    }

    private static (string Text, float Confidence) ReadPage(TesseractEngine engine, Pix image, PageSegMode mode)
    {
        using var page = engine.Process(image, mode);
        return (page.GetText()?.Trim() ?? string.Empty, page.GetMeanConfidence());
    }

    private static (string Text, float Confidence) ReadStructured(TesseractEngine engine, Pix image)
    {
        using var page = engine.Process(image, PageSegMode.Auto);
        var words = new List<PdfPageLayoutFormatter.LayoutWord>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            var word = iter.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrWhiteSpace(word)) continue;
            if (IsTableArtifactWord(word)) continue;

            if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
            {
                words.Add(new PdfPageLayoutFormatter.LayoutWord(word, rect.X1, rect.Y1, rect.X2, rect.Y2));
            }
        } while (iter.Next(PageIteratorLevel.Word));

        var text = words.Count == 0
            ? (page.GetText() ?? string.Empty).Trim()
            : PdfPageLayoutFormatter.FormatFromWords(words, pageWidth: words.Max(w => w.Right));

        return (text, page.GetMeanConfidence());
    }

    private static bool IsTableArtifactWord(string word) =>
        word is "|" or "‖" or "¦"
        || (word.Length <= 2 && word.All(c => c is '|' or 'i' or 'l' or '1' or 'I'));

    private static double ScoreText(string text, float confidence) =>
        confidence * 100.0 + PdfTextQualityHeuristics.Score(text) + PdfTextLayoutHeuristics.TableLayoutScore(text) * 20.0;

    public void Dispose()
    {
        while (_enginePool.TryTake(out var engine))
        {
            engine.Dispose();
        }
    }
}
