using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Drawing;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace ScreenTranslator;

public static class WindowsOcr
{
    private static OcrEngine? _engine;
    private static string _engineLang = "";

    private static OcrEngine? GetEngine(string langTag)
    {
        if (_engine is not null && _engineLang == langTag) return _engine;

        _engine = langTag == "auto"
            ? OcrEngine.TryCreateFromUserProfileLanguages()
            : OcrEngine.TryCreateFromLanguage(new Language(langTag));
        _engineLang = langTag;
        return _engine;
    }

    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        var settings = Settings.Load();

        if (settings.OcrEngine == "paddle")
        {
            var paddleText = await PaddleOcr.RecognizeAsync(bitmap);
            return ShouldCorrectEnglish(paddleText, settings.OcrLanguage)
                ? await TextCorrector.CorrectAsync(paddleText)
                : paddleText;
        }

        if (settings.OcrEngine == "tesseract")
        {
            var text = await TesseractOcr.RecognizeAsync(bitmap);
            return ShouldCorrectEnglish(text, settings.OcrLanguage)
                ? await TextCorrector.CorrectAsync(text)
                : text;
        }

        var engine = GetEngine(settings.OcrLanguage)
            ?? throw new InvalidOperationException(
                "Нет OCR-движка для языка '" + settings.OcrLanguage +
                "'. Установи языковой компонент в Windows (Параметры → Время и язык → языки → компонент 'Optical character recognition').");

        using var normal = ImagePreprocessor.PrepareForOcr(bitmap);
        using var enhanced = ImagePreprocessor.PrepareForOcrEnhanced(bitmap, true);

        var textNormal = await RecognizeBitmapAsync(normal, engine);
        var textEnhanced = await RecognizeBitmapAsync(enhanced, engine);

        var best = Score(textEnhanced) > Score(textNormal) ? textEnhanced : textNormal;

        return ShouldCorrectEnglish(best, settings.OcrLanguage)
            ? await TextCorrector.CorrectAsync(best)
            : best;
    }

    public static string GetLanguageStatus(Settings settings)
    {
        if (settings.OcrEngine == "tesseract")
            return TesseractOcr.GetLanguageStatus(settings);
        if (settings.OcrEngine == "paddle")
            return "PaddleOCR не использует языковые модели Windows или Tesseract.";

        if (settings.OcrLanguage == "auto")
            return OcrEngine.TryCreateFromUserProfileLanguages() is not null
                ? "Используются языки распознавания, установленные в Windows."
                : "В Windows не найден языковой компонент OCR. Установите его в параметрах языка.";

        var tag = settings.OcrLanguage;
        return OcrEngine.TryCreateFromLanguage(new Language(tag)) is not null
            ? $"Компонент { (tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "русского" : "английского") } языка найден в Windows."
            : $"В Windows не найден компонент { (tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "русского" : "английского") } OCR. Установите его в параметрах языка.";
    }

    private static bool ShouldCorrectEnglish(string text, string language)
    {
        if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return true;
        if (!language.Equals("auto", StringComparison.OrdinalIgnoreCase)) return false;

        int latin = 0, cyrillic = 0;
        foreach (char c in text)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) latin++;
            else if ((c >= 'А' && c <= 'я') || c == 'Ё' || c == 'ё') cyrillic++;
        }
        return latin > cyrillic;
    }

    private static async Task<string> RecognizeBitmapAsync(Bitmap prepared, OcrEngine engine)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        prepared.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(tempPath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var raw = await decoder.GetSoftwareBitmapAsync();
            using var bmp = SoftwareBitmap.Convert(
                raw, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bmp);
            return BuildText(result);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
    
    private static string BuildText(OcrResult result)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in result.Lines)
        {
            var words = line.Words;
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(words[i].Text);
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static double Score(string text)
    {
        var tokens = Regex.Matches(text, @"\S+");
        if (tokens.Count == 0) return 0;

        double good = 0;
        foreach (Match t in tokens)
        {
            var s = t.Value;
            if (Regex.IsMatch(s, @"^[A-Za-z][A-Za-z'-]+$")) good += 1.0;
            else if (Regex.IsMatch(s, @"^[A-Za-z0-9]+$")) good += 0.5;
        }
        return good / tokens.Count + Math.Min(tokens.Count, 50) * 0.01;
    }
}
