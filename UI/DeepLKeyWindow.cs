using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator;

public sealed class DeepLKeyWindow : Window
{
    private readonly PasswordBox _keyBox = new();
    private readonly Settings _settings;

    public DeepLKeyWindow(Settings settings)
    {
        _settings = settings;
        Title = "Ключ DeepL";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var prompt = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(settings.DeepLApiKey)
                ? "Введите API-ключ DeepL. Он будет сохранён в настройках этого приложения. Если задана переменная DEEPL_API_KEY, она имеет приоритет."
                : "Ключ сохранён. Введите новый ключ, чтобы заменить его. Переменная DEEPL_API_KEY в Windows имеет приоритет над ключом здесь.",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _keyBox.Padding = new Thickness(6);
        _keyBox.FontSize = 14;

        var save = new Button { Content = "Сохранить", MinWidth = 96, Padding = new Thickness(8, 5, 8, 5), IsDefault = true };
        save.Click += (_, _) =>
        {
            var key = _keyBox.Password.Trim();
            if (key.Length == 0)
            {
                MessageBox.Show(this, "Введите ключ DeepL.", "Ключ DeepL", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _settings.DeepLApiKey = key;
            Settings.Save(_settings);
            DialogResult = true;
            Close();
        };

        var cancel = new Button { Content = "Отмена", MinWidth = 80, Padding = new Thickness(8, 5, 8, 5), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(prompt);
        root.Children.Add(_keyBox);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => _keyBox.Focus();
    }
}
