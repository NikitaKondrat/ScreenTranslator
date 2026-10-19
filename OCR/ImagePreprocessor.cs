using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenTranslator;

public static class ImagePreprocessor
{
    private const double HStretch = 1.6;

    public static Bitmap PrepareForOcr(Bitmap src) =>
        Upscale(src, ScaleFor(src));

    public static Bitmap PrepareForOcrEnhanced(Bitmap src, bool useSauvola)
    {
        var scale = ScaleFor(src);
        if (useSauvola)
        {
            using var bin = SauvolaBinarize(src, window: 21, k: 0.3);
            return UpscaleNearest(bin, scale);
        }
        using var enh = Enhance(src);
        return Upscale(enh, scale);
    }

    private static int ScaleFor(Bitmap src) =>
        (src.Width > 1600 || src.Height > 1200) ? 2 : 3;

    private static Bitmap Upscale(Bitmap src, int scale)
    {
        const int padding = 8;
        int w = (int)((src.Width + padding * 2) * scale * HStretch);
        int h = (src.Height + padding * 2) * scale;

        var dst = new Bitmap(w, h);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(src,
                (int)(padding * scale * HStretch), padding * scale,
                (int)(src.Width * scale * HStretch), src.Height * scale);
        }
        return dst;
    }

    private static Bitmap UpscaleNearest(Bitmap src, int scale)
    {
        const int padding = 8;
        int w = (int)((src.Width + padding * 2) * scale * HStretch);
        int h = (src.Height + padding * 2) * scale;

        var dst = new Bitmap(w, h);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(src,
                (int)(padding * scale * HStretch), padding * scale,
                (int)(src.Width * scale * HStretch), src.Height * scale);
        }
        return dst;
    }

    private static Bitmap Enhance(Bitmap src)
    {
        var gray = new Bitmap(src.Width, src.Height);
        using (var g = Graphics.FromImage(gray))
        using (var attrs = new ImageAttributes())
        {
            attrs.SetColorMatrix(GrayMatrix);
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height),
                        0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }

        var contrast = new Bitmap(src.Width, src.Height);
        using (var g = Graphics.FromImage(contrast))
        using (var attrs = new ImageAttributes())
        {
            attrs.SetColorMatrix(ContrastMatrix);
            g.DrawImage(gray, new Rectangle(0, 0, src.Width, src.Height),
                        0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }
        return contrast;
    }

    private static readonly ColorMatrix GrayMatrix = new(new[]
    {
        new float[]{0.3086f,0.3086f,0.3086f,0,0},
        new float[]{0.6094f,0.6094f,0.6094f,0,0},
        new float[]{0.0820f,0.0820f,0.0820f,0,0},
        new float[]{0,0,0,1,0},
        new float[]{0,0,0,0,1}
    });

    private static readonly ColorMatrix ContrastMatrix = new(new[]
    {
        new float[]{1.35f,0,0,0,0},
        new float[]{0,1.35f,0,0,0},
        new float[]{0,0,1.35f,0,0},
        new float[]{0,0,0,1,0},
        new float[]{-0.175f,-0.175f,-0.175f,0,1}
    });

    private static Bitmap SauvolaBinarize(Bitmap src, int window, double k)
    {
        int w = src.Width, h = src.Height;
        var rect = new Rectangle(0, 0, w, h);

        var data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        src.UnlockBits(data);

        var lum = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * data.Stride;
            for (int x = 0; x < w; x++)
            {
                int i = row + x * 4;
                lum[y * w + x] = 0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2];
            }
        }

        int sw = w + 1;
        var sum = new double[sw * (h + 1)];
        var sumSq = new double[sw * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            double rs = 0, rss = 0;
            for (int x = 0; x < w; x++)
            {
                double v = lum[y * w + x];
                rs += v;
                rss += v * v;
                int idx = (y + 1) * sw + (x + 1);
                sum[idx] = sum[y * sw + (x + 1)] + rs;
                sumSq[idx] = sumSq[y * sw + (x + 1)] + rss;
            }
        }

        var bin = new byte[w * h];
        int half = window / 2;
        int blackCount = 0;

        for (int y = 0; y < h; y++)
        {
            int y1 = Math.Max(0, y - half), y2 = Math.Min(h - 1, y + half);
            for (int x = 0; x < w; x++)
            {
                int x1 = Math.Max(0, x - half), x2 = Math.Min(w - 1, x + half);
                int area = (x2 - x1 + 1) * (y2 - y1 + 1);

                double s = sum[(y2 + 1) * sw + (x2 + 1)] - sum[y1 * sw + (x2 + 1)]
                         - sum[(y2 + 1) * sw + x1] + sum[y1 * sw + x1];
                double sq = sumSq[(y2 + 1) * sw + (x2 + 1)] - sumSq[y1 * sw + (x2 + 1)]
                          - sumSq[(y2 + 1) * sw + x1] + sumSq[y1 * sw + x1];

                double m = s / area;
                double sd = Math.Sqrt(Math.Max(sq / area - m * m, 0));
                double t = m * (1 + k * (sd / 128.0 - 1));

                byte v = (byte)(lum[y * w + x] > t ? 255 : 0);
                bin[y * w + x] = v;
                if (v == 0) blackCount++;
            }
        }

        bool invert = blackCount > w * h / 2;

        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var ddata = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var dpx = new byte[ddata.Stride * h];
        for (int y = 0; y < h; y++)
        {
            int srow = y * w, drow = y * ddata.Stride;
            for (int x = 0; x < w; x++)
            {
                byte v = bin[srow + x];
                if (invert) v = (byte)(255 - v);
                int i = drow + x * 4;
                dpx[i] = v; dpx[i + 1] = v; dpx[i + 2] = v; dpx[i + 3] = 255;
            }
        }
        Marshal.Copy(dpx, 0, ddata.Scan0, dpx.Length);
        dst.UnlockBits(ddata);
        return dst;
    }
}