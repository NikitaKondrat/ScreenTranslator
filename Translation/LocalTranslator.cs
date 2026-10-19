using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator;

public sealed class LocalTranslator : ITranslator, IDisposable
{
    private const int ContextSize = 8192;
    private const int OutputTokens = 2048;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _state = new();
    private readonly StringBuilder _diagnostics = new();
    private Process? _process;
    private HttpClient? _client;
    private string _configuration = "";

    public bool IsRunning
    {
        get { lock (_state) return _process is not null && !_process.HasExited; }
    }

    public Task<string> TranslateAsync(string text, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default) =>
        TranslateWithSettingsAsync(text, sourceLang, targetLang, Settings.Load(), cancellationToken);

    public Task<List<string>> TranslateBatchAsync(IEnumerable<string> texts, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default) =>
        TranslateBatchWithSettingsAsync(texts, sourceLang, targetLang, Settings.Load(), cancellationToken);

    public async Task<List<string>> TranslateBatchWithSettingsAsync(IEnumerable<string> texts,
        string sourceLang, string targetLang, Settings settings, CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        foreach (var text in texts)
            result.Add(await TranslateWithSettingsAsync(text, sourceLang, targetLang, settings, cancellationToken));
        return result;
    }

    public async Task<string> TranslateWithSettingsAsync(string text, string sourceLang, string targetLang,
        Settings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return "";
        var source = LanguageName(sourceLang);
        var target = LanguageName(targetLang);
        if (source == target) return text;

        if (text.Length > 30000)
            throw new InvalidOperationException("Слишком много текста для одного локального перевода. Выделите меньшую область.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        try
        {
            await _gate.WaitAsync(token);
            try
            {
                var client = await EnsureStartedAsync(settings, token);
                var prompt = $"Translate the following text from {source} into {target}. " +
                             "Note that you should only output the translated result without any additional explanation:\n" + text;
                using (var response = await client.PostAsJsonAsync("tokenize",
                           new { content = prompt, add_special = true }, token))
                {
                    using var document = await ReadResponseAsync(response, token);
                    if (document.RootElement.GetProperty("tokens").GetArrayLength() > ContextSize - OutputTokens - 256)
                        throw new InvalidOperationException("Диалог слишком длинный для одного перевода. Выделите меньшую область.");
                }


                using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
                {
                    Content = JsonContent.Create(new
                    {
                        model = "hy-mt2",
                        messages = new[] { new { role = "user", content = prompt } },
                        stream = true,
                        temperature = 0.7,
                        top_p = 0.6,
                        top_k = 20,
                        repeat_penalty = 1.05,
                        max_tokens = OutputTokens,
                        cache_prompt = false
                    })
                };

                using var translation = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (!translation.IsSuccessStatusCode)
                {
                    using var error = await ReadResponseAsync(translation, token);
                }
                using var reader = new StreamReader(await translation.Content.ReadAsStreamAsync(token));
                var output = new StringBuilder();
                string? finishReason = null;
                bool completed = false;
                while (await reader.ReadLineAsync(token) is { } line)
                {
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var data = line[5..].Trim();
                    if (data == "[DONE]") { completed = true; break; }
                    using var part = JsonDocument.Parse(data);
                    var root = part.RootElement;
                    if (root.TryGetProperty("error", out _))
                        throw new InvalidOperationException("Движок прервал локальный перевод. Повторите запрос с меньшим фрагментом.");
                    if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                    var choice = choices[0];
                    if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                        finishReason = reason.GetString();
                    if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                        output.Append(content.GetString());
                }
                if (finishReason == "length")
                    throw new InvalidOperationException("Локальная модель не успела завершить перевод в пределах лимита. Выделите меньшую область.");
                if (!completed && finishReason != "stop")
                    throw new InvalidOperationException("Связь с локальным движком прервалась до завершения перевода. Повторите запрос.");
                var result = output.ToString().Trim();
                return !string.IsNullOrWhiteSpace(result) ? result :
                    throw new InvalidOperationException("Локальная модель вернула пустой перевод. Попробуйте повторить запрос.");
            }
            catch (OperationCanceledException)
            {

                await FinishCancelledRequestAsync();
                throw;
            }
            catch (HttpRequestException ex)
            {
                StopProcess();
                throw new InvalidOperationException("Не удалось связаться с локальным переводчиком. Повторите перевод или проверьте его настройку.", ex);
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException("Локальный перевод занял больше трёх минут. Попробуйте выделить меньше текста.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { StopProcess(); }
        finally { _gate.Release(); }
    }

    private async Task<HttpClient> EnsureStartedAsync(Settings settings, CancellationToken token)
    {
        var executable = Path.GetFullPath(LocalTranslationFiles.ServerPath(settings));
        var model = Path.GetFullPath(LocalTranslationFiles.ModelPath(settings));
        var configuration = executable + "\n" + model;
        lock (_state)
        {
            if (_process is not null && !_process.HasExited && _configuration == configuration)
                return _client!;
        }
        StopProcess();
        if (!File.Exists(executable) || !File.Exists(model))
            throw new InvalidOperationException("Локальный переводчик не установлен. В меню нажмите «Настроить локальный перевод…» и скачайте модель и движок.");
        using (var stream = File.OpenRead(model))
        {
            var magic = new byte[4];
            if (stream.Read(magic) != 4 || Encoding.ASCII.GetString(magic) != "GGUF")
                throw new InvalidDataException("Выбранный файл не является моделью GGUF. Укажите Hy-MT2-1.8B-Q4_K_M.gguf.");
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var key = Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("LLAMA_ARG_", StringComparison.Ordinal) || k == "LLAMA_API_KEY").ToArray())
            start.Environment.Remove(name);
        start.Environment["LLAMA_API_KEY"] = key;
        foreach (var argument in new[]
        {
            "--model", model, "--alias", "hy-mt2", "--host", "127.0.0.1", "--port", port.ToString(),
            "--ctx-size", ContextSize.ToString(), "--parallel", "1", "--n-gpu-layers", "0",
            "--threads", Math.Clamp(Environment.ProcessorCount / 2, 1, 4).ToString(),
            "--poll", "0", "--prio", "-1", "--no-webui", "--offline",
            "--no-context-shift", "--no-cache-prompt", "--reasoning", "off"
        }) start.ArgumentList.Add(argument);

        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => AppendDiagnostic(e.Data);
        process.ErrorDataReceived += (_, e) => AppendDiagnostic(e.Data);
        var client = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,

            MaxResponseDrainSize = 0,
            ResponseDrainTimeout = TimeSpan.Zero
        })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        lock (_state)
        {
            token.ThrowIfCancellationRequested();
            _process = process;
            _client = client;
            _configuration = configuration;
            lock (_diagnostics) _diagnostics.Clear();
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Win32Exception ex)
            {
                StopProcess();
                throw new InvalidOperationException("Не удалось запустить llama-server.exe. Проверьте путь и установку локального переводчика.", ex);
            }
        }

        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            while (true)
            {
                startup.Token.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    string log;
                    lock (_diagnostics) log = _diagnostics.ToString();
                    throw new InvalidOperationException($"Движок локального перевода завершился (код {process.ExitCode}). Проверьте совместимость модели и движка.\n{log}");
                }
                try
                {
                    using var health = await client.GetAsync("health", startup.Token);
                    if (health.IsSuccessStatusCode) return client;
                }
                catch (HttpRequestException) {   }
                await Task.Delay(250, startup.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            StopProcess();
            throw new TimeoutException("Модель не загрузилась за две минуты. Проверьте свободную память и путь к модели.");
        }
        catch { StopProcess(); throw; }
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        var json = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Локальный переводчик вернул ошибку {(int)response.StatusCode}. Попробуйте выделить меньше текста.");
        return JsonDocument.Parse(json);
    }

