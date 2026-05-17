using System.Drawing;

using System.Drawing.Imaging;

using System.Runtime.InteropServices;

using Docnet.Core;

using Docnet.Core.Models;

using Microsoft.Extensions.Options;



namespace DocParseLab.Server.Services;



public interface IPdfPageRenderer
{
    byte[] RenderPageToPng(byte[] pdfBytes, int pageIndexZeroBased);
    int GetPageCount(byte[] pdfBytes);
}



public sealed class DocnetPdfPageRenderer : IPdfPageRenderer

{

    private readonly OcrOptions _options;



    public DocnetPdfPageRenderer(IOptions<OcrOptions> options)

    {

        _options = options.Value;

    }



    public int GetPageCount(byte[] pdfBytes)
    {
        if (pdfBytes.Length == 0) return 0;
        var (dimSmall, dimLarge) = GetRenderDimensions();
        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(dimSmall, dimLarge));
        return docReader.GetPageCount();
    }

    public byte[] RenderPageToPng(byte[] pdfBytes, int pageIndexZeroBased)
    {
        if (pdfBytes.Length == 0 || pageIndexZeroBased < 0) return Array.Empty<byte>();

        var (dimSmall, dimLarge) = GetRenderDimensions();
        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(dimSmall, dimLarge));

        var pageCount = docReader.GetPageCount();

        if (pageIndexZeroBased >= pageCount) return Array.Empty<byte>();



        using var pageReader = docReader.GetPageReader(pageIndexZeroBased);

        var rawBytes = pageReader.GetImage();

        var w = pageReader.GetPageWidth();

        var h = pageReader.GetPageHeight();

        if (rawBytes.Length == 0 || w <= 0 || h <= 0) return Array.Empty<byte>();



        return EncodeBgraToPng(rawBytes, w, h);
    }

    private (int dimSmall, int dimLarge) GetRenderDimensions()
    {
        var targetLongSide = Math.Clamp(Math.Max(_options.RenderWidth, _options.RenderHeight), 1200, 4000);
        var dimSmall = Math.Clamp((int)Math.Round(targetLongSide * 0.72), 800, 3200);
        return (dimSmall, targetLongSide);
    }

    private static byte[] EncodeBgraToPng(byte[] bgra, int width, int height)

    {

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        var rect = new Rectangle(0, 0, width, height);

        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try

        {

            var stride = data.Stride;

            var bytesPerPixel = 4;

            var rowBytes = width * bytesPerPixel;

            if (stride == rowBytes && bgra.Length >= rowBytes * height)

            {

                Marshal.Copy(bgra, 0, data.Scan0, rowBytes * height);

            }

            else

            {

                var copyLen = Math.Min(rowBytes, stride);

                for (var y = 0; y < height; y++)

                {

                    var srcOffset = y * rowBytes;

                    var dstOffset = y * stride;

                    if (srcOffset + copyLen > bgra.Length) break;

                    Marshal.Copy(bgra, srcOffset, data.Scan0 + dstOffset, copyLen);

                }

            }

        }

        finally

        {

            bitmap.UnlockBits(data);

        }



        using var ms = new MemoryStream();

        bitmap.Save(ms, ImageFormat.Png);

        return ms.ToArray();

    }

}

