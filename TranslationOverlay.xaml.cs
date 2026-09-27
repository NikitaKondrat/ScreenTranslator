using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScreenTranslator;

public partial class TranslationOverlay : Window
{
    private Point _pressScreen;
    private bool _pressed;
    private bool _dragged;

    private Point _resizeStartScreen;
    private double _resizeStartW;
    private double _resizeStartH;
    private bool _resizing;

    private string? _hintText;

    private readonly List<(string Original, string Translated)> _history = new();

    public TranslationOverlay()
    {
        InitializeComponent();
        MaxWidth = SystemParameters.PrimaryScreenWidth;
        MaxHeight = SystemParameters.PrimaryScreenHeight;
        Loaded += (_, _) => WindowStartupLocation = WindowStartupLocation.Manual;
    }

    public void AnimateIn()
    {
        Opacity = 0;
        Show();
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private double Fs => Settings.Load().OverlayFontSize;

    private static Brush B(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public void SetHint(string text)
    {
        _hintText = text;
        RenderAll();
    }

    public void SetFontSize(double size)
    {
        RenderAll();
        WordBadge.Instance.Hide();
    }

    public void AddEntry(string translated, string? original = null)
    {
        _history.Add((original ?? "", translated));
        RenderAll();
        Scroller.ScrollToEnd();
    }

    private void RenderAll()
    {
        HistoryPanel.Children.Clear();

        if (_hintText is not null)
        {
            var hintWrap = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            hintWrap.Children.Add(new TextBlock
            {
                Text = _hintText,
                FontSize = Fs - 3,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 720,
                Foreground = B("#77FFFFFF")
            });
            HistoryPanel.Children.Add(hintWrap);
        }

        for (int i = 0; i < _history.Count; i++)
        {
            if (i > 0 || HistoryPanel.Children.Count > 0)
            {
                HistoryPanel.Children.Add(new Border
                {
                    Height = 1,
                    Background = B("#2A2A2A"),
                    Margin = new Thickness(0, 0, 0, 12)
                });
            }
            HistoryPanel.Children.Add(BuildEntryBlock(_history[i].Original, _history[i].Translated));
        }
    }

    private StackPanel BuildEntryBlock(string original, string translated)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        double fontSize = Fs;

        if (!string.IsNullOrWhiteSpace(original))
        {
            var words = new WrapPanel
            {
                MaxWidth = 720,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            foreach (var raw in original.Split(new[] { ' ', '\t', '\r', '\n' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '\'').ToArray());
                var wordBlock = new TextBlock
                {
                    Text = raw,
                    Foreground = B("#99FFFFFF"),
                    FontSize = Math.Max(10, fontSize - 4),
                    Margin = new Thickness(0, 0, 5, 2),
                    Cursor = Cursors.Hand
                };

                if (clean.Length > 1)
                {
                    wordBlock.MouseEnter += (_, _) =>
                    {
                        wordBlock.TextDecorations = TextDecorations.Underline;
                        WordBadge.Instance.ShowFor(clean, wordBlock, original, translated);
                    };
                    wordBlock.MouseLeave += (_, _) =>
                    {
                        wordBlock.TextDecorations = null;
                        WordBadge.Instance.ScheduleHide();
                    };
                }

                words.Children.Add(wordBlock);
            }

            block.Children.Add(words);
        }

        block.Children.Add(new TextBlock
        {
            Text = translated,
            Foreground = Brushes.White,
            FontSize = fontSize,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        return block;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        RenderAll();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    // ---------- перетаскивание ----------

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsDescendantOfScrollBar(e.OriginalSource as DependencyObject)) return;
        _pressed = true;
        _dragged = false;
        _pressScreen = PointToScreen(e.GetPosition(this));
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        var pos = PointToScreen(e.GetPosition(this));
        double dx = pos.X - _pressScreen.X;
        double dy = pos.Y - _pressScreen.Y;
        if (!_dragged && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
        _dragged = true;
        Left += dx;
        Top += dy;
        _pressScreen = pos;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        _pressed = false;
    }

    private static bool IsDescendantOfScrollBar(DependencyObject? o)
    {
        while (o is not null)
        {
            if (o is ScrollBar) return true;
            o = VisualTreeHelper.GetParent(o);
        }
        return false;
    }

    // ---------- растягивание ----------

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SizeToContent != SizeToContent.Manual)
        {
            Width = ActualWidth;
            Height = ActualHeight;
            SizeToContent = SizeToContent.Manual;
        }
        _resizing = true;
        _resizeStartScreen = PointToScreen(e.GetPosition(this));
        _resizeStartW = ActualWidth;
        _resizeStartH = ActualHeight;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        var pos = PointToScreen(e.GetPosition(this));
        Width = Math.Max(240, _resizeStartW + (pos.X - _resizeStartScreen.X));
        Height = Math.Max(140, _resizeStartH + (pos.Y - _resizeStartScreen.Y));
    }

    private void OnGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        _resizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }
}