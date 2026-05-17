using System.Text;

namespace DocParseLab.Server.Services;

public class GigaChatOptions
{
    public const string SectionName = "GigaChat";

    public string AuthUrl { get; set; } = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";

    public string ApiUrl { get; set; } = "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";

    /// <summary>Готовый заголовок: Basic &lt;base64&gt;. Альтернатива — ClientId + ClientSecret.</summary>
    public string Authorization { get; set; } = string.Empty;

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string Scope { get; set; } = "GIGACHAT_API_PERS";

    public string Model { get; set; } = "GigaChat";
}

public static class GigaChatOptionsExtensions
{
    public static bool IsConfigured(this GigaChatOptions options)
    {
        return !string.IsNullOrWhiteSpace(ResolveAuthorization(options));
    }

    public static string ResolveAuthorization(this GigaChatOptions options)
    {
        var auth = options.Authorization?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(auth) && !IsPlaceholderCredential(auth))
        {
            return auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ? auth : $"Basic {auth}";
        }

        var id = options.ClientId?.Trim();
        var secret = options.ClientSecret?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret)
            || IsPlaceholderCredential(id) || IsPlaceholderCredential(secret))
        {
            return string.Empty;
        }

        var raw = $"{id}:{secret}";
        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static bool IsPlaceholderCredential(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
               || value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
               || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
               || value.Contains("ВСТАВЬТЕ", StringComparison.OrdinalIgnoreCase)
               || value.Contains("example.com", StringComparison.OrdinalIgnoreCase);
    }
}
