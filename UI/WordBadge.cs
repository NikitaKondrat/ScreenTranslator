using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenTranslator;

public sealed class WordBadge : Window
{
    public static readonly WordBadge Instance = new();

    private readonly StackPanel _cardHolder = new();
    private readonly Button _addButton;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _loadTimer;
    private CancellationTokenSource? _lookupCancellation;
    private int _requestVersion;
    private string _currentWord = "";
    private string? _ctxEn;
    private string? _ctxRu;
    private UIElement? _anchor;

    private WordBadge()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _addButton = new Button
        {
            Content = "+",
            Width = 28,
            Height = 28,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _addButton.Click += (_, _) => _ = SaveAsync();

        var grid = new Grid();
        grid.Children.Add(new StackPanel
        {
            Margin = new Thickness(14, 10, 14, 12),
            Children = { _cardHolder }
        });
        grid.Children.Add(_addButton);

        Content = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#242424")),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A5A5A")),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.6
            },
            Child = grid
        };

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            CancelPendingLookup();
            Hide();
        };

        _loadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        _loadTimer.Tick += (_, _) =>
        {
            _loadTimer.Stop();
            StartLookup();
        };

        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) CancelPendingLookup();
        };

        MouseEnter += (_, _) => _hideTimer.Stop();
        MouseLeave += (_, _) => ScheduleHide();
    }

    public void ShowFor(string word, UIElement anchor,
        string? contextEn = null, string? contextRu = null)
    {
        CancelPendingLookup();
        _requestVersion++;
        _currentWord = word;
        _anchor = anchor;
        _ctxEn = contextEn;
        _ctxRu = contextRu;
        _hideTimer.Stop();

        _cardHolder.Children.Clear();
        _cardHolder.Children.Add(new TextBlock
        {
            Text = word + " …",
            Foreground = Brushes.White,
            FontSize = 14
        });

        var inDict = DictionaryStore.Load().Any(e => e.Word == Lemmatizer.Lemmatize(word));
        _addButton.Content = inDict ? "✓" : "+";
        _addButton.IsEnabled = !inDict;

        if (!IsVisible) Show();
        Position();
        _loadTimer.Stop();
        _loadTimer.Start();
    }

    private void StartLookup()
    {
        var version = _requestVersion;
        var word = _currentWord;
        var contextEn = _ctxEn;
        var contextRu = _ctxRu;
        var cancellation = new CancellationTokenSource();
        _lookupCancellation = cancellation;
        _ = FillAsync(version, word, contextEn, contextRu, cancellation);
    }

    private async Task FillAsync(int version, string word, string? contextEn,
        string? contextRu, CancellationTokenSource cancellation)
    {
        try
        {
            var data = await WordInfoService.GetAsync(word, contextEn, contextRu,
                quick => ShowDataIfCurrent(version, word, quick), cancellation.Token);
            ShowDataIfCurrent(version, word, data);
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            if (ReferenceEquals(_lookupCancellation, cancellation))
                _lookupCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ShowDataIfCurrent(int version, string word, WordCardData data)
    {
        if (version != _requestVersion || _currentWord != word || !IsVisible) return;
        _cardHolder.Children.Clear();
        _cardHolder.Children.Add(WordCardBuilder.Build(
            data, compact: true, baseFontSize: Settings.Load().OverlayFontSize));
        Position();
    }

    private void CancelPendingLookup()
    {
        _loadTimer?.Stop();
        var cancellation = _lookupCancellation;
        _lookupCancellation = null;
        cancellation?.Cancel();
    }

    private void Position()
    {
        if (_anchor is null || !_anchor.IsVisible) return;
        UpdateLayout();

        var topLeft = _anchor.PointToScreen(new Point(0, 0));
        double w = _anchor.RenderSize.Width;
        double h = _anchor.RenderSize.Height;

        double vsL = SystemParameters.VirtualScreenLeft;
        double vsT = SystemParameters.VirtualScreenTop;
        double vsW = SystemParameters.VirtualScreenWidth;
        double vsH = SystemParameters.VirtualScreenHeight;

        double x = topLeft.X + w / 2 - ActualWidth / 2;
        x = Math.Clamp(x, vsL, vsL + Math.Max(0, vsW - ActualWidth));

        double y = topLeft.Y - ActualHeight - 6;
        if (y < vsT) y = topLeft.Y + h + 6;
        y = Math.Clamp(y, vsT, vsT + Math.Max(0, vsH - ActualHeight));

        Left = x;
        Top = y;
    }

    private async Task SaveAsync()
    {
        try
        {
            var prompt = new InputWindow("Проверка распознавания",
                "Проверь слово перед добавлением в словарь:", _currentWord);
            prompt.Topmost = true;
            if (prompt.ShowDialog() != true) return;
            var corrected = prompt.InputText.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(corrected)) return;
            var data = await WordInfoService.GetAsync(corrected, _ctxEn, _ctxRu);
            DictionaryStore.AddRich(new DictEntry
            {
                Word = data.Word,
                Meanings = data.Translations.Select(t => t.Text).ToList(),
                Definition = data.DefinitionRu,
                ExampleEn = data.ExampleEn,
                ExampleRu = data.ExampleRu
            });
            _addButton.Content = "✓";
            _addButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось сохранить слово: " + ex.Message);
        }
    }

    public void ScheduleHide()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void RefreshPosition() => Position();
}
