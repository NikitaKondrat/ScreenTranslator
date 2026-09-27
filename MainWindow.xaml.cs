using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenTranslator;

public partial class MainWindow : Window
{
    private readonly DeepLTranslator _translator = new();
    private readonly List<GlobalHotkey> _hotkeys = new();
    private Settings _settings = Settings.Load();

    private TranslationOverlay? _overlay;
    private DictionaryWindow? _dictionary;
    private bool _isBusy;
    private bool _allowClose;

    private Button? _captureButton;
    private HotkeySettings? _captureTarget;

    private static readonly Brush CapBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
    private static readonly Brush CapBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A4A4A"));
    private static readonly Brush CapText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F2F2F2"));
    private static readonly Brush PlusText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9A9A9A"));

    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (!_allowClose) { e.Cancel = true; Hide(); }
        };
    }

    public void Initialize()
    {
        new WindowInteropHelper(this).EnsureHandle();

        if (!RebuildHotkeys())
        {
            MessageBox.Show(
                "Не удалось зарегистрировать горячие клавиши. " +
                "Возможно, запущена ещё одна копия программы или сочетания заняты. " +
                "Сейчас откроется меню — сочетания можно поменять там.");
            Show();
            Activate();
        }

        RefreshHotkeyButtons();

        // язык OCR
        OcrLanguageBox.Items.Add(new ComboBoxItem { Content = "Английский", Tag = "en-US" });
        OcrLanguageBox.Items.Add(new ComboBoxItem { Content = "Русский", Tag = "ru-RU" });
        OcrLanguageBox.Items.Add(new ComboBoxItem { Content = "Авто (системный)", Tag = "auto" });
        foreach (ComboBoxItem item in OcrLanguageBox.Items)
        {
            if ((string)item.Tag == _settings.OcrLanguage)
            {
                OcrLanguageBox.SelectedItem = item;
                break;
            }
        }
        if (OcrLanguageBox.SelectedItem is null)
            OcrLanguageBox.SelectedIndex = 0;

        FontSizeSlider.Value = _settings.OverlayFontSize;
        FontSizeLabel.Text = _settings.OverlayFontSize.ToString("0");

        // движок OCR
        OcrEngineBox.Items.Add(new ComboBoxItem { Content = "Windows OCR", Tag = "windows" });
        OcrEngineBox.Items.Add(new ComboBoxItem { Content = "PaddleOCR", Tag = "paddle" });
        OcrEngineBox.Items.Add(new ComboBoxItem { Content = "Tesseract OCR", Tag = "tesseract" });
        foreach (ComboBoxItem engineItem in OcrEngineBox.Items)
        {
            if ((string)engineItem.Tag == _settings.OcrEngine)
            {
                OcrEngineBox.SelectedItem = engineItem;
                break;
            }
        }
        if (OcrEngineBox.SelectedItem is null)
            OcrEngineBox.SelectedIndex = 0;

        _overlay = new TranslationOverlay();
        _overlay.SetHint(BuildHintText());
        _overlay.AnimateIn();
    }

    private string BuildHintText() =>
        _settings.RegionHotkey.Format() + " — перевод области\n" +
        _settings.ScreenHotkey.Format() + " — перевод всего экрана\n" +
        _settings.MenuHotkey.Format() + " — меню";

    // направление перевода следует за языком OCR
    private (string Source, string Target) TranslationPair(string recognizedText)
    {
        var russian = _settings.OcrLanguage == "auto"
            ? recognizedText.Count(c => c >= 'А' && c <= 'я' || c == 'Ё' || c == 'ё') >
              recognizedText.Count(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z')
            : _settings.OcrLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        var src = russian ? "RU" : "EN";
        return (src, src == "EN" ? "RU" : "EN");
    }

    // ---------- настройки распознавания ----------

    private void OnOcrLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OcrLanguageBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && tag != _settings.OcrLanguage)
        {
            _settings.OcrLanguage = tag;
            Settings.Save(_settings);
        }
    }

    private void OnFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontSizeLabel is null) return;
        var value = Math.Round(FontSizeSlider.Value);
        FontSizeLabel.Text = value.ToString("0");
        if (Math.Abs(value - _settings.OverlayFontSize) < 0.1) return;
        _settings.OverlayFontSize = value;
        Settings.Save(_settings);
        _overlay?.SetFontSize(value);
    }

    private void OnOcrEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OcrEngineBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && tag != _settings.OcrEngine)
        {
            _settings.OcrEngine = tag;
            Settings.Save(_settings);
        }
    }

    // ---------- заголовок ----------

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e) => DragMove();
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMenuHideClick(object sender, RoutedEventArgs e) => Hide();

    // ---------- горячие клавиши ----------

    private bool RebuildHotkeys()
    {
        foreach (var h in _hotkeys) h.Dispose();
        _hotkeys.Clear();

        try
        {
            _hotkeys.Add(CreateHotkey(_settings.RegionHotkey, OnRegionHotkey, 1));
            _hotkeys.Add(CreateHotkey(_settings.ScreenHotkey, OnScreenHotkey, 2));
            _hotkeys.Add(CreateHotkey(_settings.MenuHotkey, OnMenuHotkey, 3));
            return true;
        }
        catch
        {
            foreach (var h in _hotkeys) h.Dispose();
            _hotkeys.Clear();
            return false;
        }
    }

    private GlobalHotkey CreateHotkey(HotkeySettings s, Action handler, int id)
    {
        if (!Enum.TryParse<Key>(s.Key, out var key))
            throw new InvalidOperationException("неизвестная клавиша: " + s.Key);
        var h = new GlobalHotkey(this, id, key, s.Ctrl, s.Alt, s.Shift);
        h.Pressed += handler;
        return h;
    }

    private void OnRegionHotkey() => _ = SelectAndTranslateAsync();
    private void OnScreenHotkey() => _ = TranslateScreenAsync();

    private void OnMenuHotkey()
    {
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // ---------- перевод ----------

    private async Task SelectAndTranslateAsync()
    {
        if (_isBusy) return;
        _isBusy = true;
        bool menuWasVisible = IsVisible;
        try
        {
            if (menuWasVisible) Hide();
            await Task.Delay(300);

            var selector = new RegionSelector();
            selector.ShowDialog();
            if (!selector.Confirmed) return;

            var region = selector.SelectedRegion;
            using var shot = ScreenCapturer.Capture(
                (int)region.X, (int)region.Y, (int)region.Width, (int)region.Height);
            var original = await WindowsOcr.RecognizeAsync(shot);

            if (string.IsNullOrWhiteSpace(original))
                ShowResult("(текст не найден)");
            else
            {
                var (s, t) = TranslationPair(original);
                ShowResult(await _translator.TranslateAsync(original, s, t), original);
            }
        }
        catch (Exception ex)
        {
            ShowResult("Ошибка: " + ex.Message);
        }
        finally
        {
            if (menuWasVisible) OnMenuHotkey();
            _isBusy = false;
        }
    }

    private async Task TranslateScreenAsync()
    {
        if (_isBusy) return;
        _isBusy = true;
        bool menuWasVisible = IsVisible;
        try
        {
            if (menuWasVisible) Hide();
            await Task.Delay(300);

            using var shot = ScreenCapturer.CapturePrimaryScreen();
            var original = await WindowsOcr.RecognizeAsync(shot);

            if (string.IsNullOrWhiteSpace(original))
                ShowResult("(текст не найден)");
            else
            {
                var (s, t) = TranslationPair(original);
                ShowResult(await _translator.TranslateAsync(original, s, t), original);
            }
        }
        catch (Exception ex)
        {
            ShowResult("Ошибка: " + ex.Message);
        }
        finally
        {
            if (menuWasVisible) OnMenuHotkey();
            _isBusy = false;
        }
    }

    private void ShowResult(string text, string? original = null)
    {
        _overlay!.AddEntry(text, original);
        if (!_overlay.IsVisible) _overlay.AnimateIn();
    }

    // ---------- словарь и игра ----------

    private void OnDictionaryClick(object sender, RoutedEventArgs e)
    {
        if (_dictionary is null)
        {
            _dictionary = new DictionaryWindow();
            _dictionary.Closed += (_, _) => _dictionary = null;
        }
        _dictionary.Refresh();
        _dictionary.Show();
        _dictionary.Activate();
    }

    private void OnQuizClick(object sender, RoutedEventArgs e)
    {
        if (DictionaryStore.Load().Count == 0)
        {
            MessageBox.Show("Словарь пока пуст — добавляй слова кнопкой «+».");
            return;
        }
        new QuizWindow().ShowDialog();
    }

    // ---------- смена сочетаний ----------

    private void OnRegionHotkeyClick(object sender, RoutedEventArgs e) =>
        StartCapture(RegionHotkeyButton, _settings.RegionHotkey);
    private void OnScreenHotkeyClick(object sender, RoutedEventArgs e) =>
        StartCapture(ScreenHotkeyButton, _settings.ScreenHotkey);
    private void OnMenuHotkeyClick(object sender, RoutedEventArgs e) =>
        StartCapture(MenuHotkeyButton, _settings.MenuHotkey);

    private void StartCapture(Button btn, HotkeySettings target)
    {
        _captureButton = btn;
        _captureTarget = target;
        btn.Content = new TextBlock
        {
            Text = "нажмите сочетание…",
            Foreground = PlusText,
            FontStyle = FontStyles.Italic
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_captureButton is null)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                  or Key.LeftShift or Key.RightShift)
            return;

        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (_captureTarget is null) return;

        _captureTarget.Key = key.ToString();
        _captureTarget.Ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _captureTarget.Alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        _captureTarget.Shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (IsDuplicate(_captureTarget))
        {
            MessageBox.Show("Это сочетание уже используется в программе.");
            CancelCapture();
            return;
        }

        if (!RebuildHotkeys())
        {
            MessageBox.Show("Не удалось зарегистрировать сочетание — возможно, оно занято.");
            _settings = Settings.Load();
            RebuildHotkeys();
        }
        else
        {
            Settings.Save(_settings);
        }

        FinishCapture();
        _overlay?.SetHint(BuildHintText());
    }

    private bool IsDuplicate(HotkeySettings t)
    {
        foreach (var o in new[] { _settings.RegionHotkey, _settings.ScreenHotkey, _settings.MenuHotkey })
        {
            if (ReferenceEquals(o, t)) continue;
            if (o.Key == t.Key && o.Ctrl == t.Ctrl && o.Alt == t.Alt && o.Shift == t.Shift)
                return true;
        }
        return false;
    }

    private void CancelCapture()
    {
        _captureButton = null;
        _captureTarget = null;
        RefreshHotkeyButtons();
    }

    private void FinishCapture()
    {
        _captureButton = null;
        _captureTarget = null;
        RefreshHotkeyButtons();
    }

    private void RefreshHotkeyButtons()
    {
        RegionHotkeyButton.Content = BuildKeyCaps(_settings.RegionHotkey);
        ScreenHotkeyButton.Content = BuildKeyCaps(_settings.ScreenHotkey);
        MenuHotkeyButton.Content = BuildKeyCaps(_settings.MenuHotkey);
    }

    private static UIElement BuildKeyCaps(HotkeySettings h)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var keys = new List<string>();
        if (h.Ctrl) keys.Add("Ctrl");
        if (h.Alt) keys.Add("Alt");
        if (h.Shift) keys.Add("Shift");
        keys.Add(h.Key);

        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "+",
                    Foreground = PlusText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 0)
                });
            }

            panel.Children.Add(new Border
            {
                Background = CapBg,
                BorderBrush = CapBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 3, 7, 3),
                Child = new TextBlock
                {
                    Text = keys[i],
                    Foreground = CapText,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            });
        }
        return panel;
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        if (DictionaryStore.Load().Count > 0)
        {
            var play = MessageBox.Show(
                "Сыграешь напоследок в игру со словами?",
                "Screen Translator",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (play == MessageBoxResult.Yes)
                new QuizWindow().ShowDialog();
        }

        _allowClose = true;
        Application.Current.Shutdown();
    }
}
