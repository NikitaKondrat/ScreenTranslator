using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ScreenTranslator;

public class HotkeySettings
{
    public string Key { get; set; } = "Z";
    public bool Ctrl { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }

    public string Format()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}

public class Settings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string DeepLApiKey { get; set; } = "";
    public string TranslationProvider { get; set; } = "deepl";
    public string LocalServerPath { get; set; } = "";
    public string LocalModelPath { get; set; } = "";
    public HotkeySettings RegionHotkey { get; set; } = new() { Key = "Z" };
    public HotkeySettings ScreenHotkey { get; set; } = new() { Key = "X" };
    public HotkeySettings MenuHotkey { get; set; } = new() { Key = "M" };

    public string OcrLanguage { get; set; } = "en-US";

    public double OverlayFontSize { get; set; } = 16;


    public double OverlayTransparencyPercent { get; set; } = 0;

    public string TesseractExePath { get; set; } = "";
    public string TesseractDataPath { get; set; } = "";

    public string OcrEngine { get; set; } = "windows";

    internal static string SettingsPath => UserData.PathFor("settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                Save(new Settings());
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), ReadOptions) ?? new Settings();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Не удалось прочитать настройки: " + ex.Message);
            return new Settings();
        }
    }

    public static void Save(Settings s)
    {
        if (File.Exists(SettingsPath))
            _ = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), ReadOptions)
                ?? throw new InvalidDataException("Файл настроек пуст или повреждён.");
        UserData.Write("settings.json",
            JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}
