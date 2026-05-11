namespace DocParseLab.Server.Services;

public interface IOcrService
{
    Task<string> OcrPngAsync(byte[] pngBytes, CancellationToken cancellationToken = default);
}

