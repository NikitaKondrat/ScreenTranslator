using System.Drawing;

namespace ScreenTranslator;

public static class ScreenCapturer
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static Bitmap CapturePrimaryScreen()
    {
        // SM_CXSCREEN / SM_CYSCREEN — размеры основного экрана.
        // Значения 76–79 описывают виртуальный рабочий стол и включают
        // дополнительные мониторы, что здесь не требуется.
        return Capture(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
    }

    public static Bitmap Capture(int x, int y, int width, int height)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bmp;
    }
}
