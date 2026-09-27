using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator;

public sealed class TesseractSetupWindow : Window
{
    private readonly Settings _settings;
    private readonly TextBox _exePath;
    private readonly TextBox _dataPath;
    private readonly TextBlock _status;

    public TesseractSetupWindow(Settings settings)
    {
        _settings = settings;
        Title = "Настройка Tesseract OCR";
        Width = 610;
        SizeToContent = SizeToContent.Height;
        MinWidth = 500;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _exePath = new TextBox { Text = settings.TesseractExePath, Padding = new Thickness(5) };
        _dataPath = new TextBox { Text = settings.TesseractDataPath, Padding = new Thickness(5) };
        _status = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Укажите tesseract.exe и каталог с файлами языковых моделей (.traineddata). Для английского нужна eng.traineddata, для русского — rus.traineddata.",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        AddPathRow(root, "Путь к tesseract.exe", _exePath, BrowseExecutable);
        AddPathRow(root, "Каталог языковых моделей", _dataPath, BrowseLanguageModel);
        var download = new Button { Content = "Открыть страницу загрузки Tesseract", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 8, 0, 0) };
        download.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/UB-Mannheim/tesseract/wiki") { UseShellExecute = true });
        root.Children.Add(download);
        root.Children.Add(_status);

        var save = new Button { Content = "Сохранить", MinWidth = 90, Padding = new Thickness(8, 5, 8, 5), IsDefault = true };
        save.Click += (_, _) => SavePaths();
        var close = new Button { Content = "Закрыть", MinWidth = 80, Padding = new Thickness(8, 5, 8, 5), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(close);
        root.Children.Add(buttons);
        Content = root;
        RefreshStatus();
    }

    private static void AddPathRow(Panel parent, string label, TextBox box, Action browse)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var button = new Button { Content = "Обзор…", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => browse();
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(box);
        parent.Children.Add(row);
    }

    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog { Title = "Выберите tesseract.exe", Filter = "Tesseract (tesseract.exe)|tesseract.exe|Программы (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true) _exePath.Text = dialog.FileName;
    }

    private void BrowseLanguageModel()
    {
        var dialog = new OpenFileDialog { Title = "Выберите файл языковой модели", Filter = "Модели Tesseract (*.traineddata)|*.traineddata" };
        if (dialog.ShowDialog(this) == true) _dataPath.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    private void SavePaths()
    {
        var exe = _exePath.Text.Trim();
        var data = _dataPath.Text.Trim();
        if (exe.Length > 0 && !File.Exists(exe))
        {
            MessageBox.Show(this, "Файл tesseract.exe по указанному пути не найден.", "Настройка Tesseract", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (data.Length > 0 && !Directory.Exists(data))
        {
            MessageBox.Show(this, "Указанный каталог языковых моделей не найден.", "Настройка Tesseract", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.TesseractExePath = exe;
        _settings.TesseractDataPath = data;
        Settings.Save(_settings);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var exeOk = TesseractOcr.TryResolveExecutable(_settings, out var exe);
        var models = TesseractOcr.GetAvailableLanguages(_settings);
        var english = models.Contains("eng");
        var russian = models.Contains("rus");
        _status.Text = (exeOk ? $"Tesseract найден: {exe}" : "tesseract.exe не найден.") + "\n" +
            (models.Count == 0 ? "Модели языков не найдены." : "Найдены модели: " + string.Join(", ", models)) + "\n" +
            $"Английский: {(english ? "готов" : "нет eng.traineddata")}; русский: {(russian ? "готов" : "нет rus.traineddata")}.";
    }
}
