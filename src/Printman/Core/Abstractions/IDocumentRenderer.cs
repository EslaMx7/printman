namespace Printman.Core.Abstractions;

/// <summary>
/// A supported document format. Platform independent: the Windows build draws pages itself
/// (<c>IGdiDocumentRenderer</c>), the Linux / macOS build hands the file to CUPS.
/// </summary>
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
}
