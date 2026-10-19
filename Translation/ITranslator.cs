using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator;

public interface ITranslator
{
    Task<string> TranslateAsync(string text, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default);

    Task<List<string>> TranslateBatchAsync(IEnumerable<string> texts, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default);
}
