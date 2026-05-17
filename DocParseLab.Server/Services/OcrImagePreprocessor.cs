using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DocParseLab.Server.Services;

internal static class OcrImagePreprocessor
{
    private const int TargetMinWidth = 2000;
    private const int TargetMaxWidth = 3000;

    public static bool IsValidForOcr(byte[] imageBytes, int minWidth, int minHeight)
    {
        if (!TryGetSize(imageBytes, out var w, out var h))
        {
            return false;
        }

        return w >= minWidth && h >= minHeight;
    }

    public static bool TryGetSize(byte[] imageBytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (imageBytes.Length == 0) return false;

        try
        {
            using var ms = new MemoryStream(imageBytes);
            using var img = Image.FromStream(ms);
            width = img.Width;
            height = img.Height;
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    public static long EstimatePixelArea(byte[] imageBytes) =>
        TryGetSize(imageBytes, out var w, out var h) ? (long)w * h : imageBytes.Length;

    /// <summary>Мягкая подготовка: масштаб + серый + лёгкий контраст (без бинаризации).</summary>
    public static byte[] PrepareForOcr(byte[] imageBytes) => PrepareLight(imageBytes);

    public static byte[] PrepareLight(byte[] imageBytes)
    {
        if (imageBytes.Length == 0) return imageBytes;

        try
        {
            using var input = new MemoryStream(imageBytes);
            using var source = new Bitmap(input);
            using var gray = ToGrayscale(source);
            using var scaled = ScaleToOcrWidth(gray);
            using var enhanced = EnhanceContrast(scaled, factor: 1.22);
            return EncodePng(enhanced);
        }
        catch
        {
            return imageBytes;
        }
    }

    /// <summary>Доля тёмных пикселей; если слишком мало — изображение «пустое» для OCR.</summary>
    public static double EstimateInkRatio(byte[] pngBytes)
    {
        try
        {
            using var ms = new MemoryStream(pngBytes);
            using var bmp = new Bitmap(ms);
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var stride = data.Stride;
                var bytes = new byte[stride * bmp.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                var dark = 0L;
                var total = (long)bmp.Width * bmp.Height;
                for (var y = 0; y < bmp.Height; y++)
                {
                    var row = y * stride;
                    for (var x = 0; x < bmp.Width; x++)
                    {
                        var i = row + x * 3;
                        var lum = (bytes[i] + bytes[i + 1] + bytes[i + 2]) / 3;
                        if (lum < 210) dark++;
                    }
                }

                return dark / (double)Math.Max(1, total);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
        catch
        {
            return 0;
        }
    }

    private static Bitmap ScaleToOcrWidth(Bitmap source)
    {
        if (source.Width >= TargetMinWidth && source.Width <= TargetMaxWidth)
        {
            return (Bitmap)source.Clone();
        }

        var targetW = source.Width < TargetMinWidth
            ? TargetMinWidth
            : Math.Min(TargetMaxWidth, source.Width);

        var scale = targetW / (double)source.Width;
        var targetH = Math.Max(1, (int)Math.Round(source.Height * scale));

        var result = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.White);
        g.DrawImage(source, 0, 0, targetW, targetH);
        return result;
    }

    private static Bitmap ToGrayscale(Bitmap source)
    {
        var gray = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(gray);
        var matrix = new ColorMatrix(
        [
            [0.299f, 0.299f, 0.299f, 0, 0],
            [0.587f, 0.587f, 0.587f, 0, 0],
            [0.114f, 0.114f, 0.114f, 0, 0],
            [0, 0, 0, 1, 0],
            [0, 0, 0, 0, 1],
        ]);
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(matrix);
        g.DrawImage(source, new Rectangle(0, 0, gray.Width, gray.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
        return gray;
    }

    private static Bitmap EnhanceContrast(Bitmap source, double factor)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, source.Width, source.Height);
        var srcData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = srcData.Stride;
            var bytes = new byte[stride * source.Height];
            Marshal.Copy(srcData.Scan0, bytes, 0, bytes.Length);
            for (var i = 0; i < bytes.Length; i += 3)
            {
                var lum = (int)(bytes[i] * 0.114 + bytes[i + 1] * 0.587 + bytes[i + 2] * 0.299);
                lum = (int)Math.Clamp((lum - 128) * factor + 128, 0, 255);
                bytes[i] = bytes[i + 1] = bytes[i + 2] = (byte)lum;
            }

            Marshal.Copy(bytes, 0, dstData.Scan0, bytes.Length);
        }
        finally
        {
            source.UnlockBits(srcData);
            result.UnlockBits(dstData);
        }

        return result;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
