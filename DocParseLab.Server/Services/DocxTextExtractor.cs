using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DocParseLab.Server.Services;

public sealed class DocxTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string extension) =>
        string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        stream.Position = 0;
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null)
        {
            return Task.FromResult(string.Empty);
        }

        var paragraphs = body.Elements<Paragraph>()
            .Select(p => p.InnerText?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return Task.FromResult(string.Join(Environment.NewLine, paragraphs));
    }
}

