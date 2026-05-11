using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DocParseLab.Server.Services;

public sealed class PdfTextExtractor : IDocumentTextExtractor
{
    private readonly IOcrService _ocr;
    private readonly OcrOptions _options;
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(IOcrService ocr, IOptions<OcrOptions> options, ILogger<PdfTextExtractor> logger)
    {
        _ocr = ocr;
        _options = options.Value;
        _logger = logger;
    }

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        stream.Position = 0;
        using var pdf = PdfDocument.Open(stream);

        var pageTexts = new List<string>();
        var pages = pdf.GetPages().OrderBy(p => p.Number).ToList();

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var t = page.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(t))
            {
                pageTexts.Add(t);
                continue;
            }

            if (!_options.Enabled)
            {
                continue;
            }

            // OCR fallback for scanned pages: extract images and OCR them.
            var images = page.GetImages().Take(Math.Max(1, _options.MaxImagesPerPage)).ToList();
            if (images.Count == 0)
            {
                continue;
            }

            var ocrParts = new List<string>();
            foreach (var img in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGetImageBytes(img, out var imageBytes))
                {
                    _logger.LogDebug("Skipping PDF image: cannot decode to PNG or supported encoded bytes.");
                    continue;
                }

                var ocrText = await _ocr.OcrPngAsync(imageBytes, cancellationToken);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    ocrParts.Add(ocrText);
                }
            }

            if (ocrParts.Count > 0)
            {
                pageTexts.Add(string.Join(Environment.NewLine, ocrParts));
            }
        }

        var text = pageTexts.Count == 0 ? string.Empty : string.Join(Environment.NewLine + Environment.NewLine, pageTexts);

        // Если в документе есть немного текста, но его подозрительно мало — попробуем OCR по всему документу.
        if (_options.Enabled && text.Trim().Length < _options.MinTextCharsToSkipOcr)
        {
            _logger.LogInformation("PDF has low text ({Chars}), OCR fallback activated.", text.Trim().Length);
        }

        return text;
    }

    private static bool TryGetImageBytes(IPdfImage image, out byte[] imageBytes)
    {
        imageBytes = Array.Empty<byte>();

        try
        {
            if (image.TryGetPng(out var pngBytes) && pngBytes != null && pngBytes.Length > 0)
            {
                imageBytes = pngBytes;
                return true;
            }

            if (image.TryGetBytes(out var decodedBytes) && decodedBytes is { Count: > 0 })
            {
                // Tesseract can read common encoded image formats (JPEG/PNG/TIFF/BMP/GIF/WebP).
                // For raw decoded raster without container headers this will be false and we fallback below.
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
            // ignore image decode errors
        }

        return false;
    }

    private static bool IsSupportedEncodedImage(byte[] bytes)
    {
        if (bytes.Length < 4) return false;

        // PNG
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 &&
            bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return true;
        }

        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return true;

        // GIF
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;

        // BMP
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;

        // TIFF (II* / MM*)
        if ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
            (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A))
        {
            return true;
        }

        // WEBP: RIFF....WEBP
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 &&
            bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 &&
            bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return true;
        }

        return false;
    }
}

