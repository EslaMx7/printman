namespace Printman.Core.Abstractions;

public interface IDocumentRendererResolver
{
    /// <summary>
    /// Resolves the appropriate IDocumentRenderer for the specified file path.
    /// </summary>
    IDocumentRenderer Resolve(string filePath);
}
