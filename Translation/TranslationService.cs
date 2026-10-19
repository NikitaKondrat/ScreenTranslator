using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator;


public sealed class TranslationService : ITranslator
{
    public static TranslationService Instance { get; } = new();
    public LocalTranslator Local { get; } = new();
    private readonly DeepLTranslator _deepL = new();

    private TranslationService() { }

    public Task<string> TranslateAsync(string text, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default)
    {
        var settings = Settings.Load();
        return settings.TranslationProvider == "local"
            ? Local.TranslateWithSettingsAsync(text, sourceLang, targetLang, settings, cancellationToken)
            : _deepL.TranslateAsync(text, sourceLang, targetLang, cancellationToken);
    }

    public Task<List<string>> TranslateBatchAsync(IEnumerable<string> texts, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default)
    {
        var settings = Settings.Load();
        return settings.TranslationProvider == "local"
            ? Local.TranslateBatchWithSettingsAsync(texts, sourceLang, targetLang, settings, cancellationToken)
            : _deepL.TranslateBatchAsync(texts, sourceLang, targetLang, cancellationToken);
    }
}
