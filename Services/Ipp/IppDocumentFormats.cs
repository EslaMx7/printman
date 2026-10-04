using Printman.Core.Abstractions;

namespace Printman.Services.Ipp;

/// <summary>
/// Maps IPP document formats (MIME types) to cache file extensions, limited to formats
/// that a registered <see cref="IDocumentRenderer"/> can print.
/// </summary>
public sealed class IppDocumentFormats
{
    public const string OctetStream = "application/octet-stream";
    public const string Pdf = "application/pdf";
    public const string Urf = "image/urf";
    public const string PwgRaster = "image/pwg-raster";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    // Preference order also drives the DNS-SD "pdl" key
    private static readonly (string Mime, string Extension)[] Known =
    [
        (Pdf, ".pdf"),
        (Urf, ".urf"),
        (PwgRaster, ".pwg"),
        (Jpeg, ".jpg"),
        (Png, ".png")
    ];

    private readonly Dictionary<string, string> _supported;

    public IppDocumentFormats(IEnumerable<IDocumentRenderer> renderers)
    {
        var list = renderers.ToList();
        _supported = Known
            .Where(k => list.Any(r => r.CanHandle(k.Extension)))
            .ToDictionary(k => k.Mime, k => k.Extension, StringComparer.OrdinalIgnoreCase);
        SupportedMimeTypes = Known.Where(k => _supported.ContainsKey(k.Mime)).Select(k => k.Mime).ToList();
    }

    /// <summary>Printable formats in preference order (without application/octet-stream).</summary>
    public IReadOnlyList<string> SupportedMimeTypes { get; }

    public bool Supports(string? mime) => mime != null && _supported.ContainsKey(mime);
    public bool SupportsUrf => Supports(Urf);
    public bool SupportsPwgRaster => Supports(PwgRaster);

    public string? ExtensionFor(string mime) => _supported.TryGetValue(mime, out var ext) ? ext : null;

    public static bool IsRaster(string? mime) =>
        string.Equals(mime, Urf, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mime, PwgRaster, StringComparison.OrdinalIgnoreCase);

    /// <summary>Detects the format from the first bytes of a document, or null if unknown.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF"u8)) return Pdf;
        if (head.StartsWith("RaS2"u8) || head.StartsWith("RaS3"u8)) return PwgRaster;
        if (head.StartsWith("UNIRAST"u8)) return Urf;
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return Jpeg;
        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png;
        return null;
    }
}
