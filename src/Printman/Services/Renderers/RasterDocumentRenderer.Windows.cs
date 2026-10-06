using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

public abstract partial class RasterDocumentRenderer : IGdiDocumentRenderer
{
    public Task RenderPageAsync(string filePath, int pageNumber, Graphics graphics, Rectangle printableArea, int dpi, bool fitToPage)
    {
        var pages = GetPages(filePath);
        if (pageNumber < 1 || pageNumber > pages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Requested page {pageNumber} is outside the document range (1 to {pages.Count}).");
        }

        var page = pages[pageNumber - 1];
        using var file = OpenRead(filePath);
        file.Position = page.DataOffset;
        using var bitmap = DecodePage(file, page);

        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Graphics units are 1/100 inch; raster pages carry their own resolution
        float pageDpi = page.Dpi > 0 ? page.Dpi : 300;
        float naturalWidth = page.Width / pageDpi * 100f;
        float naturalHeight = page.Height / pageDpi * 100f;

        if (fitToPage)
        {
            float scale = Math.Min(printableArea.Width / naturalWidth, printableArea.Height / naturalHeight);
            float w = naturalWidth * scale;
            float h = naturalHeight * scale;
            graphics.DrawImage(bitmap,
                printableArea.X + (printableArea.Width - w) / 2,
                printableArea.Y + (printableArea.Height - h) / 2,
                w, h);
        }
        else
        {
            graphics.DrawImage(bitmap, printableArea.X, printableArea.Y, naturalWidth, naturalHeight);
        }

        return Task.CompletedTask;
    }

    private static Bitmap DecodePage(Stream stream, RasterPage page)
    {
        var bitmap = new Bitmap(page.Width, page.Height, PixelFormat.Format24bppRgb);
        if (page.Dpi > 0)
        {
            bitmap.SetResolution(page.Dpi, page.Dpi);
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, page.Width, page.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var bgr = new byte[page.Width * 3];
            DecodeRows(stream, page, (y, line, isNewLine) =>
            {
                if (isNewLine)
                {
                    ToBgr(line, bgr, page.ColorSpace);
                }
                Marshal.Copy(bgr, 0, data.Scan0 + (y * data.Stride), bgr.Length);
            });
        }
        catch
        {
            bitmap.UnlockBits(data);
            bitmap.Dispose();
            throw;
        }

        bitmap.UnlockBits(data);
        return bitmap;
    }

    private static void ToBgr(byte[] line, byte[] bgr, RasterColorSpace colorSpace)
    {
        switch (colorSpace)
        {
            case RasterColorSpace.Rgb:
                for (int i = 0; i < line.Length; i += 3)
                {
                    bgr[i] = line[i + 2];
                    bgr[i + 1] = line[i + 1];
                    bgr[i + 2] = line[i];
                }
                break;
            case RasterColorSpace.Gray:
                for (int i = 0, j = 0; i < line.Length; i++, j += 3)
                {
                    bgr[j] = bgr[j + 1] = bgr[j + 2] = line[i];
                }
                break;
            case RasterColorSpace.Black:
                for (int i = 0, j = 0; i < line.Length; i++, j += 3)
                {
                    bgr[j] = bgr[j + 1] = bgr[j + 2] = (byte)(255 - line[i]);
                }
                break;
        }
    }
}
