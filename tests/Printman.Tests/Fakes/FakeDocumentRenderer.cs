using System.Drawing;
using Printman.Core.Abstractions;

namespace Printman.Tests.Fakes;

/// <summary>No-op renderer that supports a configurable set of extensions.</summary>
public sealed class FakeDocumentRenderer(params string[] extensions) : IDocumentRenderer
{
    public string[] Extensions { get; } = extensions;
    public int PageCount { get; set; } = 1;
    public int RenderedPages { get; private set; }

    public bool CanHandle(string fileExtension) =>
        Extensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase);

    public Task<int> GetPageCountAsync(string filePath) => Task.FromResult(PageCount);

    public Task RenderPageAsync(string filePath, int pageNumber, Graphics graphics, Rectangle printableArea, int dpi, bool fitToPage)
    {
        RenderedPages++;
        return Task.CompletedTask;
    }
}
