namespace DocParseLab.Server.DTOs;

/// <summary>
/// Базовый класс для DTO запросов аутентификации
/// </summary>
public sealed class AuthRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// DTO ответа аутентификации
/// </summary>
public sealed class AuthResponse
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// DTO запроса на предоставление доступа к документу
/// </summary>
public sealed class ShareDocumentRequest
{
    public int DocumentId { get; set; }
    public string TargetEmail { get; set; } = string.Empty;
}

/// <summary>
/// DTO ответа с информацией об ошибке
/// </summary>
public sealed class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed class UpdateDocumentTextRequest
{
    public string Text { get; set; } = string.Empty;
}
