using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

/// <summary>
/// Raster images. Windows decodes and draws them with GDI+ (ImageDocumentRenderer.Windows.cs);
/// Linux / macOS hand them to CUPS (ImageDocumentRenderer.Unix.cs).
/// </summary>
public partial class ImageDocumentRenderer : IDocumentRenderer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff", ".tif", ".ico"
    };

    public bool CanHandle(string fileExtension) =>
        SupportedExtensions.Contains(fileExtension);
}
