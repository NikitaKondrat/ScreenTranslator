using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator;

public sealed class DictionaryWindow : Window
{
    private readonly StackPanel _list = new();
    private readonly StackPanel _card = new();
    private readonly TextBox _search = new();
    private readonly TextBlock _searchHint = new() { Text = "Поиск слова или перевода",
        Foreground = Brushes.Gray, Margin = new Thickness(20, 16, 0, 0),
        IsHitTestVisible = false };
    private DictEntry? _selected;

    private static Brush B(string hex) => WordCardBuilder.B(hex);

    public DictionaryWindow()
    {
        Title = "Словарь";
        Width = 900;
        Height = 620;
        Background = B("#141414");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _search.Background = B("#1F1F1F");
        _search.Foreground = B("#F2F2F2");
        _search.BorderBrush = B("#3A3A3A");
        _search.Padding = new Thickness(6, 4, 6, 4);
        _search.Margin = new Thickness(12, 12, 12, 8);
        _search.TextChanged += (_, _) =>
        {
            _searchHint.Visibility = string.IsNullOrEmpty(_search.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            RefreshList();
        };

        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _list
        };

        var left = new DockPanel();
        var searchBox = new Grid();
        searchBox.Children.Add(_search);
        searchBox.Children.Add(_searchHint);
        DockPanel.SetDock(searchBox, Dock.Top);
        left.Children.Add(searchBox);
        left.Children.Add(listScroll);

        var cardScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _card,
            Padding = new Thickness(20)
        };

        var splitter = new GridSplitter
        {
            Width = 1,
            Background = B("#3A3A3A")
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(cardScroll, 2);
        root.Children.Add(left);
        root.Children.Add(splitter);
        root.Children.Add(cardScroll);

        Content = root;
        _card.Children.Add(new TextBlock { Text = "Выберите слово слева",
            Foreground = B("#9A9A9A"), FontSize = 14 });
        RefreshList();
    }

    public void Refresh() => RefreshList();

    private void RefreshList()
    {
        _list.Children.Clear();
        var filter = (_search.Text ?? "").Trim().ToLowerInvariant();
        var entries = DictionaryStore.Load()
            .Where(e => filter.Length == 0 ||
                        e.Word.Contains(filter) ||
                        e.Meanings.Any(m => m.Contains(filter)))
            .ToList();

        if (entries.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "Пока пусто.",
                Foreground = B("#9A9A9A"),
                Margin = new Thickness(12, 4, 0, 0)
            });
            return;
        }

        var today = DateTime.Now.ToString("dd.MM.yyyy");
        foreach (var group in entries
                 .GroupBy(e => e.AddedAt)
                 .OrderByDescending(g => DateTime.TryParseExact(g.Key, "dd.MM.yyyy",
                     CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                     ? date : DateTime.MinValue))
        {
            _list.Children.Add(new TextBlock
            {
                Text = group.Key == today ? "Сегодня" : group.Key,
                Foreground = B("#9A9A9A"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, 10, 0, 6)
            });

            foreach (var entry in group)
            {
                var captured = entry;
                var row = new StackPanel { Margin = new Thickness(12, 0, 8, 10), Cursor = System.Windows.Input.Cursors.Hand };
                row.Children.Add(new TextBlock
                {
                    Text = entry.Word,
                    Foreground = B("#F2F2F2"),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold
                });
                row.Children.Add(new TextBlock
                {
                    Text = string.Join(", ", entry.Meanings),
                    Foreground = B("#9A9A9A"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
                row.MouseLeftButtonUp += (_, _) => ShowCard(captured);
                _list.Children.Add(row);
            }
        }
    }

    private void ShowCard(DictEntry entry)
    {
        _selected = entry;
        _card.Children.Clear();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var addMeaning = new Button
        {
            Content = "+ значение",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Background = B("#1F1F1F"),
            Foreground = B("#F2F2F2"),
            BorderBrush = B("#3A3A3A")
        };
        addMeaning.Click += (_, _) =>
        {
            var input = new InputWindow("Новое значение",
                $"Слово: {entry.Word}. Введи ещё один перевод:");
            if (input.ShowDialog() == true && !string.IsNullOrWhiteSpace(input.InputText))
            {
                try
                {
                    DictionaryStore.AddMeaning(entry.Word, input.InputText);
                    RefreshList();
                    ShowCard(DictionaryStore.Load().First(e => e.Word == entry.Word));
                }
                catch (Exception ex) { MessageBox.Show("Не удалось сохранить значение: " + ex.Message); }
            }
        };

        var delete = new Button
        {
            Content = "✕ удалить",
            Padding = new Thickness(8, 4, 8, 4),
            Background = B("#1F1F1F"),
            Foreground = B("#FF5C5C"),
            BorderBrush = B("#3A3A3A")
        };
        delete.Click += (_, _) =>
        {
            try
            {
                DictionaryStore.Remove(entry.Word);
                _selected = null;
                RefreshList();
                _card.Children.Clear();
            }
            catch (Exception ex) { MessageBox.Show("Не удалось удалить слово: " + ex.Message); }
        };

        buttons.Children.Add(addMeaning);
        buttons.Children.Add(delete);
        _card.Children.Add(buttons);

        _card.Children.Add(WordCardBuilder.BuildFromEntry(entry));
    }
}