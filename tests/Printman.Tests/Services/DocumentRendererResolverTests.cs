using Printman.Services;
using Printman.Tests.Fakes;

namespace Printman.Tests.Services;

[TestClass]
public sealed class DocumentRendererResolverTests
{
    [TestMethod]
    public void Resolve_MatchesExtensionCaseInsensitively()
    {
        var renderer = new FakeDocumentRenderer(".pdf");
        var resolver = new DocumentRendererResolver([renderer]);

        Assert.AreSame(renderer, resolver.Resolve("C:\\docs\\report.pdf"));
        Assert.AreSame(renderer, resolver.Resolve("C:\\docs\\report.PDF"));
        Assert.AreSame(renderer, resolver.Resolve("C:\\docs\\REPORT.PdF"));
    }

    [TestMethod]
    public void Resolve_PicksTheRendererRegisteredForTheExtension()
    {
        var pdf = new FakeDocumentRenderer(".pdf");
        var txt = new FakeDocumentRenderer(".txt");
        var resolver = new DocumentRendererResolver([pdf, txt]);

        Assert.AreSame(txt, resolver.Resolve("notes.TXT"));
        Assert.AreSame(pdf, resolver.Resolve("notes.pdf"));
    }

    [TestMethod]
    public void Resolve_CustomRendererContract_IsHonored()
    {
        var custom = new FakeDocumentRenderer(".foo");
        var resolver = new DocumentRendererResolver([custom]);

        Assert.AreSame(custom, resolver.Resolve("archive.foo"));
    }

    [TestMethod]
    public void Resolve_PathWithoutExtension_ThrowsNotSupported()
    {
        var resolver = new DocumentRendererResolver([new FakeDocumentRenderer(".pdf")]);

        try
        {
            resolver.Resolve("C:\\docs\\report");
            Assert.Fail("Expected NotSupportedException.");
        }
        catch (NotSupportedException ex)
        {
            StringAssert.Contains(ex.Message, "has no file extension");
        }
    }

    [TestMethod]
    public void Resolve_UnknownExtension_ThrowsNotSupportedWithSupportedFormats()
    {
        var resolver = new DocumentRendererResolver([new FakeDocumentRenderer(".pdf")]);

        try
        {
            resolver.Resolve("C:\\docs\\report.xyz");
            Assert.Fail("Expected NotSupportedException.");
        }
        catch (NotSupportedException ex)
        {
            StringAssert.Contains(ex.Message, ".xyz");
            StringAssert.Contains(ex.Message, "is not supported");
            StringAssert.Contains(ex.Message, "Supported formats include: .pdf");
        }
    }

    [TestMethod]
    public void Resolve_NoRenderersRegistered_ThrowsNotSupported()
    {
        var resolver = new DocumentRendererResolver([]);

        try
        {
            resolver.Resolve("C:\\docs\\report.pdf");
            Assert.Fail("Expected NotSupportedException.");
        }
        catch (NotSupportedException ex)
        {
            StringAssert.Contains(ex.Message, "is not supported");
        }
    }
}
