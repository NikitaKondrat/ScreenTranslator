using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScreenTranslator;

public static class WordCardBuilder
{
    public static Brush B(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public static StackPanel Build(WordCardData d, bool compact, double baseFontSize = 16)
    {
        var panel = new StackPanel();
        double scale = (compact ? 0.85 : 1.0) * (baseFontSize / 16.0);

        panel.Children.Add(new TextBlock
        {
            Text = d.Word,
            FontSize = 24 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = B("#F2F2F2"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });

        panel.Children.Add(new TextBlock
        {
            Text = d.TranslationsLine,
            FontSize = 14 * scale,
            Foreground = B("#58A6FF"),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });

        if (!string.IsNullOrWhiteSpace(d.DefinitionRu))
        {
            panel.Children.Add(new Border
            {
                BorderBrush = B("#58A6FF"),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 2, 0, 2),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock
                {
                    Text = d.DefinitionRu,
                    FontSize = 13 * scale,
                    Foreground = B("#C9C9C9"),
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(d.ExampleEn))
        {
            panel.Children.Add(Highlighted(d.ExampleEn,
                new[] { d.Word, d.Surface }, false, 13 * scale, "#99FFFFFF"));
            panel.Children.Add(new TextBlock { Text = "", Height = 4 });
        }
        if (!string.IsNullOrWhiteSpace(d.ExampleRu))
        {
            panel.Children.Add(Highlighted(d.ExampleRu,
                d.Translations.Select(t => t.Text).ToList(), true, 13 * scale, "#BBBBBB"));
        }

        return panel;
    }

    public static StackPanel BuildFromEntry(DictEntry e)
    {
        var d = new WordCardData
        {
            Word = e.Word,
            Surface = e.Word,
            DefinitionRu = e.Definition,
            ExampleEn = e.ExampleEn,
            ExampleRu = e.ExampleRu,
            TranslationsLine = string.Join(", ", e.Meanings)
        };
        foreach (var m in e.Meanings) d.Translations.Add((m, ""));
        return Build(d, false);
    }

    public static TextBlock Highlighted(string text, IEnumerable<string> keys,
        bool italic, double fontSize, string fg)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = B(fg)
        };

        var pattern = string.Join("|", keys
            .Where(k => !string.IsNullOrWhiteSpace(k) && k.Length >= 3)
            .Select(k => Regex.Escape(k.ToLowerInvariant().Split(' ')[0]) + @"\w*"));

        var match = pattern.Length > 0
            ? Regex.Match(text.ToLowerInvariant(), pattern)
            : Match.Empty;

        if (!match.Success)
        {
            tb.Text = text;
            return tb;
        }

        tb.Inlines.Add(new Run(text[..match.Index]));
        tb.Inlines.Add(new Run(text.Substring(match.Index, match.Length))
        {
            Background = B("#FFE94D"),
            Foreground = B("#111111")
        });
        tb.Inlines.Add(new Run(text[(match.Index + match.Length)..]));
        return tb;
    }
}