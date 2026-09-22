using System.Drawing;
using System.Drawing.Drawing2D;
using Windows.Data.Pdf;
using Windows.Storage;
using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

public class PdfDocumentRenderer : IDocumentRenderer
{
    public bool CanHandle(string fileExtension) =>
        string.Equals(fileExtension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<int> GetPageCountAsync(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var storageFile = await StorageFile.GetFileFromPathAsync(fullPath);
        var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile);
        return (int)pdfDoc.PageCount;
    }

    public async Task RenderPageAsync(
        string filePath,
        int pageNumber,
        Graphics graphics,
        Rectangle printableArea,
        int dpi,
        bool fitToPage)
    {
        var fullPath = Path.GetFullPath(filePath);
        var storageFile = await StorageFile.GetFileFromPathAsync(fullPath);
        var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile);

        if (pageNumber < 1 || pageNumber > pdfDoc.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Requested page {pageNumber} is outside the document range (1 to {pdfDoc.PageCount}).");
        }

        using var page = pdfDoc.GetPage((uint)(pageNumber - 1));

        // PDF coordinate space is in points (72 points per inch)
        // High quality rendering: scale to target DPI (default 300 DPI)
        double scale = (dpi > 0 ? dpi : 300) / 72.0;
        uint destWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale));
        uint destHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale));

        var renderOptions = new PdfPageRenderOptions
        {
            DestinationWidth = destWidth,
            DestinationHeight = destHeight
        };

        using var memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(memStream, renderOptions);

        using var managedStream = memStream.AsStream();
        using var bitmap = new Bitmap(managedStream);

        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;

        if (fitToPage)
        {
            float scaleX = (float)printableArea.Width / bitmap.Width;
            float scaleY = (float)printableArea.Height / bitmap.Height;
            float uniformScale = Math.Min(scaleX, scaleY);

            int drawWidth = (int)Math.Round(bitmap.Width * uniformScale);
            int drawHeight = (int)Math.Round(bitmap.Height * uniformScale);
            int drawX = printableArea.X + (printableArea.Width - drawWidth) / 2;
            int drawY = printableArea.Y + (printableArea.Height - drawHeight) / 2;

            graphics.DrawImage(bitmap, new Rectangle(drawX, drawY, drawWidth, drawHeight));
        }
        else
        {
            graphics.DrawImage(bitmap, printableArea.X, printableArea.Y);
        }
    }
}
