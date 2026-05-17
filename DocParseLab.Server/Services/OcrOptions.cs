namespace DocParseLab.Server.Services;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public bool Enabled { get; set; } = true;

    /// <summary>Языки Tesseract, например: "rus".</summary>
    public string Languages { get; set; } = "rus";

    public string TessdataPath { get; set; } = "Resources/Tessdata";

    public int MinTextCharsToSkipOcr { get; set; } = 80;

    public int MaxImagesPerPage { get; set; } = 3;

    public bool OcrOnSuspiciousTextLayer { get; set; } = true;

    /// <summary>Длинная сторона рендера страницы (Docnet).</summary>
    public int RenderWidth { get; set; } = 2400;

    public int RenderHeight { get; set; } = 3400;

    /// <summary>Параллельная обработка страниц (сканы).</summary>
    public int PageParallelism { get; set; } = 3;

    /// <summary>Минимальная ширина изображения для OCR (отсекает полоски 2×36).</summary>
    public int MinImageWidthForOcr { get; set; } = 600;

    /// <summary>Минимальная высота изображения для OCR.</summary>
    public int MinImageHeightForOcr { get; set; } = 600;

    /// <summary>Для полностью сканированных PDF — только рендер страницы, один проход Tesseract.</summary>
    public bool ScannedPdfRenderOnly { get; set; } = true;

    /// <summary>После OCR — автоправка слов по Hunspell (словарь ru_RU).</summary>
    public bool ApplyHunspellCorrection { get; set; } = true;

    /// <summary>Два прохода Tesseract (подготовка default + light), выбор лучшего.</summary>
    public bool UseDualTesseractPass { get; set; } = false;
}
