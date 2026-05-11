using DocParseLab.Server.DTOs;

namespace DocParseLab.Server.Services;

public interface ISpellcheckService
{
    Task<SpellcheckResponse> CheckAsync(SpellcheckRequest request, CancellationToken cancellationToken = default);
}

