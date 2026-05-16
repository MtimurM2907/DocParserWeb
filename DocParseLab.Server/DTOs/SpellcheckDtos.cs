namespace DocParseLab.Server.DTOs;

public sealed class SpellcheckRequest
{
    public string Text { get; set; } = string.Empty;
    public string Language { get; set; } = "ru_RU";
    public int MaxSuggestions { get; set; } = 5;
    public int MaxMistakes { get; set; } = 200;

    /// <summary>При указании и авторизации: для документов с грифом Confidential орфография выполняется локально (Hunspell), без GigaChat.</summary>
    public int? DocumentId { get; set; }
}

public sealed class SpellcheckMistake
{
    public string Word { get; set; } = string.Empty;
    public int Start { get; set; }
    public int Length { get; set; }
    public List<string> Suggestions { get; set; } = new();
}

public sealed class SpellcheckResponse
{
    public string Language { get; set; } = "ru_RU";
    public int TextLength { get; set; }
    public List<SpellcheckMistake> Mistakes { get; set; } = new();

    /// <summary>gigachat — нейросеть; hunspell — локальный словарь (например, для Confidential).</summary>
    public string SpellcheckEngine { get; set; } = "gigachat";
}
