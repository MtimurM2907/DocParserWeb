using DocParseLab.Server.Models;

namespace DocParseLab.Server.Services;

public interface IPdfParserService
{
    Task<ParsedDocument> ParseAndSaveAsync(IFormFile file, int? ownerId = null, CancellationToken cancellationToken = default);
}


