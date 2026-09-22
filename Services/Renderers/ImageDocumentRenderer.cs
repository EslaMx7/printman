using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

public class ImageDocumentRenderer : IDocumentRenderer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff", ".tif", ".ico"
    };

    public bool CanHandle(string fileExtension) =>
        SupportedExtensions.Contains(fileExtension);

    public Task<int> GetPageCountAsync(string filePath)
    {
        using var image = Image.FromFile(filePath);
        if (image.FrameDimensionsList.Contains(FrameDimension.Page.Guid))
        {
            var dimension = new FrameDimension(FrameDimension.Page.Guid);
            return Task.FromResult(image.GetFrameCount(dimension));
        }

        return Task.FromResult(1);
    }

    public Task RenderPageAsync(
        string filePath,
        int pageNumber,
        Graphics graphics,
        Rectangle printableArea,
        int dpi,
        bool fitToPage)
    {
        using var image = Image.FromFile(filePath);

        if (image.FrameDimensionsList.Contains(FrameDimension.Page.Guid))
        {
            var dimension = new FrameDimension(FrameDimension.Page.Guid);
            int frameCount = image.GetFrameCount(dimension);
            if (pageNumber < 1 || pageNumber > frameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber),
                    $"Requested frame {pageNumber} is outside the image frames range (1 to {frameCount}).");
            }
            image.SelectActiveFrame(dimension, pageNumber - 1);
        }

        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;

        if (fitToPage)
        {
            float scaleX = (float)printableArea.Width / image.Width;
            float scaleY = (float)printableArea.Height / image.Height;
            float uniformScale = Math.Min(scaleX, scaleY);

            int drawWidth = (int)Math.Round(image.Width * uniformScale);
            int drawHeight = (int)Math.Round(image.Height * uniformScale);
            int drawX = printableArea.X + (printableArea.Width - drawWidth) / 2;
            int drawY = printableArea.Y + (printableArea.Height - drawHeight) / 2;

            graphics.DrawImage(image, new Rectangle(drawX, drawY, drawWidth, drawHeight));
        }
        else
        {
            graphics.DrawImage(image, printableArea.X, printableArea.Y);
        }

        return Task.CompletedTask;
    }
}
