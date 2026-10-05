using System.Text;
using Printman.Services.Ipp;
using Printman.Tests.Fakes;

namespace Printman.Tests.Ipp;

[TestClass]
public sealed class IppDocumentFormatsTests
{
    private static IppDocumentFormats Formats(params string[] extensions) =>
        new([new FakeDocumentRenderer(extensions)]);

    [TestMethod]
    public void Constructor_NoRenderers_ReportsNoSupportedFormats()
    {
        var formats = Formats();

        Assert.AreEqual(0, formats.SupportedMimeTypes.Count);
        Assert.IsFalse(formats.Supports(IppDocumentFormats.Pdf));
        Assert.IsFalse(formats.SupportsUrf);
        Assert.IsFalse(formats.SupportsPwgRaster);
        Assert.IsNull(formats.ExtensionFor(IppDocumentFormats.Pdf));
    }

    [TestMethod]
    public void SupportedMimeTypes_FollowKnownPreferenceOrder()
    {
        var formats = Formats(".png", ".jpg", ".pwg", ".urf", ".pdf");

        CollectionAssert.AreEqual(
            new[]
            {
                IppDocumentFormats.Pdf,
                IppDocumentFormats.Urf,
                IppDocumentFormats.PwgRaster,
                IppDocumentFormats.Jpeg,
                IppDocumentFormats.Png
            },
            formats.SupportedMimeTypes.ToArray());
    }

    [TestMethod]
    public void Constructor_MultipleRenderers_UnionsTheirExtensions()
    {
        var formats = new IppDocumentFormats(
        [
            new FakeDocumentRenderer(".pdf"),
            new FakeDocumentRenderer(".png")
        ]);

        CollectionAssert.AreEqual(
            new[] { IppDocumentFormats.Pdf, IppDocumentFormats.Png },
            formats.SupportedMimeTypes.ToArray());
    }

    [TestMethod]
    [DataRow(IppDocumentFormats.Pdf)]
    [DataRow(IppDocumentFormats.Jpeg)]
    [DataRow(IppDocumentFormats.Png)]
    [DataRow(IppDocumentFormats.PwgRaster)]
    [DataRow(IppDocumentFormats.Urf)]
    public void Supports_KnownMime_ReturnsTrueAndExtensionIsCaseInsensitive(string mime)
    {
        var extension = mime switch
        {
            IppDocumentFormats.Pdf => ".pdf",
            IppDocumentFormats.Jpeg => ".jpg",
            IppDocumentFormats.Png => ".png",
            IppDocumentFormats.PwgRaster => ".pwg",
            _ => ".urf"
        };

        var formats = Formats(extension);

        Assert.IsTrue(formats.Supports(mime));
        Assert.IsTrue(formats.Supports(mime.ToUpperInvariant()));
        Assert.AreEqual(extension, formats.ExtensionFor(mime));
        Assert.AreEqual(extension, formats.ExtensionFor(mime.ToUpperInvariant()));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("text/plain")]
    [DataRow("application/x-msdownload")]
    public void Supports_UnknownOrNull_ReturnsFalse(string? mime) =>
        Assert.IsFalse(Formats(".pdf").Supports(mime));

    [TestMethod]
    public void Supports_OnlyRegisteredFormats()
    {
        var formats = Formats(".jpg");

        Assert.IsTrue(formats.Supports(IppDocumentFormats.Jpeg));
        Assert.IsFalse(formats.Supports(IppDocumentFormats.Pdf));
        Assert.IsFalse(formats.SupportsUrf);
        Assert.IsFalse(formats.SupportsPwgRaster);
        Assert.IsNull(formats.ExtensionFor(IppDocumentFormats.Pdf));
    }

    [TestMethod]
    public void Constructor_UnknownRendererExtensions_AreIgnored()
    {
        var formats = Formats(".txt", ".docx", ".json");

        Assert.AreEqual(0, formats.SupportedMimeTypes.Count);
    }

    [TestMethod]
    public void SupportsUrf_And_SupportsPwgRaster_ReflectRenderers()
    {
        var urf = Formats(".urf");
        var pwg = Formats(".pwg");

        Assert.IsTrue(urf.SupportsUrf);
        Assert.IsFalse(urf.SupportsPwgRaster);
        Assert.IsFalse(pwg.SupportsUrf);
        Assert.IsTrue(pwg.SupportsPwgRaster);
    }

    [TestMethod]
    [DataRow(IppDocumentFormats.Urf, true)]
    [DataRow("IMAGE/URF", true)]
    [DataRow(IppDocumentFormats.PwgRaster, true)]
    [DataRow("image/PWG-Raster", true)]
    [DataRow(IppDocumentFormats.Pdf, false)]
    [DataRow(IppDocumentFormats.Jpeg, false)]
    [DataRow(null, false)]
    [DataRow("", false)]
    public void IsRaster_ClassifiesMimeTypes(string? mime, bool expected) =>
        Assert.AreEqual(expected, IppDocumentFormats.IsRaster(mime));

    [TestMethod]
    [DataRow("%PDF-1.7", IppDocumentFormats.Pdf)]
    [DataRow("RaS2 raster bytes", IppDocumentFormats.PwgRaster)]
    [DataRow("RaS3 raster bytes", IppDocumentFormats.PwgRaster)]
    [DataRow("UNIRAST raster bytes", IppDocumentFormats.Urf)]
    public void Sniff_RecognizesTextSignatures(string content, string expected) =>
        Assert.AreEqual(expected, IppDocumentFormats.Sniff(Encoding.ASCII.GetBytes(content)));

    [TestMethod]
    public void Sniff_RecognizesJpeg()
    {
        Assert.AreEqual(IppDocumentFormats.Jpeg, IppDocumentFormats.Sniff(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.AreEqual(IppDocumentFormats.Jpeg, IppDocumentFormats.Sniff(new byte[] { 0xFF, 0xD8, 0xFF }));
    }

    [TestMethod]
    public void Sniff_RecognizesPng() =>
        Assert.AreEqual(
            IppDocumentFormats.Png,
            IppDocumentFormats.Sniff(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }));

    [TestMethod]
    public void Sniff_TooShort_ReturnsNull()
    {
        Assert.IsNull(IppDocumentFormats.Sniff(Array.Empty<byte>()));
        Assert.IsNull(IppDocumentFormats.Sniff(new byte[] { 0xFF }));
        Assert.IsNull(IppDocumentFormats.Sniff(new byte[] { 0xFF, 0xD8 }));
    }

    [TestMethod]
    public void Sniff_UnknownSignature_ReturnsNull()
    {
        Assert.IsNull(IppDocumentFormats.Sniff(Encoding.ASCII.GetBytes("GIF89a")));
        Assert.IsNull(IppDocumentFormats.Sniff(new byte[] { 0x00, 0x01, 0x02, 0x03 }));
    }
}
