using System.Drawing;

namespace OhMyPrinter.Core.Abstractions;

public interface IDocumentRenderer
{
    /// <summary>
    /// Checks if this renderer supports the given file extension (e.g. ".pdf", ".png").
    /// </summary>
    bool CanHandle(string fileExtension);

    /// <summary>
    /// Gets the total number of pages in the document.
    /// </summary>
    Task<int> GetPageCountAsync(string filePath);

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
