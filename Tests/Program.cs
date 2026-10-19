using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenTranslator;


internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static async Task<int> Main()
    {
        try
        {
            var settings = new Settings { TranslationProvider = "local" };
            var legacy = JsonSerializer.Deserialize<Settings>("{\"DeepLApiKey\":\"synthetic-key\",\"OcrLanguage\":\"ru-RU\"}")!;
            Check(legacy.TranslationProvider == "deepl" && legacy.DeepLApiKey == "synthetic-key" &&
                  legacy.OcrLanguage == "ru-RU", "Legacy settings changed");
            Check(LocalTranslationFiles.IsInstalled(settings), "Install the default model and runtime first.");

            await LocalModelInstaller.InstallAsync();
            CheckWindowConstruction(settings);
            using var translator = new LocalTranslator();
            Check(await translator.TranslateWithSettingsAsync(" ", "EN", "RU", settings) == "", "Empty input");
            await ExpectAsync<NotSupportedException>(() => translator.TranslateWithSettingsAsync("Hello", "EN", "XX", settings));
            await ExpectAsync<InvalidOperationException>(() => translator.TranslateWithSettingsAsync(new string('a', 30001), "EN", "RU", settings));
            await ExpectAsync<InvalidOperationException>(() => translator.TranslateWithSettingsAsync("Hello", "EN", "RU",
                new Settings { LocalModelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gguf") }));
            Check(!translator.IsRunning, "Missing files must not start a server");
            Console.WriteLine("PASS: empty input, unsupported language, input bound, missing model");

            var watch = Stopwatch.StartNew();
            var russian = await translator.TranslateWithSettingsAsync(
                "Alex: Keep your voice down.\nSam: The guards are right outside.\nAlex: Then we wait until they leave.", "EN", "RU", settings);
            Check(Regex.IsMatch(russian, "[А-Яа-яЁё]"), "No Russian translation");
            Console.WriteLine($"EN->RU cold ({watch.Elapsed.TotalSeconds:F1}s): {russian}");
            Check(translator.IsRunning, "Server should remain loaded");
            watch.Restart();
            var english = await translator.TranslateWithSettingsAsync("Нам нужно уйти до возвращения стражников.", "RU", "EN", settings);
            Check(Regex.IsMatch(english, "[A-Za-z]") && !Regex.IsMatch(english, "[А-Яа-яЁё]"), "No English translation");
            Console.WriteLine($"RU->EN warm ({watch.Elapsed.TotalSeconds:F1}s): {english}");

            var batch = await translator.TranslateBatchWithSettingsAsync(new[] { "Hello!", "Goodbye!" }, "EN", "RU", settings);
            Check(batch.Count == 2 && batch[0] != batch[1] && batch.All(t => Regex.IsMatch(t, "[А-Яа-яЁё]")), "Batch shape/order");
            Console.WriteLine("PASS: batch: " + string.Join(" | ", batch));
            var concurrent = await Task.WhenAll(
                translator.TranslateWithSettingsAsync("The bridge collapsed.", "EN", "RU", settings),
                translator.TranslateWithSettingsAsync("The key is missing.", "EN", "RU", settings));
            Check(concurrent.All(t => Regex.IsMatch(t, "[А-Яа-яЁё]")) && concurrent[0] != concurrent[1], "Concurrent requests mixed results");
            Console.WriteLine("PASS: concurrent requests share one server");
            await ExpectAsync<InvalidOperationException>(() => translator.TranslateWithSettingsAsync(
                string.Concat(Enumerable.Repeat("a ", 6500)), "EN", "RU", settings));
            Check(translator.IsRunning, "Oversized token input should be rejected without losing a healthy server");

            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                await ExpectAsync<OperationCanceledException>(() => translator.TranslateWithSettingsAsync(
                    string.Concat(Enumerable.Repeat("The guards are outside and we must wait for a signal. ", 5)),
                    "EN", "RU", settings, cancellation.Token));
            Check(translator.IsRunning, "A cancelled request should leave the acknowledged idle model loaded");
            var recovered = await translator.TranslateWithSettingsAsync("The gate is locked.", "EN", "RU", settings);
            Check(Regex.IsMatch(recovered, "[А-Яа-яЁё]"), "No recovery after cancellation");
            Console.WriteLine("PASS: cancellation acknowledged, warm model reuse: " + recovered);
            await translator.StopAsync();
            Check(!translator.IsRunning, "Stop did not terminate the owned server");
            Console.WriteLine("ALL LOCAL TRANSLATION CHECKS PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckWindowConstruction(Settings settings)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                var menu = new MainWindow();
                var setup = new LocalTranslationSetupWindow(settings);
                var overlay = new TranslationOverlay();
                Check(menu.Content is not null && setup.Content is not null && overlay.Content is not null,
                    "WPF windows did not load");
                app.Shutdown();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("WPF construction failed", error);
        Console.WriteLine("PASS: legacy settings, installed files, WPF window/resource construction (no desktop windows shown)");
    }
}
