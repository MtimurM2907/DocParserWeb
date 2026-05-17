namespace DocParseLab.Server.Services;

public interface IOcrService
{
    Task<string> OcrPngAsync(byte[] pngBytes, CancellationToken cancellationToken = default);

    /// <summary>OCR с сохранением строк и колонок (таблицы через табуляцию).</summary>
    Task<string> OcrPngStructuredAsync(byte[] pngBytes, CancellationToken cancellationToken = default);

    /// <summary>Быстрый OCR полностраничного скана (один проход, без мелких вложений).</summary>
    Task<string> OcrScannedPageAsync(byte[] pngBytes, CancellationToken cancellationToken = default);
}

