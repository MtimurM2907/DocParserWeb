using WeCantSpell.Hunspell;

namespace DocParseLab.Server.Services;

/// <summary>Общий словарь ru_RU для Hunspell (проверка орфографии и правка OCR).</summary>
public sealed class RussianHunspellDictionary
{
    private readonly Lazy<WordList> _wordList;

    public RussianHunspellDictionary(IWebHostEnvironment env, ILogger<RussianHunspellDictionary> logger)
    {
        _wordList = new Lazy<WordList>(() => Load(env, logger));
    }

    public WordList WordList => _wordList.Value;

    private static WordList Load(IWebHostEnvironment env, ILogger logger)
    {
        var affPath = Path.Combine(env.ContentRootPath, "Resources", "Hunspell", "ru_RU", "ru_RU.aff");
        var dicPath = Path.Combine(env.ContentRootPath, "Resources", "Hunspell", "ru_RU", "ru_RU.dic");

        if (!File.Exists(affPath) || !File.Exists(dicPath))
        {
            throw new FileNotFoundException(
                $"Не найдены файлы словаря Hunspell: '{affPath}' и/или '{dicPath}'.");
        }

        var wl = WordList.CreateFromFiles(dicPath, affPath);
        logger.LogInformation("Словарь Hunspell ru_RU загружен для OCR/орфографии.");
        return wl;
    }
}
