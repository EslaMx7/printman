namespace Printman.Services.Renderers;

public partial class ImageDocumentRenderer
{
    // CUPS image filters print the first frame of multi-page images, so every image is one page
    public Task<int> GetPageCountAsync(string filePath) => Task.FromResult(1);
}
