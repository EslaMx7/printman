using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Services;
using Printman.Services.Cups;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Cups;

[TestClass]
public sealed class CupsPrintServiceTests
{
    private static readonly PaperSizeOption A4 = new("A4", 0, 827, 1169) { Keyword = "iso_a4_210x297mm" };
    private static readonly PaperSizeOption Letter = new("Letter", 0, 850, 1100) { Keyword = "na_letter_8.5x11in" };
    private static readonly PaperSizeOption NoKeyword = new("Odd", 0, 500, 500);

    private sealed class Fixture : IDisposable
    {
        public TempDirectory Temp { get; } = new();
        public FakeCupsServer Server { get; } = new();
        public FakePrinterDiscoveryService Discovery { get; } = new();
        public FakeDocumentRenderer Renderer { get; } = new(".pdf", ".txt", ".png", ".jpg", ".jpeg", ".pwg", ".urf", ".dat", ".tif", ".tiff", ".gif", ".bmp",
            ".log", ".csv", ".json", ".md", ".xml", ".ini", ".yaml", ".yml", ".sql", ".cmd", ".ps1");
        public FakeExternalTools Tools { get; } = new();
        public List<string> Progress { get; } = [];
        public CupsPrintService Service { get; }

        public Fixture(bool color = true, bool duplex = true, IReadOnlyList<PaperSizeOption>? papers = null)
        {
            Discovery.Printers.Add(TestData.Printer("PDF", isDefault: true, supportsColor: color, canDuplex: duplex, paperSizes: papers ?? [A4, Letter, NoKeyword]));
            var resolver = new FakeDocumentRendererResolver(Renderer.Extensions.ToDictionary(e => e, _ => (IDocumentRenderer)Renderer));
            Service = new CupsPrintService(Discovery, resolver, new PrintJobValidator(Discovery, resolver), Server.CreateClient(), Tools);
            Server.Respond = r => FakeCupsServer.Ok(m => m.AddGroup(IppTag.JobAttributes).AddInteger("job-id", 7));
            Renderer.PageCount = 5;
        }

        public string Doc(string name = "doc.pdf")
        {
            var path = Temp.Combine(name);
            File.WriteAllBytes(path, "%PDF-1.4 x"u8.ToArray());
            return path;
        }

        public Task<PrintJobResult> PrintAsync(PrintJobRequest request) =>
            Service.PrintAsync(request, new SyncProgress(Progress));

        public IppAttributeGroup SentJob => Server.Requests.Single().Message.Group(IppTag.JobAttributes)!;
        public IppAttributeGroup SentOp => Server.Requests.Single().Message.Group(IppTag.OperationAttributes)!;

        public void Dispose() => Temp.Dispose();
    }

    /// <summary>IProgress that records synchronously (Progress&lt;T&gt; posts to a context).</summary>
    private sealed class SyncProgress(List<string> sink) : IProgress<string>
    {
        public void Report(string value) => sink.Add(value);
    }