    private void AppendDiagnostic(string? line)
    {
        if (line is null) return;
        lock (_diagnostics)
        {
            _diagnostics.AppendLine(line);
            if (_diagnostics.Length > 3000) _diagnostics.Remove(0, _diagnostics.Length - 3000);
        }
    }

    private async Task FinishCancelledRequestAsync()
    {
        if (_lifetime.IsCancellationRequested) { StopProcess(); return; }
        HttpClient? client;
        lock (_state) client = _client;
        if (client is null) return;
        using var acknowledgement = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
        try
        {
            await Task.Delay(50, acknowledgement.Token);
            while (true)
            {
                using var response = await client.GetAsync("slots", acknowledgement.Token);
                if (!response.IsSuccessStatusCode) break;
                using var state = await ReadResponseAsync(response, acknowledgement.Token);
                if (state.RootElement.ValueKind == JsonValueKind.Array && state.RootElement.GetArrayLength() > 0 &&
                    state.RootElement.EnumerateArray().All(slot =>
                        slot.TryGetProperty("is_processing", out var active) && active.ValueKind == JsonValueKind.False))
                    return;
                await Task.Delay(50, acknowledgement.Token);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or ObjectDisposedException) { }

        StopProcess();
    }

    private void StopProcess()
    {
        lock (_state)
        {
            _client?.Dispose();
            _client = null;
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(3000);
                    }
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                _process.Dispose();
                _process = null;
            }
            _configuration = "";
        }
    }

    private static string LanguageName(string language) => language.ToUpperInvariant() switch
    {
        "EN" or "EN-US" or "EN-GB" => "English",
        "RU" or "RU-RU" => "Russian",
        _ => throw new NotSupportedException("Локальный перевод в приложении поддерживает английский и русский языки.")
    };

    public void Dispose()
    {
        _lifetime.Cancel();
        StopProcess();
    }
}
