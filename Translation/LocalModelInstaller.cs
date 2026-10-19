using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator;

public readonly record struct LocalInstallProgress(string Message, double Percent);

public static class LocalModelInstaller
{

    private const string RuntimeUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11669/llama-b11669-bin-win-cpu-x64.zip";
    private const string RuntimeHash = "37b3efcf962847ca869745fd4ccf7f5e7e8dacfeff9fa3523b4ca8cdc66d632b";
    private const string ModelUrl = "https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/resolve/a0c709d9fac510f2c807aa3af52872340dc37a4a/Hy-MT2-1.8B-Q4_K_M.gguf?download=true";
    private const string ModelHash = "dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699";
    private const long ModelSize = 1133080448;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task InstallAsync(IProgress<LocalInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(LocalTranslationFiles.DirectoryPath);
            if (!File.Exists(LocalTranslationFiles.DefaultServerPath))
            {
                var archive = Path.Combine(LocalTranslationFiles.DirectoryPath, "runtime-" + Guid.NewGuid().ToString("N") + ".zip");
                var staging = Path.Combine(LocalTranslationFiles.DirectoryPath, "runtime-" + Guid.NewGuid().ToString("N"));
                try
                {
                    await DownloadAsync(RuntimeUrl, archive, RuntimeHash, "Загрузка движка", progress, cancellationToken);
                    progress?.Report(new("Распаковка движка…", 100));
                    await Task.Run(() => ZipFile.ExtractToDirectory(archive, staging), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(Path.Combine(staging, "llama-server.exe")))
                        throw new InvalidDataException("В архиве не найден llama-server.exe.");

                    if (Directory.Exists(LocalTranslationFiles.RuntimeDirectory))
                        Directory.Move(LocalTranslationFiles.RuntimeDirectory,
                            LocalTranslationFiles.RuntimeDirectory + ".backup-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(staging, LocalTranslationFiles.RuntimeDirectory);
                }
                finally
                {
                    if (File.Exists(archive)) File.Delete(archive);
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                }
            }

            progress?.Report(new("Проверка модели…", 0));
            var model = LocalTranslationFiles.DefaultModelPath;
            bool valid = File.Exists(model) && new FileInfo(model).Length == ModelSize &&
                         await HasHashAsync(model, ModelHash, cancellationToken);
            if (!valid)
                await DownloadAsync(ModelUrl, model, ModelHash, "Загрузка модели Hy-MT2 Q4", progress, cancellationToken);
            progress?.Report(new("Модель и движок готовы.", 100));
        }
        finally { Gate.Release(); }
    }

    private static async Task DownloadAsync(string url, string destination, string hash, string label,
        IProgress<LocalInstallProgress>? progress, CancellationToken cancellationToken)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ScreenTranslator/1.0");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[131072];
                long received = 0;
                var lastReport = Environment.TickCount64;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    received += count;
                    if (Environment.TickCount64 - lastReport > 150)
                    {
                        progress?.Report(new($"{label}: {received / 1048576} МБ" +
                            (total > 0 ? $" из {total / 1048576} МБ" : ""),
                            total > 0 ? received * 100.0 / total.Value : 0));
                        lastReport = Environment.TickCount64;
                    }
                }
            }
            progress?.Report(new("Проверка контрольной суммы…", 100));
            if (!await HasHashAsync(temporary, hash, cancellationToken))
                throw new InvalidDataException("Контрольная сумма загрузки не совпала. Попробуйте скачать ещё раз.");
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<bool> HasHashAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
