using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using PaddleOCRSharp;

namespace ScreenTranslator;

public static class PaddleOcr
{
    private static PaddleOCREngine? _engine;
    private static readonly object Lock = new();

    // движок дорогой, создаём один раз лениво
    private static PaddleOCREngine GetEngine()
    {
        if (_engine is not null) return _engine;
        lock (Lock)
        {
            _engine ??= new PaddleOCREngine(null, new OCRParameter());
        }
        return _engine;
    }

    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        using var prepared = ImagePreprocessor.PrepareForOcr(bitmap);

        return await Task.Run(() =>
        {
            try
            {
                var result = GetEngine().DetectText(prepared);
                // OCRResult.Text concatenates blocks without a separator.
                // Keep blocks even when Paddle did not return coordinates.
                // Using result.Text in that case loses the spaces between blocks.
                var blocks = result?.TextBlocks?
                    .Where(b => !string.IsNullOrWhiteSpace(b.Text))
                    .Select(b => new
                    {
                        Text = b.Text.Trim(),
                        HasPosition = b.BoxPoints is { Count: > 0 },
                        Left = b.BoxPoints is { Count: > 0 }
                            ? b.BoxPoints.Min(p => p.X) : 0,
                        Top = b.BoxPoints is { Count: > 0 }
                            ? b.BoxPoints.Min(p => p.Y) : 0
                    })
                    .ToList();

                // Sort only if every block has coordinates. Otherwise preserve
                // Paddle's own reading order instead of pushing unknown blocks away.
                if (blocks is { Count: > 0 } && blocks.All(b => b.HasPosition))
                    blocks = blocks.OrderBy(b => b.Top).ThenBy(b => b.Left).ToList();

                var recognized = blocks is null || blocks.Count == 0
                    ? result?.Text ?? ""
                    : string.Join(" ", blocks.Select(b => b.Text));

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[OCR] blocks={result?.TextBlocks?.Count ?? 0}");
                if (result?.TextBlocks is not null)
                    foreach (var block in result.TextBlocks)
                        System.Diagnostics.Debug.WriteLine($"[OCR] block=[{block.Text}]");
                System.Diagnostics.Debug.WriteLine($"[OCR] joined=[{recognized}]");
                System.Diagnostics.Debug.WriteLine("[OCR] chars=" + string.Join(" ",
                    recognized.Select(c => $"U+{(int)c:X4}")));
#endif

                // Correct every OCR result here. TextCorrector leaves ordinary
                // Cyrillic text unchanged and repairs merged English tokens.
                var corrected = TextCorrector.Correct(recognized);
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[OCR] corrected=[{corrected}]");
#endif
                return corrected;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("PaddleOCR: " + ex.Message, ex);
            }
        });
    }
}
