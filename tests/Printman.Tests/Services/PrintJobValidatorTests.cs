using System.Drawing;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Services;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Services;

[TestClass]
public sealed class PrintJobValidatorTests
{
    [TestMethod]
    public async Task Validate_ValidRequestWithExplicitPrinter_Succeeds()
    {
        using var fx = new ValidatorFixture();
        fx.Renderer.PageCount = 3;
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, range: PageRange.Parse("1-2")));

        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task Validate_DefaultPrinterAndAllPages_Succeeds()
    {
        using var fx = new ValidatorFixture();
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, printer: null));

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t")]
    public async Task Validate_BlankPath_Fails(string path)
    {
        using var fx = new ValidatorFixture();

        var result = await fx.Validator.ValidateAsync(new PrintJobRequest { FilePath = path });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("File path cannot be empty.", result.ErrorMessage);
    }

    [TestMethod]
    public async Task Validate_NonExistentFile_Fails()
    {
        using var fx = new ValidatorFixture();
        var missing = fx.Temp.Combine("missing.txt");

        var result = await fx.Validator.ValidateAsync(Request(missing));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "File not found");
        StringAssert.Contains(result.ErrorMessage!, "missing.txt");
    }

    [TestMethod]
    public async Task Validate_UnsupportedExtension_FailsWithResolverMessage()
    {
        using var fx = new ValidatorFixture();
        var path = fx.WriteFile("doc.xyz");

        var result = await fx.Validator.ValidateAsync(Request(path));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "No renderer registered");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(-100)]
    public async Task Validate_CopiesBelowOne_Fails(int copies)
    {
        using var fx = new ValidatorFixture();
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, copies: copies));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "copies must be at least 1");
        StringAssert.Contains(result.ErrorMessage!, copies.ToString());
    }

    [TestMethod]
    [DataRow("Fake Printer")]
    [DataRow("fake printer")]
    [DataRow("Fake")]
    [DataRow("printer")]
    public async Task Validate_PrinterMatchesExactOrSubstring_Succeeds(string printer)
    {
        using var fx = new ValidatorFixture();
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, printer: printer));

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public async Task Validate_ExplicitPrinterNotFound_Fails()
    {
        using var fx = new ValidatorFixture();
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, printer: "Nonexistent"));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "Printer matching 'Nonexistent' could not be found.");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public async Task Validate_NoTargetAndNoDefaultPrinter_Fails(string? printer)
    {
        using var fx = new ValidatorFixture();
        fx.Discovery.Printers.Clear();
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, printer: printer));

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("No default printer is configured on this machine.", result.ErrorMessage);
    }

    [TestMethod]
    public async Task Validate_ExplicitTargetAndNoPrinters_Fails()
    {
        using var fx = new ValidatorFixture();
        fx.Discovery.Printers.Clear();
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, printer: "Fake Printer"));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "Printer matching 'Fake Printer' could not be found.");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-3)]
    public async Task Validate_NonPositivePageCount_Fails(int pageCount)
    {
        using var fx = new ValidatorFixture();
        fx.Renderer.PageCount = pageCount;
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "does not contain any printable pages");
    }

    [TestMethod]
    public async Task Validate_PageRangeOutsideDocument_Fails()
    {
        using var fx = new ValidatorFixture();
        fx.Renderer.PageCount = 3;
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, range: PageRange.Parse("5")));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "did not match any pages");
        StringAssert.Contains(result.ErrorMessage!, "total pages: 3");
    }

    [TestMethod]
    public async Task Validate_PageCountInspectionThrows_Fails()
    {
        using var fx = new ValidatorFixture(new ThrowingRenderer(".txt"));
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "Failed to inspect document pages");
        StringAssert.Contains(result.ErrorMessage!, "corrupt document");
    }

    [TestMethod]
    public async Task Validate_SingleCopyAndAllPages_Succeeds()
    {
        using var fx = new ValidatorFixture();
        var path = fx.WriteFile();

        var result = await fx.Validator.ValidateAsync(Request(path, copies: 1, range: PageRange.Parse("all")));

        Assert.IsTrue(result.IsValid);
    }

    private static PrintJobRequest Request(string filePath, string? printer = "Fake Printer", int copies = 1, PageRange? range = null) => new()
    {
        FilePath = filePath,
        TargetPrinterName = printer,
        Copies = copies,
        PageRange = range ?? PageRange.All
    };

    private sealed class ValidatorFixture : IDisposable
    {
        public ValidatorFixture(IDocumentRenderer? renderer = null)
        {
            var effective = renderer ?? Renderer;
            Resolver = new FakeDocumentRendererResolver(new Dictionary<string, IDocumentRenderer>
            {
                [".txt"] = effective,
                [".pdf"] = effective
            });
            Validator = new PrintJobValidator(Discovery, Resolver);
            Discovery.Printers.Add(TestData.Printer("Fake Printer", isDefault: true));
        }

        public TempDirectory Temp { get; } = new();
        public FakePrinterDiscoveryService Discovery { get; } = new();
        public FakeDocumentRenderer Renderer { get; } = new(".txt", ".pdf");
        public FakeDocumentRendererResolver Resolver { get; }
        public PrintJobValidator Validator { get; }

        public string WriteFile(string name = "doc.txt", string content = "hello")
        {
            var path = Temp.Combine(name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Temp.Dispose();
    }

    private sealed class ThrowingRenderer(string extension) : IDocumentRenderer
    {
        public bool CanHandle(string fileExtension) =>
            string.Equals(fileExtension, extension, StringComparison.OrdinalIgnoreCase);

        public Task<int> GetPageCountAsync(string filePath) =>
            Task.FromException<int>(new InvalidOperationException("corrupt document"));

        public Task RenderPageAsync(string filePath, int pageNumber, Graphics graphics, Rectangle printableArea, int dpi, bool fitToPage) =>
            Task.CompletedTask;
    }
}
