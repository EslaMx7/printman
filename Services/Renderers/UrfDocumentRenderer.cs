namespace Printman.Services.Renderers;

/// <summary>
/// Apple Raster (URF, image/urf): "UNIRAST\0" + page count, then per page a 32-byte header
/// followed by compressed pixel data. Required for AirPrint (iPhone / iPad / macOS).
/// </summary>
public class UrfDocumentRenderer : RasterDocumentRenderer
{
    private const int PageHeaderSize = 32;

    public override bool CanHandle(string fileExtension) =>
        string.Equals(fileExtension, ".urf", StringComparison.OrdinalIgnoreCase);

    protected override IReadOnlyList<RasterPage> IndexPages(Stream stream)
    {
        var fileHeader = new byte[12];
        if (!TryReadExactly(stream, fileHeader) || !fileHeader.AsSpan(0, 8).SequenceEqual("UNIRAST\0"u8))
        {
            throw new InvalidDataException("Not an Apple raster file (expected 'UNIRAST').");
        }

        // The declared page count (bytes 8-11) may be 0 when streaming, so pages are counted by scanning
        var pages = new List<RasterPage>();
        var header = new byte[PageHeaderSize];
        while (TryReadExactly(stream, header))
        {
            int bitsPerPixel = header[0];
            int colorSpace = header[1];
            int width = ReadBigEndian(header, 12);
            int height = ReadBigEndian(header, 16);
            int dpi = ReadBigEndian(header, 20);

            var space = colorSpace switch
            {
                0 or 4 => RasterColorSpace.Gray,       // sGray / W
                1 or 3 or 5 => RasterColorSpace.Rgb,   // sRGB / AdobeRGB / RGB
                _ => throw new NotSupportedException($"Unsupported Apple raster color space ({colorSpace}).")
            };

            var page = CreatePage(stream.Position, width, height, bitsPerPixel, space, dpi);
            pages.Add(page);
            SkipPage(stream, page);
        }

        return pages;
    }
}
