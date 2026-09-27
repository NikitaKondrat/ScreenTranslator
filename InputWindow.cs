using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator;

public sealed class InputWindow : Window
{
    public string InputText { get; private set; } = "";

    public InputWindow(string title, string promptText, string initialText = "")
    {
        Title = title;
        Width = 380;
        Height = 180;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var prompt = new TextBlock
        {
            Text = promptText,
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var box = new TextBox { FontSize = 15, Padding = new Thickness(4), Text = initialText };

        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        ok.Click += (_, _) => { InputText = box.Text; DialogResult = true; Close(); };

        var cancel = new Button { Content = "Отмена", Width = 90, Padding = new Thickness(6), IsCancel = true };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(prompt);
        root.Children.Add(box);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}