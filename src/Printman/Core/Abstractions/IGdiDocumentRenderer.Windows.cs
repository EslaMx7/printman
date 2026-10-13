using System.Drawing;

namespace Printman.Core.Abstractions;

/// <summary>
/// Windows-only: renders document pages onto the GDI+ surface of a <c>PrintDocument</c>.
/// </summary>
public interface IGdiDocumentRenderer : IDocumentRenderer
{
    /// <summary>
    /// Renders a specific 1-based page onto the target Graphics surface.
    /// </summary>
    Task RenderPageAsync(
        string filePath,
        int pageNumber,
        Graphics graphics,
        Rectangle printableArea,
        int dpi,
        bool fitToPage);
}
