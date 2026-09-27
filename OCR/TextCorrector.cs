using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ScreenTranslator;

public static class TextCorrector
{
    private static readonly (char From, char To)[] DigitMap =
    {
        ('1','l'), ('0','o'), ('5','s'), ('3','e'), ('4','a'),
        ('8','b'), ('7','t'), ('9','g'), ('6','g'), ('2','z')
    };

    private static readonly (string From, string To)[] PatternMap =
    {
        ("rn", "m"), ("vv", "w"), ("cl", "d")
    };

    private static readonly Lazy<HashSet<string>> Words = new(LoadWords);

    // A split needs at least one common boundary word; otherwise names and
    // unusual words from a screenshot are too easy to split incorrectly.
    private static readonly HashSet<string> BoundaryWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "to", "of", "and", "or", "in", "on", "at",
        "for", "from", "with", "by", "as", "is", "are", "was", "were",
        "this", "that", "these", "those"
    };

    public static Task<string> CorrectAsync(string text) =>
        Task.FromResult(Correct(text));

    public static string Correct(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Zero-width spaces are invisible in the overlay but separate words.
        text = text.Replace('\u200B', ' ').Replace('\u2060', ' ');

        // Keep visually similar Cyrillic letters in the token so they can be
        // normalized before checking the English dictionary (e.g. "tоthe").
        var parts = Regex.Split(text, @"([A-Za-z0-9\u0400-\u04FF]+)");
        var sb = new StringBuilder(text.Length);

        foreach (var part in parts)
        {
            var token = NormalizeMixedScript(part);
            sb.Append(Regex.IsMatch(token, @"^[A-Za-z0-9]+$") && token.Length >= 3
                ? CorrectToken(token)
                : part);
        }
        return sb.ToString();
    }

    private static string NormalizeMixedScript(string token)
    {
        int latin = token.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        int cyrillic = token.Count(c => c is >= '\u0400' and <= '\u04FF');
        if (latin < 2 || cyrillic == 0 || cyrillic > 2) return token;

        return new string(token.Select(c => c switch
        {
            'а' => 'a', 'е' => 'e', 'о' => 'o', 'р' => 'p', 'с' => 'c',
            'у' => 'y', 'х' => 'x', 'к' => 'k', 'м' => 'm', 'т' => 't',
            'н' => 'h', 'в' => 'b', 'і' => 'i',
            'А' => 'A', 'Е' => 'E', 'О' => 'O', 'Р' => 'P', 'С' => 'C',
            'У' => 'Y', 'Х' => 'X', 'К' => 'K', 'М' => 'M', 'Т' => 'T',
            'Н' => 'H', 'В' => 'B', 'І' => 'I',
            _ => c
        }).ToArray());
    }

    private static string CorrectToken(string token)
    {
        var words = Words.Value;

        if (words.Contains(token.ToLowerInvariant()))
            return token;

        // PaddleOCR may merge short words too: "tothe", "coastof".
        if (token.Length >= 4 && token.Length <= 32 &&
            Regex.IsMatch(token, @"^[A-Za-z]+$"))
        {
            var split = TrySplitMerged(token, words);
            if (split is not null) return split;
        }

        foreach (var candidate in ConfusionCandidates(token))
        {
            if (words.Contains(candidate.ToLowerInvariant()))
                return candidate;
        }

        int letters = token.Count(char.IsLetter);
        int digits = token.Count(char.IsDigit);
        if (digits > 0 && digits <= 2 && letters >= 4)
        {
            var fixedTok = ApplyDigitMap(token);
            if (fixedTok.All(char.IsLetter) &&
                words.Contains(fixedTok.ToLowerInvariant()))
                return fixedTok;
        }

        return token;
    }

    // Only choose a split when it has a clear lead over other valid splits.
    // The whole-word dictionary check above always takes precedence.
    private static string? TrySplitMerged(string token, HashSet<string> words)
    {
        var s = token.ToLowerInvariant();
        int n = s.Length;
        var candidates = new List<(string[] Parts, int Score)>();

        void Consider(params string[] parts)
        {
            if (parts.Any(p => !words.Contains(p)) ||
                !parts.Any(BoundaryWords.Contains)) return;

            // Prefer common function words and longer content words.
            var score = parts.Sum(p => BoundaryWords.Contains(p) ? 3 :
                p.Length >= 4 ? 2 : -3) - 3 * (parts.Length - 2);
            candidates.Add((parts, score));
        }

        for (int i = 2; i <= n - 2; i++)
        {
            Consider(s[..i], s[i..]);
            if (n < 8) continue;
            for (int j = i + 2; j <= n - 2; j++)
                Consider(s[..i], s[i..j], s[j..]);
        }

        var best = candidates.OrderByDescending(c => c.Score).ToList();
        if (best.Count == 0 || best.Count > 1 &&
            best[0].Score - best[1].Score < 2) return null;

        var result = string.Join(" ", best[0].Parts);
        return token == token.ToUpperInvariant() ? result.ToUpperInvariant() :
            char.IsUpper(token[0]) ? char.ToUpperInvariant(result[0]) + result[1..] : result;
    }

    private static IEnumerable<string> ConfusionCandidates(string token)
    {
        var lower = token.ToLowerInvariant();

        foreach (var (from, to) in DigitMap)
            if (lower.Contains(from))
                yield return lower.Replace(from, to);

        foreach (var (from, to) in PatternMap)
            if (lower.Contains(from))
                yield return lower.Replace(from, to);
    }

    private static string ApplyDigitMap(string s)
    {
        foreach (var (from, to) in DigitMap)
            s = s.Replace(from, to);
        return s;
    }

    private static HashSet<string> LoadWords()
    {
        // Embed the dictionary so it cannot silently disappear from the
        // build output. Keep the external file as a fallback for old builds.
        using var stream = typeof(TextCorrector).Assembly
            .GetManifestResourceStream("ScreenTranslator.words.txt")
            ?? (File.Exists(Path.Combine(AppContext.BaseDirectory, "words.txt"))
                ? File.OpenRead(Path.Combine(AppContext.BaseDirectory, "words.txt"))
                : throw new FileNotFoundException("Словарь OCR words.txt отсутствует."));
        using var reader = new StreamReader(stream);
        var words = new HashSet<string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var word = line.Trim().ToLowerInvariant();
            if (word.Length > 0) words.Add(word);
        }
        if (words.Count == 0)
            throw new InvalidDataException("Словарь OCR words.txt пуст.");
        return words;
    }
}
