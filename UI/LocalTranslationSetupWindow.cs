using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator;

public sealed class LocalTranslationSetupWindow : Window
{
    private readonly Settings _settings;
    private readonly TextBox _serverPath;
    private readonly TextBox _modelPath;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly StackPanel _editable = new();
    private CancellationTokenSource? _operation;

    public LocalTranslationSetupWindow(Settings settings)
    {
        _settings = settings;
        Title = "Локальный перевод Hy-MT2";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Hy-MT2-1.8B Q4 переводит английский и русский локально. Модель и движок скачиваются один раз (около 1,15 ГБ). Расчёты выполняются на процессоре. Первый перевод включает загрузку модели в память.",
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });
        root.Children.Add(_editable);
        _serverPath = new TextBox { Text = LocalTranslationFiles.ServerPath(settings), Padding = new Thickness(5) };
        _modelPath = new TextBox { Text = LocalTranslationFiles.ModelPath(settings), Padding = new Thickness(5) };
        _progress = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 0, 8) };
        _status = new TextBlock
        {
            Text = LocalTranslationFiles.IsInstalled(settings) ? "Файлы найдены. Можно проверить перевод." : "Нажмите «Скачать модель и движок» или укажите уже скачанные файлы.",
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap
        };
        AddPathRow("Движок llama-server.exe", _serverPath, "llama-server.exe|llama-server.exe|Программы (*.exe)|*.exe");
        AddPathRow("Модель Hy-MT2-1.8B Q4", _modelPath, "Модель GGUF (*.gguf)|*.gguf");

        var download = new Button { Content = "Скачать модель и движок", Padding = new Thickness(8), Margin = new Thickness(0, 2, 0, 8) };
        download.Click += async (_, _) => await RunOperationAsync(async token =>
        {
            _status.Text = "Подготовка установки…";
            await TranslationService.Instance.Local.StopAsync(token);
            token.ThrowIfCancellationRequested();
            var progress = new Progress<LocalInstallProgress>(p =>
            {
                if (_operation is null) return;
                _status.Text = p.Message;
                _progress.Value = p.Percent;
            });
            await LocalModelInstaller.InstallAsync(progress, token);
            _serverPath.Text = LocalTranslationFiles.DefaultServerPath;
            _modelPath.Text = LocalTranslationFiles.DefaultModelPath;
            _status.Text = "Установка завершена. Нажмите «Сохранить» или проверьте перевод.";
        });
        _editable.Children.Add(download);
        var check = new Button { Content = "Проверить перевод", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8) };
        check.Click += async (_, _) => await RunOperationAsync(async token =>
        {
            var candidate = Candidate();
            _status.Text = "Загружаю модель и перевожу пример…";
            _progress.IsIndeterminate = true;
            var watch = Stopwatch.StartNew();
            var result = await TranslationService.Instance.Local.TranslateWithSettingsAsync(
                "We should leave before the guards return.", "EN", "RU", candidate, token);
            _status.Text = $"Перевод за {watch.Elapsed.TotalSeconds:F1} с: {result}";
        });
        _editable.Children.Add(check);
        root.Children.Add(_progress);
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var save = new Button { Content = "Сохранить", Padding = new Thickness(8, 5, 8, 5), MinWidth = 90 };
        save.Click += (_, _) =>
        {
            if (_operation is not null) return;
            var previousServer = _settings.LocalServerPath;
            var previousModel = _settings.LocalModelPath;
            try
            {
                var candidate = Candidate();
                _settings.LocalServerPath = candidate.LocalServerPath;
                _settings.LocalModelPath = candidate.LocalModelPath;
                Settings.Save(_settings);
                WordInfoService.ClearCache();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                _settings.LocalServerPath = previousServer;
                _settings.LocalModelPath = previousModel;
                _status.Text = ex.Message;
            }
        };
        var close = new Button { Content = "Закрыть / отменить", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(save);
        buttons.Children.Add(close);
        root.Children.Add(buttons);
        Content = root;
        Closing += (_, e) =>
        {
            if (_operation is null) return;
            e.Cancel = true;
            _operation.Cancel();
            _status.Text = "Отмена операции…";
        };
    }

    private Settings Candidate()
    {
        var server = Path.GetFullPath(_serverPath.Text.Trim());
        var model = Path.GetFullPath(_modelPath.Text.Trim());
        if (!File.Exists(server)) throw new FileNotFoundException("Не найден llama-server.exe. Скачайте движок или укажите его путь.");
        if (!File.Exists(model)) throw new FileNotFoundException("Не найдена модель GGUF. Скачайте модель или укажите её путь.");
        return new Settings { TranslationProvider = "local", LocalServerPath = server, LocalModelPath = model };
    }

    private void AddPathRow(string label, TextBox box, string filter)
    {
        _editable.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var browse = new Button { Content = "Обзор…", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = filter };
            if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName;
        };
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(box);
        _editable.Children.Add(row);
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        _editable.IsEnabled = false;
        _progress.IsIndeterminate = false;
        _progress.Value = 0;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { _status.Text = "Операция отменена."; }
        catch (Exception ex) { _status.Text = "Ошибка: " + ex.Message; }
        finally
        {
            _operation = null;
            _editable.IsEnabled = true;
            _progress.IsIndeterminate = false;
        }
    }
}
