using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ScreenTranslator;

public class DictEntry
{
    public string Word { get; set; } = "";
    public List<string> Meanings { get; set; } = new();
    public string Definition { get; set; } = "";
    public string ExampleEn { get; set; } = "";
    public string ExampleRu { get; set; } = "";
    public string AddedAt { get; set; } = "";
    // легаси
    public string Translation { get; set; } = "";
}

public static class DictionaryStore
{
    private static string StorePath => UserData.PathFor("dictionary.json");

    public static List<DictEntry> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new List<DictEntry>();
            var list = JsonSerializer.Deserialize<List<DictEntry>>(File.ReadAllText(StorePath))
                       ?? new List<DictEntry>();

            foreach (var e in list)
            {
                e.Word = e.Word.Trim().ToLowerInvariant();
                if (e.Meanings is null) e.Meanings = new List<string>();
                if (e.Meanings.Count == 0 && !string.IsNullOrWhiteSpace(e.Translation))
                    e.Meanings.Add(e.Translation);
                e.Meanings = e.Meanings
                    .Select(m => m.Trim().ToLowerInvariant())
                    .Where(m => m.Length > 0)
                    .Distinct()
                    .ToList();
            }
            return list;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Не удалось прочитать словарь. Исходный файл сохранён. " + ex.Message);
            return new List<DictEntry>();
        }
    }

    public static void Save(List<DictEntry> list)
    {
        // Do not overwrite an unreadable dictionary with an empty list.
        if (File.Exists(StorePath))
            _ = JsonSerializer.Deserialize<List<DictEntry>>(File.ReadAllText(StorePath))
                ?? throw new InvalidDataException("Файл словаря пуст или повреждён.");
        foreach (var e in list)
            e.Translation = string.Join(", ", e.Meanings);
        UserData.Write("dictionary.json",
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    // сохранение богатой карточки; если слово есть — дополняем
    public static void AddRich(DictEntry entry)
    {
        var list = Load();
        var existing = list.FirstOrDefault(e => e.Word == entry.Word);
        if (existing is not null)
        {
            foreach (var m in entry.Meanings)
                if (!existing.Meanings.Contains(m)) existing.Meanings.Add(m);
            if (string.IsNullOrWhiteSpace(existing.Definition)) existing.Definition = entry.Definition;
            if (string.IsNullOrWhiteSpace(existing.ExampleEn)) existing.ExampleEn = entry.ExampleEn;
            if (string.IsNullOrWhiteSpace(existing.ExampleRu)) existing.ExampleRu = entry.ExampleRu;
        }
        else
        {
            entry.AddedAt = DateTime.Now.ToString("dd.MM.yyyy");
            list.Insert(0, entry);
        }
        Save(list);
    }

    public static bool TryAdd(string word, string translation)
    {
        word = word.Trim().ToLowerInvariant();
        var meaning = translation.Trim().ToLowerInvariant();
        var list = Load();
        var existing = list.FirstOrDefault(e => e.Word == word);
        if (existing is not null)
        {
            if (!existing.Meanings.Contains(meaning)) existing.Meanings.Add(meaning);
            Save(list);
            return true;
        }
        list.Insert(0, new DictEntry
        {
            Word = word,
            Meanings = new List<string> { meaning },
            AddedAt = DateTime.Now.ToString("dd.MM.yyyy")
        });
        Save(list);
        return true;
    }

    public static void AddMeaning(string word, string meaning)
    {
        meaning = meaning.Trim().ToLowerInvariant();
        if (meaning.Length == 0) return;
        var list = Load();
        var e = list.FirstOrDefault(x => x.Word == word);
        if (e is null) return;
        if (!e.Meanings.Contains(meaning)) e.Meanings.Add(meaning);
        Save(list);
    }

    public static void Remove(string word)
    {
        var list = Load();
        list.RemoveAll(e => e.Word == word);
        Save(list);
    }

    public static string? FindTranslation(string word) =>
        Load().FirstOrDefault(e => e.Word == word)?.Meanings.FirstOrDefault();
}
