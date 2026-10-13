using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

/// <summary>
/// PDF documents. Windows renders pages through WinRT <c>Windows.Data.Pdf</c> (PdfDocumentRenderer.Windows.cs);
/// Linux / macOS only count pages and let CUPS rasterize (PdfDocumentRenderer.Unix.cs).
/// </summary>
public partial class PdfDocumentRenderer : IDocumentRenderer
{
    public bool CanHandle(string fileExtension) =>
        string.Equals(fileExtension, ".pdf", StringComparison.OrdinalIgnoreCase);
}