    [TestMethod]
    public async Task PrintAsync_SubmitsOriginalDocument_WithJobTemplate()
    {
        using var fx = new Fixture();
        var doc = fx.Doc();

        var result = await fx.PrintAsync(new PrintJobRequest
        {
            FilePath = doc,
            TargetPrinterName = "PDF",
            PageRange = PageRange.Parse("1-2,4"),
            PaperSizeName = "a4",
            Copies = 3,
            Orientation = PrintOrientation.Landscape,
            Duplex = PrintDuplex.Vertical,
            ColorMode = PrintColorMode.Monochrome,
            FitToPage = true,
            JobTitle = "Report"
        });

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual("PDF", result.PrinterUsed);
        Assert.AreEqual(3, result.PagesPrinted);
        Assert.AreEqual(3, result.CopiesPrinted);
        Assert.AreEqual("A4", result.PaperSizeUsed);

        var sent = fx.Server.Requests.Single();
        Assert.AreEqual("/printers/PDF", sent.Resource);
        Assert.AreEqual(IppOperation.PrintJob, sent.Message.Code);
        CollectionAssert.AreEqual("%PDF-1.4 x"u8.ToArray(), sent.Document);
        Assert.AreEqual("ipp://localhost/printers/PDF", fx.SentOp.Get("printer-uri")!.First!.AsString());
        Assert.AreEqual("Report", fx.SentOp.Get("job-name")!.First!.AsString());
        Assert.AreEqual("application/pdf", fx.SentOp.Get("document-format")!.First!.AsString());

        Assert.AreEqual(3, fx.SentJob.Get("copies")!.First!.AsInt());
        CollectionAssert.AreEqual(new[] { new IppRange(1, 2), new IppRange(4, 4) }, fx.SentJob.Get("page-ranges")!.Values.Select(v => v.AsRange()!.Value).ToArray());
        Assert.AreEqual("iso_a4_210x297mm", fx.SentJob.Get("media")!.First!.AsString());
        Assert.AreEqual("two-sided-long-edge", fx.SentJob.Get("sides")!.First!.AsString());
        Assert.AreEqual(4, fx.SentJob.Get("orientation-requested")!.First!.AsInt());
        Assert.AreEqual("monochrome", fx.SentJob.Get("print-color-mode")!.First!.AsString());
        Assert.AreEqual("fit", fx.SentJob.Get("print-scaling")!.First!.AsString());

        StringAssert.Contains(string.Join("\n", fx.Progress), "Paper size set to: A4");
        StringAssert.Contains(string.Join("\n", fx.Progress), "Print job #7 queued on 'PDF'");
    }

