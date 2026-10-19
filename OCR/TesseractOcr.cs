using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ScreenTranslator;

public static class TesseractOcr
{
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        var settings = Settings.Load();
        var tessdata = ResolveTessdata(settings);
        if (tessdata is null)
            throw new InvalidOperationException(
                "Не найден каталог языковых моделей Tesseract. Открой меню → «Настроить Tesseract…» и укажи каталог с файлами .traineddata.");

        var available = GetAvailableLanguages(settings);
        var language = ResolveLanguage(settings.OcrLanguage, available);
        var missing = settings.OcrLanguage switch
        {
            "ru-RU" when !available.Contains("rus") => "Не найдена русская модель Tesseract (rus.traineddata). Укажи каталог модели в настройках Tesseract.",
            "en-US" when !available.Contains("eng") => "Не найдена английская модель Tesseract (eng.traineddata). Укажи каталог модели в настройках Tesseract.",
            _ when string.IsNullOrEmpty(language) => "Не найдены модели Tesseract для английского или русского языка. Добавь eng.traineddata и/или rus.traineddata в каталоге моделей.",
            _ => null
        };
        if (missing is not null) throw new InvalidOperationException(missing);

        var imagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        bitmap.Save(imagePath, ImageFormat.Png);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveExecutable(settings),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(imagePath);
            startInfo.ArgumentList.Add("stdout");
            startInfo.ArgumentList.Add("--tessdata-dir");
            startInfo.ArgumentList.Add(tessdata);
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add(language);
            startInfo.ArgumentList.Add("--psm");
            startInfo.ArgumentList.Add("3");

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось запустить Tesseract. Укажи tesseract.exe в меню «Настроить Tesseract…».", ex);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Tesseract завершился с кодом {process.ExitCode}: {error.Trim()}");
            return output.Trim();
        }
        finally
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
        }
    }

    public static List<string> GetAvailableLanguages(Settings settings)
    {
        var dir = ResolveTessdata(settings);
        if (dir is null) return new List<string>();
        try
        {
            return Directory.EnumerateFiles(dir, "*.traineddata", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException) { return new List<string>(); }
        catch (UnauthorizedAccessException) { return new List<string>(); }
    }

    public static bool TryResolveExecutable(Settings settings, out string executable)
    {
        try
        {
            executable = ResolveExecutable(settings);
            if (File.Exists(executable)) return true;

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                if (File.Exists(Path.Combine(folder.Trim(), "tesseract.exe"))) return true;
        }
        catch (Exception) { }
        executable = "";
        return false;
    }

    public static string GetLanguageStatus(Settings settings)
    {
        var models = GetAvailableLanguages(settings);
        bool eng = models.Contains("eng"), rus = models.Contains("rus");
        var exeFound = TryResolveExecutable(settings, out _);
        var availability = settings.OcrLanguage switch
        {
            "en-US" => eng ? "Английская модель найдена." : "Не найдена eng.traineddata.",
            "ru-RU" => rus ? "Русская модель найдена." : "Не найдена rus.traineddata.",
            _ => eng && rus ? "Найдены английская и русская модели; авто использует обе." :
                 eng ? "Найдена только английская модель; авто распознаёт английский." :
                 rus ? "Найдена только русская модель; авто распознаёт русский." : "Модели eng.traineddata и rus.traineddata не найдены."
        };
        return (exeFound ? "Tesseract найден. " : "Не найден tesseract.exe. ") + availability;
    }

    private static string ResolveLanguage(string preference, IReadOnlyCollection<string> available)
    {
        if (preference == "ru-RU") return available.Contains("rus") ? "rus" : "";
        if (preference == "en-US") return available.Contains("eng") ? "eng" : "";
        if (available.Contains("eng") && available.Contains("rus")) return "eng+rus";
        if (available.Contains("eng")) return "eng";
        if (available.Contains("rus")) return "rus";
        return "";
    }

    private static string? ResolveTessdata(Settings settings)
    {
        var configured = settings.TesseractDataPath;
        var executable = ResolveExecutable(settings);
        var exeDir = Path.IsPathRooted(executable) ? Path.GetDirectoryName(executable) : null;
        var env = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "tessdata"));
        if (!string.IsNullOrWhiteSpace(exeDir)) candidates.Add(Path.Combine(exeDir, "tessdata"));
        if (!string.IsNullOrWhiteSpace(env))
        {
            candidates.Add(env);
            candidates.Add(Path.Combine(env, "tessdata"));
        }
        return candidates.FirstOrDefault(path => Directory.Exists(path) &&
            (File.Exists(Path.Combine(path, "eng.traineddata")) || File.Exists(Path.Combine(path, "rus.traineddata"))));
    }

    private static string ResolveExecutable(Settings settings)
    {
        var configured = settings.TesseractExePath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var environmentPath = Environment.GetEnvironmentVariable("TESSERACT_EXE");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath)) return environmentPath;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tesseract", "tesseract.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tesseract-OCR", "tesseract.exe")
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;
        return "tesseract";
    }
}
