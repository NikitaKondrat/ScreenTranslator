using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator;

public class DeepLTranslator
{
    private static readonly HttpClient Client = new();

    private static string ApiKey
    {
        get
        {
            var key = Environment.GetEnvironmentVariable("DEEPL_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
                key = Settings.Load().DeepLApiKey;
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException(
                    $"Не найден ключ DeepL. Проверь поле DeepLApiKey в файле {Settings.SettingsPath} " +
                    "или переменную среды DEEPL_API_KEY.");
            return key.Trim();
        }
    }

    private static string Url => ApiKey.EndsWith(":fx")
        ? "https://api-free.deepl.com/v2/translate"
        : "https://api.deepl.com/v2/translate";

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default) =>
        (await TranslateBatchAsync(new[] { text }, sourceLang, targetLang, cancellationToken))[0];

    // несколько текстов одним запросом (DeepL поддерживает повтор text=)
    public async Task<List<string>> TranslateBatchAsync(
        IEnumerable<string> texts, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var t in texts) pairs.Add(new("text", t));
        pairs.Add(new("source_lang", sourceLang));
        pairs.Add(new("target_lang", targetLang));

        using var content = new FormUrlEncodedContent(pairs);
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {ApiKey}");

        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DeepL {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var list = new List<string>();
        foreach (var tr in doc.RootElement.GetProperty("translations").EnumerateArray())
            list.Add(tr.GetProperty("text").GetString() ?? "");
        return list;
    }
}
