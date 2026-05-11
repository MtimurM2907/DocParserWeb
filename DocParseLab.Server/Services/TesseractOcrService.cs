using Microsoft.Extensions.Options;
using Tesseract;

namespace DocParseLab.Server.Services;

public sealed class TesseractOcrService : IOcrService, IDisposable
{
    private readonly OcrOptions _options;
    private readonly ILogger<TesseractOcrService> _logger;
    private readonly object _lock = new();
    private TesseractEngine? _engine;

    public TesseractOcrService(IOptions<OcrOptions> options, ILogger<TesseractOcrService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<string> OcrPngAsync(byte[] pngBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pngBytes.Length == 0) return Task.FromResult(string.Empty);

        try
        {
            lock (_lock)
            {
                _engine ??= CreateEngine();
                using var image = Pix.LoadFromMemory(pngBytes);
                var bestText = RunBestRotationOcr(_engine, image);
                return Task.FromResult(bestText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR failed for image ({Bytes} bytes)", pngBytes.Length);
            return Task.FromResult(string.Empty);
        }
    }

    private TesseractEngine CreateEngine()
    {
        var path = ResolveTessdataPath(_options.TessdataPath);

        _logger.LogInformation("Initializing Tesseract OCR. tessdata={Path}, lang={Lang}", path, _options.Languages);
        return new TesseractEngine(path, _options.Languages, EngineMode.Default);
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

    private static string RunBestRotationOcr(TesseractEngine engine, Pix source)
    {
        var candidates = new List<(string Text, float Confidence, int Rotation)>
        {
            Process(engine, source, rotation: 0),
        };

        using var rot90 = source.Rotate90(1);
        candidates.Add(Process(engine, rot90, rotation: 90));

        using var rot180 = rot90.Rotate90(1);
        candidates.Add(Process(engine, rot180, rotation: 180));

        using var rot270 = rot180.Rotate90(1);
        candidates.Add(Process(engine, rot270, rotation: 270));

        var winner = candidates
            .OrderByDescending(c => Score(c.Text, c.Confidence))
            .First();

        return winner.Text;
    }

    private static (string Text, float Confidence, int Rotation) Process(TesseractEngine engine, Pix image, int rotation)
    {
        using var page = engine.Process(image, PageSegMode.Auto);
        var text = (page.GetText() ?? string.Empty).Trim();
        var confidence = page.GetMeanConfidence();
        return (text, confidence, rotation);
    }

    private static double Score(string text, float confidence)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NegativeInfinity;

        var lettersOrDigits = text.Count(char.IsLetterOrDigit);
        var density = lettersOrDigits / Math.Max(1.0, text.Length);
        var lengthBoost = Math.Min(1.0, text.Length / 1200.0);

        return confidence * 100.0 + density * 35.0 + lengthBoost * 10.0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
