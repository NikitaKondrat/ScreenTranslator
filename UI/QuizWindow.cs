using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator;

public sealed class QuizWindow : Window
{
    private const int MaxWords = 20;
    private static readonly ITranslator Translator = TranslationService.Instance;
    private static readonly Random Rnd = new();

    private readonly List<DictEntry> _pool =
        DictionaryStore.Load().Where(e => e.Meanings.Count > 0).ToList();

    private readonly StackPanel _root = new();

    private List<DictEntry> _questions = new();
    private List<string> _prompts = new();
    private bool _engToRus;
    private int _index;
    private int _correct;
    private bool _answered;
    private bool _checking;

    private TextBox _answerBox = new();
    private TextBlock _feedback = new();
    private Button _actionButton = new();

    public QuizWindow()
    {
        Title = "Словарная игра";
        Width = 480;
        Height = 290;
        MinHeight = 290;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _root.Margin = new Thickness(20);
        Content = _root;
        ShowModeSelection();
    }

    private void ShowModeSelection()
    {
        _root.Children.Clear();
        _root.Children.Add(Header("Выбери режим"));

        var b1 = new Button
        {
            Content = "Английский → русский",
            Padding = new Thickness(6),
            Margin = new Thickness(0, 10, 0, 8)
        };
        b1.Click += (_, _) => Start(true);

        var b2 = new Button { Content = "Русский → английский", Padding = new Thickness(6) };
        b2.Click += (_, _) => Start(false);

        _root.Children.Add(b1);
        _root.Children.Add(b2);
    }

    private void Start(bool engToRus)
    {
        _engToRus = engToRus;
        _questions = _pool.OrderBy(_ => Guid.NewGuid()).Take(MaxWords).ToList();
        _prompts = _questions.Select(q =>
            engToRus ? q.Word : q.Meanings[Rnd.Next(q.Meanings.Count)]).ToList();
        _index = 0;
        _correct = 0;
        ShowQuestion();
    }

    private void ShowQuestion()
    {
        if (_index >= _questions.Count)
        {
            ShowSummary();
            return;
        }

        _answered = false;
        _checking = false;

        _root.Children.Clear();
        _root.Children.Add(new TextBlock
        {
            Text = $"Вопрос {_index + 1} из {_questions.Count}",
            Foreground = Brushes.Gray,
            FontSize = 12
        });

        _root.Children.Add(new TextBlock
        {
            Text = _prompts[_index],
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 14),
            TextWrapping = TextWrapping.Wrap
        });

        _root.Children.Add(new TextBlock { Text = "Введи перевод",
            Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 0, 0, 5) });
        _answerBox = new TextBox { FontSize = 16, Padding = new Thickness(4) };
        _root.Children.Add(_answerBox);

        _feedback = new TextBlock
        {
            FontSize = 16,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _root.Children.Add(_feedback);

        _actionButton = new Button
        {
            Content = "Ответить",
            Padding = new Thickness(6),
            Margin = new Thickness(0, 12, 0, 0),
            IsDefault = true
        };
        _actionButton.Click += OnActionClick;
        _root.Children.Add(_actionButton);

        Dispatcher.BeginInvoke(new Action(() => _answerBox.Focus()));
    }

    private async void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (_checking) return;

        if (!_answered)
        {
            _checking = true;
            _actionButton.IsEnabled = false;
            _feedback.Text = "Проверяю…";
            _feedback.Foreground = Brushes.Gray;

            var entry = _questions[_index];
            bool ok = await CheckAnswerAsync(entry, _answerBox.Text);
            if (!IsVisible) return;

            if (ok)
            {
                _correct++;
                _feedback.Text = "✓ Верно";
                _feedback.Foreground =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
            }
            else
            {
                var correctText = _engToRus
                    ? string.Join(", ", entry.Meanings)
                    : entry.Word;
                _feedback.Text = "✕ Неверно. Правильный ответ: " + correctText;
                _feedback.Foreground =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
            }

            _answered = true;
            _checking = false;
            _answerBox.IsEnabled = false;
            _actionButton.IsEnabled = true;
            _actionButton.Content = "Дальше";
        }
        else
        {
            _index++;
            ShowQuestion();
        }
    }


    private async Task<bool> CheckAnswerAsync(DictEntry entry, string answer)
    {
        var a = Normalize(answer);
        if (a.Length == 0) return false;

        if (_engToRus)
        {
            if (entry.Meanings.Any(m => Normalize(m) == a)) return true;

            var back = await TranslateSafe(a, "RU", "EN");
            if (back is null) return false;
            var nb = Normalize(back);
            var w = Normalize(entry.Word);
            return nb == w || nb.Split(' ').Contains(w);
        }
        else
        {
            if (Normalize(entry.Word) == a) return true;

            var back = await TranslateSafe(a, "EN", "RU");
            if (back is null) return false;
            var nb = Normalize(back);
            return entry.Meanings.Any(m =>
            {
                var nm = Normalize(m);
                return nb == nm || nb.Contains(nm) || nm.Contains(nb);
            });
        }
    }

    private static async Task<string?> TranslateSafe(string text, string src, string dst)
    {
        try
        {
            return await Translator.TranslateAsync(text, src, dst);
        }
        catch
        {
            return null;
        }
    }

    private void ShowSummary()
    {
        _root.Children.Clear();
        _root.Children.Add(Header("Итоги"));

        _root.Children.Add(new TextBlock
        {
            Text = $"Правильно {_correct} из {_questions.Count}",
            Foreground = Brushes.White,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 6)
        });

        var percent = _questions.Count == 0 ? 0 : _correct * 100 / _questions.Count;
        _root.Children.Add(new TextBlock
        {
            Text = $"Это {percent}%",
            Foreground = Brushes.Gray,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var close = new Button { Content = "Закрыть", Padding = new Thickness(6) };
        close.Click += (_, _) => Close();
        _root.Children.Add(close);
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold
    };

    private static string Normalize(string s) =>
        Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");
}
