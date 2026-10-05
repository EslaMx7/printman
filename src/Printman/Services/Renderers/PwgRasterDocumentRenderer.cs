namespace Printman.Services.Renderers;

/// <summary>
/// PWG Raster (PWG 5102.4, image/pwg-raster): "RaS2" sync word, then per page a 1796-byte
/// big-endian cups_page_header2_t followed by compressed pixel data.
/// Sent by Windows' IPP class driver, Android and CUPS clients.
/// </summary>
public class PwgRasterDocumentRenderer : RasterDocumentRenderer
{
    private const int PageHeaderSize = 1796;

    public override bool CanHandle(string fileExtension) =>
        string.Equals(fileExtension, ".pwg", StringComparison.OrdinalIgnoreCase);

    protected override IReadOnlyList<RasterPage> IndexPages(Stream stream)
    {
        var sync = new byte[4];
        if (!TryReadExactly(stream, sync) || sync[0] != 'R' || sync[1] != 'a' || sync[2] != 'S' || sync[3] != '2')
        {
            throw new InvalidDataException("Not a PWG raster file (expected 'RaS2').");
        }

        var pages = new List<RasterPage>();
        var header = new byte[PageHeaderSize];
        while (TryReadExactly(stream, header))
        {
            int dpi = ReadBigEndian(header, 276);            // HWResolution[0]
            int width = ReadBigEndian(header, 372);          // cupsWidth
            int height = ReadBigEndian(header, 376);         // cupsHeight
            int bitsPerColor = ReadBigEndian(header, 384);   // cupsBitsPerColor
            int bitsPerPixel = ReadBigEndian(header, 388);   // cupsBitsPerPixel
            int colorSpace = ReadBigEndian(header, 400);     // cupsColorSpace

            if (bitsPerColor != 8)
            {
                throw new NotSupportedException($"Unsupported PWG raster bit depth ({bitsPerColor} bits per color).");
            }

            var space = colorSpace switch
            {
                0 or 18 => RasterColorSpace.Gray,       // W / sGray
                3 => RasterColorSpace.Black,            // K
                1 or 19 or 20 => RasterColorSpace.Rgb,  // RGB / sRGB / AdobeRGB
                _ => throw new NotSupportedException($"Unsupported PWG raster color space ({colorSpace}).")
            };

            var page = CreatePage(stream.Position, width, height, bitsPerPixel, space, dpi);
            pages.Add(page);
            SkipPage(stream, page);
        }

        return pages;
    }
}
