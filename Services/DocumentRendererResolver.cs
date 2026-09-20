using OhMyPrinter.Core.Abstractions;

namespace OhMyPrinter.Services;

public class DocumentRendererResolver(IEnumerable<IDocumentRenderer> renderers) : IDocumentRendererResolver
{
    private readonly IEnumerable<IDocumentRenderer> _renderers = renderers;

    public IDocumentRenderer Resolve(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
        {
            throw new NotSupportedException($"File '{filePath}' has no file extension.");
        }

        var renderer = _renderers.FirstOrDefault(r => r.CanHandle(extension));
        if (renderer == null)
        {
            throw new NotSupportedException(
                $"File format '{extension}' is not supported. Supported formats include: .pdf, .png, .jpg, .jpeg, .bmp, .gif, .tiff, .txt, .log, .csv, .json, .md.");
        }

        return renderer;
    }
}
