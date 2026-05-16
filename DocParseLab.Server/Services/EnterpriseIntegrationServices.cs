using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Data;
using DocParseLab.Server.Models;
using Microsoft.Extensions.Options;

namespace DocParseLab.Server.Services;

public interface IAuditService
{
    Task LogAsync(string action, string? resource, string? details, CancellationToken cancellationToken = default);
}

public sealed class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly IOptions<EnterpriseOptions> _options;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        AppDbContext db,
        IHttpContextAccessor http,
        IOptions<EnterpriseOptions> options,
        ILogger<AuditService> logger)
    {
        _db = db;
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task LogAsync(string action, string? resource, string? details, CancellationToken cancellationToken = default)
    {
        if (!_options.Value.EnableAudit)
            return;

        try
        {
            int? userId = null;
            string? email = null;
            var user = _http.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                var sid = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(sid, out var id))
                {
                    userId = id;
                    email = await _db.Users.Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
                }
            }

            var ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

            _db.AuditLogEntries.Add(new AuditLogEntry
            {
                Action = action,
                Resource = resource,
                Details = details,
                UserId = userId,
                UserEmailSnapshot = email,
                IpAddress = ip,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось записать аудит: {Action}", action);
        }
    }
}

public interface IWebhookService
{
    Task NotifyAsync(string eventType, object payload, CancellationToken cancellationToken = default);
}

public sealed class WebhookService : IWebhookService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<EnterpriseOptions> _options;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(IHttpClientFactory httpClientFactory, IOptions<EnterpriseOptions> options, ILogger<WebhookService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyAsync(string eventType, object payload, CancellationToken cancellationToken = default)
    {
        var url = _options.Value.WebhookUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var secret = _options.Value.WebhookSecret;
            if (!string.IsNullOrWhiteSpace(secret))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Webhook-Secret", secret);

            var body = new { @event = eventType, at = DateTime.UtcNow, data = payload };
            var response = await client.PostAsJsonAsync(url, body, cancellationToken);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Webhook {Event} вернул {Code}", eventType, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook {Event} не доставлен", eventType);
        }
    }
}

public interface IChecklistService
{
    ChecklistResult Validate(string text, string checklistId);
}

public sealed class ChecklistResult
{
    public string ChecklistId { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public List<string> Missing { get; set; } = new();
}

public sealed class ChecklistService : IChecklistService
{
    private readonly IOptions<EnterpriseOptions> _options;

    public ChecklistService(IOptions<EnterpriseOptions> options) => _options = options;

    public ChecklistResult Validate(string text, string checklistId)
    {
        var def = _options.Value.Checklists.FirstOrDefault(c =>
            string.Equals(c.Id, checklistId, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            return new ChecklistResult { ChecklistId = checklistId, Ok = false, Missing = new List<string> { "Неизвестный идентификатор чек-листа" } };
        }

        var t = text ?? string.Empty;
        var missing = new List<string>();
        foreach (var phrase in def.RequiredPhrases)
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;
            if (t.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) < 0)
                missing.Add(phrase);
        }

        return new ChecklistResult { ChecklistId = def.Id, Ok = missing.Count == 0, Missing = missing };
    }
}

public interface IEntityExtractionService
{
    ExtractedEntitiesDto Extract(string text);
}

public sealed class ExtractedEntitiesDto
{
    public List<string> Dates { get; set; } = new();
    public List<string> Money { get; set; } = new();
    public List<string> Inn { get; set; } = new();
    public List<string> Emails { get; set; } = new();
}

public sealed class EntityExtractionService : IEntityExtractionService
{
    private static readonly System.Text.RegularExpressions.Regex RxDate =
        new(@"\b(0?[1-9]|[12]\d|3[01])\.(0?[1-9]|1[0-2])\.(\d{2}|\d{4})\b", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RxMoney =
        new(@"\b\d{1,3}(?:\s\d{3})*(?:,\d{2})?\s*(?:руб|₽|р\.)\b|\b\d+[.,]\d{2}\s*(?:руб|₽|р\.)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RxInn =
        new(@"\b(?:ИНН|инн)[\s:]*(\d{10}|\d{12})\b|\b(?<!\d)(\d{10}|\d{12})(?!\d)\b", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RxEmail =
        new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public ExtractedEntitiesDto Extract(string text)
    {
        var t = text ?? string.Empty;
        var dto = new ExtractedEntitiesDto();
        foreach (System.Text.RegularExpressions.Match m in RxDate.Matches(t))
            dto.Dates.Add(m.Value);
        foreach (System.Text.RegularExpressions.Match m in RxMoney.Matches(t))
            dto.Money.Add(m.Value.Trim());
        foreach (System.Text.RegularExpressions.Match m in RxInn.Matches(t))
        {
            var g = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
            if (g.Length is 10 or 12)
                dto.Inn.Add(g);
        }
        dto.Inn = dto.Inn.Distinct().ToList();
        foreach (System.Text.RegularExpressions.Match m in RxEmail.Matches(t))
            dto.Emails.Add(m.Value);
        dto.Dates = dto.Dates.Distinct().Take(50).ToList();
        dto.Money = dto.Money.Distinct().Take(50).ToList();
        dto.Emails = dto.Emails.Distinct().Take(50).ToList();
        return dto;
    }
}
