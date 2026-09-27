using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace ScreenTranslator;

/// <summary>
/// Runs the locally installed Tesseract command-line engine against a captured image.
/// </summary>
public static class TesseractOcr
{
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
        var englishData = Path.Combine(tessdata, "eng.traineddata");
        if (!File.Exists(englishData))
            throw new FileNotFoundException(
                $"Не найден файл английской модели Tesseract: {englishData}");

        var imagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        bitmap.Save(imagePath, ImageFormat.Png);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveExecutable(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(imagePath);
            bitmap.Save(Path.Combine(Path.GetTempPath(), "ocr-input.png"), ImageFormat.Png);
            startInfo.ArgumentList.Add("stdout");
            startInfo.ArgumentList.Add("--tessdata-dir");
            startInfo.ArgumentList.Add(tessdata);
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add("eng");
            startInfo.ArgumentList.Add("--psm");
            startInfo.ArgumentList.Add("3"); // automatic page segmentation

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось запустить Tesseract. Установи Tesseract OCR 5 и добавь " +
                    "его папку в PATH либо укажи путь через переменную TESSERACT_EXE.", ex);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Tesseract завершился с кодом {process.ExitCode}: {error.Trim()}");

            return output.Trim();
        }
        finally
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
        }
    }

    private static string ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("TESSERACT_EXE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tesseract", "tesseract.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Tesseract-OCR", "tesseract.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Tesseract-OCR", "tesseract.exe")
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        // Windows resolves this through PATH.
        return "tesseract";
    }
}
