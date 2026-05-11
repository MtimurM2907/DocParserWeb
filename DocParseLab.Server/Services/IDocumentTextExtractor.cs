namespace DocParseLab.Server.Services;

public interface IDocumentTextExtractor
{
    bool CanHandle(string extension);
    Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken = default);
}