    [TestMethod]
    public async Task PrintAsync_Defaults_SendMinimalTemplate_AndUseJobTitleFallback()
    {
        using var fx = new Fixture();
        var doc = fx.Doc("notes.txt");
        fx.Server.Respond = _ => FakeCupsServer.Ok(); // no job-id in the reply

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = doc, FullPage = true });

        Assert.IsTrue(result.Success);
        Assert.AreEqual(5, result.PagesPrinted);
        Assert.IsNull(result.PaperSizeUsed);
        Assert.AreEqual("notes.txt", fx.SentOp.Get("job-name")!.First!.AsString());
        Assert.AreEqual("text/plain", fx.SentOp.Get("document-format")!.First!.AsString());
        Assert.AreEqual(0, fx.SentJob.Attributes.Count, "all pages, 1 copy, defaults: nothing to send");
        StringAssert.Contains(string.Join("\n", fx.Progress), "Print job queued on 'PDF'");
    }

    [TestMethod]
    public async Task PrintAsync_OptionsThePrinterLacks_AreNotSent()
    {
        using var fx = new Fixture(color: false, duplex: false);

        await fx.PrintAsync(new PrintJobRequest
        {
            FilePath = fx.Doc(),
            Duplex = PrintDuplex.Horizontal,
            ColorMode = PrintColorMode.Color,
            Orientation = PrintOrientation.Portrait,
            FitToPage = false,
            Copies = 1000
        });

        Assert.IsNull(fx.SentJob.Get("sides"));
        Assert.IsNull(fx.SentJob.Get("print-color-mode"));
        Assert.AreEqual(3, fx.SentJob.Get("orientation-requested")!.First!.AsInt());
        Assert.AreEqual("none", fx.SentJob.Get("print-scaling")!.First!.AsString());
        Assert.AreEqual(999, fx.SentJob.Get("copies")!.First!.AsInt(), "copies are clamped");
    }

    [TestMethod]
    public async Task PrintAsync_DuplexAndColorVariants()
    {
        using var fx = new Fixture();
        await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), Duplex = PrintDuplex.Simplex, ColorMode = PrintColorMode.Color });
        Assert.AreEqual("one-sided", fx.SentJob.Get("sides")!.First!.AsString());
        Assert.AreEqual("color", fx.SentJob.Get("print-color-mode")!.First!.AsString());

        fx.Server.Requests.Clear();
        await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), Duplex = PrintDuplex.Horizontal, ColorMode = PrintColorMode.Default });
        Assert.AreEqual("two-sided-short-edge", fx.SentJob.Get("sides")!.First!.AsString());
        Assert.IsNull(fx.SentJob.Get("print-color-mode"));

        fx.Server.Requests.Clear();
        await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), Duplex = PrintDuplex.Default });
        Assert.IsNull(fx.SentJob.Get("sides"));
    }

    [TestMethod]
    public async Task PrintAsync_PaperSize_MatchesKeyword_Substring_OrWarns()
    {
        using var fx = new Fixture();

        await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), PaperSizeName = "na_letter_8.5x11in" });
        Assert.AreEqual("na_letter_8.5x11in", fx.SentJob.Get("media")!.First!.AsString());

        fx.Server.Requests.Clear();
        await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), PaperSizeName = "lett" });
        Assert.AreEqual("na_letter_8.5x11in", fx.SentJob.Get("media")!.First!.AsString());

        fx.Server.Requests.Clear();
        fx.Progress.Clear();
        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), PaperSizeName = "Odd" });
        Assert.IsNull(fx.SentJob.Get("media"), "a size without a CUPS keyword cannot be requested");
        Assert.IsNull(result.PaperSizeUsed);
        StringAssert.Contains(string.Join("\n", fx.Progress), "Warning: Paper size 'Odd' not found");

        fx.Server.Requests.Clear();
        await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), PaperSizeName = "A3" });
        Assert.IsNull(fx.SentJob.Get("media"));
    }

    [TestMethod]
    public async Task PrintAsync_PageRanges_MergeConsecutivePages()
    {
        using var fx = new Fixture();
        fx.Renderer.PageCount = 10;

        await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), PageRange = PageRange.Parse("1,2,3,5,7,8,10") });

        CollectionAssert.AreEqual(
            new[] { new IppRange(1, 3), new IppRange(5, 5), new IppRange(7, 8), new IppRange(10, 10) },
            fx.SentJob.Get("page-ranges")!.Values.Select(v => v.AsRange()!.Value).ToArray());
    }

    [TestMethod]
    [DataRow("a.png", "image/png")]
    [DataRow("a.JPG", "image/jpeg")]
    [DataRow("a.gif", "image/gif")]
    [DataRow("a.bmp", "image/x-bitmap")]
    [DataRow("a.tif", "image/tiff")]
    [DataRow("a.pwg", "image/pwg-raster")]
    [DataRow("a.urf", "image/urf")]
    [DataRow("a.dat", "application/octet-stream")]
    [DataRow("a.log", "text/plain")]
    [DataRow("a.csv", "text/plain")]
    [DataRow("a.json", "text/plain")]
    [DataRow("a.md", "text/plain")]
    [DataRow("a.xml", "text/plain")]
    [DataRow("a.ini", "text/plain")]
    [DataRow("a.yaml", "text/plain")]
    [DataRow("a.yml", "text/plain")]
    [DataRow("a.sql", "text/plain")]
    [DataRow("a.cmd", "text/plain")]
    [DataRow("a.ps1", "text/plain")]
    [DataRow("a.jpeg", "text/plain", "image/jpeg")]
    [DataRow("a.tiff", "text/plain", "image/tiff")]
    public async Task PrintAsync_DocumentFormat_FollowsExtension(string name, string mime, string? overrideMime = null)
    {
        using var fx = new Fixture();

        await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(name) });

        Assert.AreEqual(overrideMime ?? mime, fx.SentOp.Get("document-format")!.First!.AsString());
    }

    [TestMethod]
    public async Task PrintAsync_WithoutProgress_AndBlankOutputPath_PrintsToQueue()
    {
        using var fx = new Fixture();

        var result = await fx.Service.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), OutputFilePath = "  ", PaperSizeName = "A4" });

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, fx.Server.Requests.Count);
        Assert.AreEqual("A4", result.PaperSizeUsed);
    }

    [TestMethod]
    public async Task PrintAsync_ValidationFailure_ReturnsFailedWithoutContactingCups()
    {
        using var fx = new Fixture();

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Temp.Combine("missing.pdf"), TargetPrinterName = "PDF" });

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage!, "File not found");
        Assert.AreEqual("PDF", result.PrinterUsed);
        Assert.AreEqual(0, fx.Server.Requests.Count);
    }

    [TestMethod]
    public async Task PrintAsync_ValidationFailure_WithoutPrinterName_ReportsUnknown()
    {
        using var fx = new Fixture();

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = "" });

        Assert.AreEqual("Unknown", result.PrinterUsed);
    }

    [TestMethod]
    public async Task PrintAsync_PrinterDisappearsAfterValidation_ReturnsFailed()
    {
        using var fx = new Fixture();
        var doc = fx.Doc();
        int calls = 0;
        fx.Discovery.FindOverride = _ => calls++ == 0 ? fx.Discovery.Printers[0] : null;

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = doc, TargetPrinterName = "PDF" });

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Target printer could not be resolved.", result.ErrorMessage);
    }

    [TestMethod]
    public async Task PrintAsync_DefaultPrinterDisappearsAfterValidation_ReportsDefault()
    {
        using var fx = new Fixture();
        var doc = fx.Doc();
        fx.Discovery.Printers.Clear(); // validation is bypassed, so the service sees no default printer
        var service = new CupsPrintService(fx.Discovery,
            new FakeDocumentRendererResolver(new Dictionary<string, IDocumentRenderer> { [".pdf"] = fx.Renderer }),
            new AlwaysValid(), fx.Server.CreateClient(), fx.Tools);

        var result = await service.PrintAsync(new PrintJobRequest { FilePath = doc });

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Default", result.PrinterUsed);
    }

    [TestMethod]
    public async Task PrintAsync_RangeBeyondDocument_FailsAfterValidation()
    {
        using var fx = new Fixture();
        var doc = fx.Doc();
        var service = new CupsPrintService(fx.Discovery,
            new FakeDocumentRendererResolver(new Dictionary<string, IDocumentRenderer> { [".pdf"] = fx.Renderer }),
            new AlwaysValid(), fx.Server.CreateClient(), fx.Tools);

        var result = await service.PrintAsync(new PrintJobRequest { FilePath = doc, PageRange = PageRange.Parse("9-12") });

        Assert.IsFalse(result.Success);
        Assert.AreEqual("No valid pages to print.", result.ErrorMessage);
    }

    [TestMethod]
    public async Task PrintAsync_CupsRejectsJob_ReturnsFailed()
    {
        using var fx = new Fixture();
        fx.Server.Respond = _ => FakeCupsServer.Error(IppStatus.DocumentFormatNotSupported, "Unsupported format 'application/octet-stream'.");

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc() });

        Assert.IsFalse(result.Success);
        Assert.AreEqual("CUPS error: Unsupported format 'application/octet-stream'.", result.ErrorMessage);
    }

    [TestMethod]
    public async Task PrintToFile_RunsCupsfilterWithQueuePpd_AndWritesPdf()
    {
        using var fx = new Fixture();
        fx.Tools.Installed["cupsfilter"] = "/usr/sbin/cupsfilter";
        var doc = fx.Doc();
        var output = fx.Temp.Combine("out", "nested", "result.pdf");

        var result = await fx.PrintAsync(new PrintJobRequest
        {
            FilePath = doc,
            OutputFilePath = output,
            PageRange = PageRange.Parse("2-3"),
            PaperSizeName = "Letter",
            Orientation = PrintOrientation.Landscape,
            FitToPage = true
        });

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(2, result.PagesPrinted);
        Assert.AreEqual("Letter", result.PaperSizeUsed);
        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual(0, fx.Server.Requests.Count, "no Print-Job for print-to-file");

        var run = fx.Tools.Runs.Single();
        Assert.AreEqual("/usr/sbin/cupsfilter", run.Executable);
        Assert.AreEqual(output, run.StdoutFile);
        var args = string.Join(" ", run.Arguments);
        StringAssert.StartsWith(args, "-m application/vnd.cups-pdf -i application/pdf -o page-ranges=2-3 -o media=na_letter_8.5x11in -o orientation-requested=4 -o print-scaling=fit -p ");
        StringAssert.EndsWith(args, $".ppd {doc}");
        var ppd = run.Arguments[run.Arguments.IndexOf("-p") + 1];
        Assert.IsFalse(File.Exists(ppd), "temporary PPD is removed");
        StringAssert.Contains(string.Join("\n", fx.Progress), "Document written to");
    }

    [TestMethod]
    public async Task PrintToFile_NoPpd_UnknownFormat_SinglePages_OmitsOptionalArguments()
    {
        using var fx = new Fixture();
        fx.Tools.Installed["cupsfilter"] = "/usr/sbin/cupsfilter";
        fx.Server.Ppd = null;
        var doc = fx.Doc("raw.dat");

        var result = await fx.PrintAsync(new PrintJobRequest
        {
            FilePath = doc,
            OutputFilePath = fx.Temp.Combine("r.pdf"),
            PageRange = PageRange.Parse("1,3"),
            FullPage = true
        });

        Assert.IsTrue(result.Success, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "-m", "application/vnd.cups-pdf", "-o", "page-ranges=1,3", doc }, fx.Tools.Runs.Single().Arguments);
    }

    [TestMethod]
    public async Task PrintToFile_WithoutCupsfilter_Fails()
    {
        using var fx = new Fixture();

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), OutputFilePath = fx.Temp.Combine("r.pdf") });

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage!, "needs 'cupsfilter'");
        Assert.AreEqual(0, fx.Tools.Runs.Count);
    }

    [TestMethod]
    public async Task PrintToFile_CupsfilterFails_ReportsFirstErrorLine_AndDeletesOutput()
    {
        using var fx = new Fixture();
        fx.Tools.Installed["cupsfilter"] = "/usr/sbin/cupsfilter";
        fx.Tools.ExitCode = 1;
        fx.Tools.StandardError = "DEBUG: starting\nERROR: universal filter failed.\nERROR: later line\n";
        var output = fx.Temp.Combine("r.pdf");

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), OutputFilePath = output });

        Assert.IsFalse(result.Success);
        Assert.AreEqual("cupsfilter could not convert the document (ERROR: universal filter failed.)", result.ErrorMessage);
        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public async Task PrintToFile_EmptyOutput_FailsWithoutDetail()
    {
        using var fx = new Fixture();
        fx.Tools.Installed["cupsfilter"] = "/usr/sbin/cupsfilter";
        fx.Tools.StdoutFileContent = [];
        fx.Tools.StandardError = "no ERROR prefix here";

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), OutputFilePath = fx.Temp.Combine("r.pdf") });

        Assert.IsFalse(result.Success);
        Assert.AreEqual("cupsfilter could not convert the document.", result.ErrorMessage);
    }

    [TestMethod]
    public async Task PrintToFile_NoOutputFile_Fails()
    {
        using var fx = new Fixture();
        fx.Tools.Installed["cupsfilter"] = "/usr/sbin/cupsfilter";
        fx.Tools.StdoutFileContent = null;

        var result = await fx.PrintAsync(new PrintJobRequest { FilePath = fx.Doc(), OutputFilePath = fx.Temp.Combine("r.pdf") });

        Assert.IsFalse(result.Success);
    }

    private sealed class AlwaysValid : IPrintJobValidator
    {
        public Task<ValidationResult> ValidateAsync(PrintJobRequest request) => Task.FromResult(ValidationResult.Ok());
    }
}
