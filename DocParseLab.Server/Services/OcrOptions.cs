namespace DocParseLab.Server.Services;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public bool Enabled { get; set; } = true;

    /// <summary>Языки Tesseract, например: "rus+eng".</summary>
    public string Languages { get; set; } = "rus+eng";

    /// <summary>Папка с tessdata (внутри должны быть *.traineddata).</summary>
    public string TessdataPath { get; set; } = "Resources/Tessdata";

    /// <summary>Если извлечённого текста меньше порога — включаем OCR.</summary>
    public int MinTextCharsToSkipOcr { get; set; } = 80;

    /// <summary>Максимум изображений на страницу для OCR (защита от тяжёлых PDF).</summary>
    public int MaxImagesPerPage { get; set; } = 3;
}

