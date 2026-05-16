namespace DocParseLab.Server.Services;

public sealed class EnterpriseOptions
{
    public const string SectionName = "Enterprise";

    /// <summary>URL для POST при событиях (document.parsed, batch.completed). Пусто — не вызывать.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Заголовок Authorization или X-Webhook-Secret для исходящих вызовов.</summary>
    public string? WebhookSecret { get; set; }

    public bool EnableAudit { get; set; } = true;

    /// <summary>Если задано, для POST parse-batch нужен заголовок X-Enterprise-Batch-Key с тем же значением.</summary>
    public string? BatchApiKey { get; set; }

    /// <summary>Чек-листы: id → список обязательных подстрок/заголовков в тексте.</summary>
    public List<ChecklistDefinitionOptions> Checklists { get; set; } = new();
}

public sealed class ChecklistDefinitionOptions
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> RequiredPhrases { get; set; } = new();
}
