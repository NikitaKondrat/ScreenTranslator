using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator;

public class WordCardData
{
    public string Surface = "";
    public string Word = "";
    public List<(string Text, string Pos)> Translations = new();
    public string TranslationsLine = "";
    public string DefinitionRu = "";
    public string ExampleEn = "";
    public string ExampleRu = "";
}

public static class WordInfoService
{
    private static readonly ConcurrentDictionary<string, WordCardData> Cache = new();
    private static readonly ITranslator Translator = TranslationService.Instance;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static void ClearCache() => Cache.Clear();

    public static async Task<WordCardData> GetAsync(
        string surface,
        string? contextEn = null,
        string? contextRu = null,
        Action<WordCardData>? onQuickResult = null,
        CancellationToken cancellationToken = default)
    {
        surface = surface.ToLowerInvariant();
        var lemma = Lemmatizer.Lemmatize(surface);
        var provider = Settings.Load();
        var cacheKey = provider.TranslationProvider + "|" + LocalTranslationFiles.ModelPath(provider) + "|" + lemma;
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            var cachedResult = Copy(cached);
            cachedResult.Surface = surface;
            if (!string.IsNullOrWhiteSpace(contextEn))
                ApplyContext(cachedResult, contextEn, contextRu ?? "", surface);
            else if (!string.IsNullOrWhiteSpace(cachedResult.ExampleEn) &&
                     string.IsNullOrWhiteSpace(cachedResult.ExampleRu))
            {
                try
                {
                    cachedResult.ExampleRu = await Translator.TranslateAsync(
                        cachedResult.ExampleEn, "EN", "RU", cancellationToken);
                    Cache[cacheKey] = Copy(cachedResult);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch { }
            }
            return cachedResult;
        }

        var data = new WordCardData { Surface = surface, Word = lemma };

        if (!string.IsNullOrWhiteSpace(contextEn))
            ApplyContext(data, contextEn, contextRu ?? "", surface);

        var dictionaryTask = provider.TranslationProvider == "local"
            ? Task.FromResult(new DictionaryData())
            : FetchDictionaryAsync(lemma, cancellationToken);
        string quickTranslation;
        try
        {
            quickTranslation = await Translator.TranslateAsync(
                lemma, "EN", "RU", cancellationToken);
            AddTranslation(data, Clean(quickTranslation), "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            AddTranslation(data, lemma, "");
        }

        UpdateTranslationsLine(data);
        onQuickResult?.Invoke(Copy(data));

        var dictionary = await dictionaryTask;
        cancellationToken.ThrowIfCancellationRequested();

        var batch = new List<string>();
        foreach (var pos in dictionary.PosList)
            batch.Add($"{lemma} ({PosRu(pos)})");
        if (dictionary.DefinitionEn is not null)
            batch.Add(dictionary.DefinitionEn);

        try
        {
            if (batch.Count > 0)
            {
                var results = await Translator.TranslateBatchAsync(
                    batch, "EN", "RU", cancellationToken);
                int i = 0;
                if (dictionary.PosList.Count > 0)
                {
                    data.Translations.Clear();
                    foreach (var pos in dictionary.PosList)
                        AddTranslation(data, Clean(results[i++]), PosRu(pos));
                }
                if (dictionary.DefinitionEn is not null)
                    data.DefinitionRu = results[i];
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(contextEn) && dictionary.ExampleEn is not null)
        {
            data.ExampleEn = dictionary.ExampleEn;
            try
            {
                data.ExampleRu = await Translator.TranslateAsync(
                    dictionary.ExampleEn, "EN", "RU", cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }
        }

        UpdateTranslationsLine(data);
        var cacheData = Copy(data);
        if (!string.IsNullOrWhiteSpace(contextEn))
        {
            cacheData.ExampleEn = dictionary.ExampleEn ?? "";
            cacheData.ExampleRu = "";
        }
        Cache[cacheKey] = cacheData;
        return data;
    }

    private sealed class DictionaryData
    {
        public List<string> PosList { get; } = new();
        public string? DefinitionEn { get; set; }
        public string? ExampleEn { get; set; }
    }

    private static async Task<DictionaryData> FetchDictionaryAsync(
        string lemma, CancellationToken cancellationToken)
    {
        var result = new DictionaryData();
        try
        {
            using var response = await Http.GetAsync(
                $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(lemma)}",
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                string? definitionEn = null;
                string? exampleEn = null;
                ParseDict(json, result.PosList, ref definitionEn, ref exampleEn);
                result.DefinitionEn = definitionEn;
                result.ExampleEn = exampleEn;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch { }
        return result;
    }

    private static WordCardData Copy(WordCardData source) => new()
    {
        Surface = source.Surface,
        Word = source.Word,
        Translations = new List<(string Text, string Pos)>(source.Translations),
        TranslationsLine = source.TranslationsLine,
        DefinitionRu = source.DefinitionRu,
        ExampleEn = source.ExampleEn,
        ExampleRu = source.ExampleRu
    };

    private static void ApplyContext(WordCardData data, string contextEn,
        string contextRu, string surface)
    {
        var (en, ru) = ExtractContext(contextEn, contextRu, surface);
        data.ExampleEn = en;
        data.ExampleRu = ru;
    }

    private static void UpdateTranslationsLine(WordCardData data) =>
        data.TranslationsLine = string.Join(", ", data.Translations.Select(t => t.Text));

    private static void AddTranslation(WordCardData data, string text, string pos)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (data.Translations.Any(t => t.Text == text)) return;
        data.Translations.Add((text, pos));
    }

    public static (string En, string Ru) ExtractContext(
        string original, string translated, string word)
    {
        var enSent = SplitSentences(original);
        var ruSent = SplitSentences(translated);

        for (int i = 0; i < enSent.Count; i++)
        {
            if (Regex.IsMatch(enSent[i],
                    $@"\b{Regex.Escape(word)}\w*",
                    RegexOptions.IgnoreCase))
            {
                var ru = enSent.Count == ruSent.Count && i < ruSent.Count
                    ? ruSent[i]
                    : translated;
                return (enSent[i], ru);
            }
        }
        return (original, translated);
    }

    private static List<string> SplitSentences(string text) =>
        Regex.Split(text, @"(?<=[.!?…])\s+|\r?\n+")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private static void ParseDict(string json, List<string> posList,
        ref string? defEn, ref string? exampleEn)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("meanings", out var meanings)) continue;
            foreach (var meaning in meanings.EnumerateArray())
            {
                var pos = meaning.TryGetProperty("partOfSpeech", out var p)
                    ? p.GetString() ?? "" : "";
                if (posList.Contains(pos)) continue;
                if (meaning.TryGetProperty("definitions", out var definitions))
                {
                    foreach (var definition in definitions.EnumerateArray())
                    {
                        if (defEn is null)
                        {
                            defEn = definition.GetProperty("definition").GetString();
                            if (definition.TryGetProperty("example", out var ex))
                                exampleEn = ex.GetString();
                        }
                        break;
                    }
                }
                posList.Add(pos);
                if (posList.Count >= 3) return;
            }
        }
    }

    private static string Clean(string s)
    {
        var idx = s.LastIndexOf(" (", StringComparison.Ordinal);
        return idx > 0 ? s[..idx] : s;
    }

    public static string PosRu(string pos) => pos switch
    {
        "verb" => "глагол",
        "noun" => "существительное",
        "adjective" => "прилагательное",
        "adverb" => "наречие",
        "pronoun" => "местоимение",
        "preposition" => "предлог",
        "conjunction" => "союз",
        "interjection" => "междометие",
        "determiner" => "определитель",
        "numeral" => "числительное",
        _ => pos
    };
}
