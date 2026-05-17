using DocParseLab.Server.Hubs;

namespace DocParseLab.Server.Services;

/// <summary>Контекст отчёта о прогрессе парсинга (страница N из M) в SignalR.</summary>
public sealed class DocumentParseProgressScope : IDisposable
{
    private static readonly AsyncLocal<DocumentParseProgressScope?> Current = new();

    private readonly int _userId;
    private readonly IDocumentRealtimeService _realtime;

    private DocumentParseProgressScope(int userId, IDocumentRealtimeService realtime)
    {
        _userId = userId;
        _realtime = realtime;
        Current.Value = this;
    }

    public static IDisposable? TryBegin(int? userId, IDocumentRealtimeService? realtime)
    {
        if (userId is not int uid || realtime == null) return null;
        return new DocumentParseProgressScope(uid, realtime);
    }

    public static async Task ReportPageAsync(int page, int totalPages, CancellationToken cancellationToken = default)
    {
        var scope = Current.Value;
        if (scope == null || totalPages <= 0) return;

        page = Math.Clamp(page, 1, totalPages);
        var percent = 34 + (int)Math.Round((page / (double)totalPages) * 58.0);
        var message = totalPages == 1
            ? "Обработка документа…"
            : $"Страница {page} из {totalPages}…";

        await scope._realtime.PushParseProgressAsync(
            scope._userId,
            new { page, totalPages, percent, message },
            cancellationToken);
    }

    public void Dispose() => Current.Value = null;
}
