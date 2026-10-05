using System.Net;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Services;
using Printman.Services.Ipp;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Ipp;

/// <summary>
/// Branch-focused coverage for <see cref="IppRequestHandler"/>: default/fallback values,
/// every job-template mapping arm, media resolution paths and job lookup by URI.
/// </summary>
[TestClass]
public sealed class IppRequestHandlerBranchTests
{
    // ------------------------------------------------------------------ Validate-Job

    [TestMethod]
    public async Task ValidateJob_WithoutDocumentFormat_IsAccepted()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.ValidateJob);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task ValidateJob_WithOctetStream_IsAccepted()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.ValidateJob);
        request.Group(IppTag.OperationAttributes)!.AddKeyword("document-format", IppDocumentFormats.OctetStream);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task ValidateJob_WithSupportedFormat_IsAccepted()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.ValidateJob);
        request.Group(IppTag.OperationAttributes)!.AddKeyword("document-format", IppDocumentFormats.Pdf);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    // ------------------------------------------------------------------ job identity fallbacks

    [TestMethod]
    public async Task PrintJob_NoUserNoName_UsesRemoteAddressAndDefaultName()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var job = h.Store.Get(JobId(response))!;
        Assert.AreEqual("192.168.1.50", job.UserName);
        Assert.AreEqual("Network print job", job.Name);
    }

    [TestMethod]
    public async Task PrintJob_DocumentNameOnly_UsesDocumentName()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        request.Group(IppTag.OperationAttributes)!.AddName("document-name", "report.pdf");

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual("report.pdf", h.Store.Get(JobId(response))!.Name);
    }

    [TestMethod]
    public async Task PrintJob_JobNameAndUser_AreUsed()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var op = request.Group(IppTag.OperationAttributes)!;
        op.AddName("job-name", "My Job");
        op.AddName("requesting-user-name", "tester");

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        var job = h.Store.Get(JobId(response))!;
        Assert.AreEqual("My Job", job.Name);
        Assert.AreEqual("tester", job.UserName);
    }

    [TestMethod]
    public async Task PrintJob_NoRemoteNoUser_FallsBackToAnonymous()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var context = new IppRequestContext(
            new MemoryStream(IppTestMessages.Body(request, IppTestMessages.PdfDoc()), writable: false),
            PrinterSlug: null,
            Host: IppTestMessages.Host,
            RemoteAddress: null);

        var bytes = await h.Handler.HandleAsync(context, CancellationToken.None);
        var response = IppMessageReader.Parse(bytes);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual("anonymous", h.Store.Get(JobId(response))!.UserName);
    }

    [TestMethod]
    public async Task IdentifyPrinter_NoUserNoRemote_UsesGenericClient()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.IdentifyPrinter);
        var context = new IppRequestContext(
            new MemoryStream(IppTestMessages.Body(request), writable: false),
            PrinterSlug: null,
            Host: IppTestMessages.Host,
            RemoteAddress: null);

        var bytes = await h.Handler.HandleAsync(context, CancellationToken.None);

        Assert.AreEqual((short)IppStatus.Ok, IppMessageReader.Parse(bytes).Code);
    }

    // ------------------------------------------------------------------ job-template parsing

    [TestMethod]
    public async Task PrintJob_AllTemplateOptions_AreMapped()
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, job =>
        {
            job.AddInteger("copies", 4);
            job.AddKeyword("sides", "two-sided-short-edge");
            job.AddKeyword("print-color-mode", "monochrome");
            job.AddEnum("orientation-requested", 5);
            job.AddKeyword("print-scaling", "none");
            job.AddName("media", "iso_a4_210x297mm");
        });

        var options = h.Store.Get(id)!.Options;
        Assert.AreEqual(4, options.Copies);
        Assert.AreEqual(PrintDuplex.Horizontal, options.Duplex);
        Assert.AreEqual(PrintColorMode.Monochrome, options.ColorMode);
        Assert.AreEqual(PrintOrientation.Landscape, options.Orientation);
        Assert.IsFalse(options.FitToPage);
        Assert.AreEqual("A4", options.PaperSizeName);
    }

    [TestMethod]
    [DataRow("one-sided", PrintDuplex.Simplex)]
    [DataRow("two-sided-long-edge", PrintDuplex.Vertical)]
    [DataRow("two-sided-short-edge", PrintDuplex.Horizontal)]
    [DataRow("unexpected", PrintDuplex.Default)]
    public async Task PrintJob_SidesValues_AreMapped(string sides, PrintDuplex expected)
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, job => job.AddKeyword("sides", sides));
        Assert.AreEqual(expected, h.Store.Get(id)!.Options.Duplex);
    }

    [TestMethod]
    [DataRow("color", PrintColorMode.Color)]
    [DataRow("monochrome", PrintColorMode.Monochrome)]
    [DataRow("process-monochrome", PrintColorMode.Monochrome)]
    [DataRow("auto-monochrome", PrintColorMode.Monochrome)]
    [DataRow("bi-level", PrintColorMode.Monochrome)]
    [DataRow("auto", PrintColorMode.Default)]
    public async Task PrintJob_ColorModeValues_AreMapped(string mode, PrintColorMode expected)
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, job => job.AddKeyword("print-color-mode", mode));
        Assert.AreEqual(expected, h.Store.Get(id)!.Options.ColorMode);
    }

    [TestMethod]
    [DataRow(3, PrintOrientation.Portrait)]
    [DataRow(4, PrintOrientation.Landscape)]
    [DataRow(5, PrintOrientation.Landscape)]
    [DataRow(6, PrintOrientation.Portrait)]
    [DataRow(7, PrintOrientation.Auto)]
    public async Task PrintJob_OrientationValues_AreMapped(int orientation, PrintOrientation expected)
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, job => job.AddEnum("orientation-requested", orientation));
        Assert.AreEqual(expected, h.Store.Get(id)!.Options.Orientation);
    }

    [TestMethod]
    public async Task PrintJob_NoTemplateAttributes_UsesDefaults()
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, _ => { });

        var options = h.Store.Get(id)!.Options;
        Assert.AreEqual(1, options.Copies);
        Assert.AreEqual(PrintDuplex.Default, options.Duplex);
        Assert.AreEqual(PrintColorMode.Default, options.ColorMode);
        Assert.AreEqual(PrintOrientation.Auto, options.Orientation);
        Assert.IsTrue(options.FitToPage);
        Assert.IsNull(options.PaperSizeName);
    }

    [TestMethod]
    public async Task PrintJob_MediaColWithSizeName_ResolvesPaper()
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, job =>
        {
            var col = new IppCollection();
            col.AddKeyword("media-size-name", "iso_a4_210x297mm");
            job.AddCollection("media-col", col);
        });

        Assert.AreEqual("A4", h.Store.Get(id)!.Options.PaperSizeName);
    }

    [TestMethod]
    public async Task PrintJob_MediaColWithDimensions_ResolvesPaper()
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, job =>
        {
            var size = new IppCollection();
            size.AddInteger("x-dimension", 21000);
            size.AddInteger("y-dimension", 29700);
            var col = new IppCollection();
            col.AddCollection("media-size", size);
            job.AddCollection("media-col", col);
        });

        Assert.AreEqual("A4", h.Store.Get(id)!.Options.PaperSizeName);
    }

    [TestMethod]
    public async Task PrintJob_UnknownMedia_LeavesPaperUnset()
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, job => job.AddName("media", "not-a-real-media-size"));

        Assert.IsNull(h.Store.Get(id)!.Options.PaperSizeName);
    }

    [TestMethod]
    public async Task PrintJob_PageRanges_AreParsed()
    {
        using var h = new Harness();
        var id = await SubmitPrintJob(h, job => job.Add("page-ranges", IppTag.RangeOfInteger, new IppRange(1, 2), new IppRange(5, 5)));

        Assert.AreEqual("1,2,5", string.Join(',', h.Store.Get(id)!.Options.PageRange.ResolvePages(10)));
    }

    // ------------------------------------------------------------------ Send-Document / Get-Jobs / Cancel

    [TestMethod]
    public async Task SendDocument_LastDocumentFalse_IsAccepted()
    {
        using var h = new Harness();
        var create = await SendAsync(h, IppTestMessages.New(IppOperation.CreateJob));
        int id = JobId(create);

        var send = IppTestMessages.New(IppOperation.SendDocument);
        var op = send.Group(IppTag.OperationAttributes)!;
        op.AddInteger("job-id", id);
        op.AddBoolean("last-document", false);

        var response = await SendAsync(h, send, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task GetJobs_WithoutAttributes_UsesDefaults()
    {
        using var h = new Harness();
        await SendAsync(h, IppTestMessages.New(IppOperation.CreateJob));

        var response = await SendAsync(h, IppTestMessages.New(IppOperation.GetJobs));

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task GetJobs_WithExplicitFilters_IsAccepted()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetJobs);
        var op = request.Group(IppTag.OperationAttributes)!;
        op.AddKeyword("which-jobs", "all");
        op.AddBoolean("my-jobs", true);
        op.AddName("requesting-user-name", "tester");
        op.AddInteger("limit", 1);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task CancelMyJobs_WithAndWithoutUser_IsAccepted()
    {
        using var h = new Harness();
        await SendAsync(h, IppTestMessages.New(IppOperation.CreateJob));

        var withUser = IppTestMessages.New(IppOperation.CancelMyJobs);
        withUser.Group(IppTag.OperationAttributes)!.AddName("requesting-user-name", "tester");
        Assert.AreEqual((short)IppStatus.Ok, (await SendAsync(h, withUser)).Code);

        var withoutUser = IppTestMessages.New(IppOperation.CancelMyJobs);
        Assert.AreEqual((short)IppStatus.Ok, (await SendAsync(h, withoutUser)).Code);
    }

    [TestMethod]
    public async Task CancelJob_ByJobUri_IsAccepted()
    {
        using var h = new Harness();
        var create = await SendAsync(h, IppTestMessages.New(IppOperation.CreateJob));
        int id = JobId(create);

        var cancel = IppTestMessages.New(IppOperation.CancelJob);
        cancel.Group(IppTag.OperationAttributes)!.AddUri("job-uri", $"ipp://{IppTestMessages.Host}/ipp/print/fake-printer/{id}");

        var response = await SendAsync(h, cancel);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.IsTrue(h.Store.Get(id)!.GetState().State is IppJobState.Canceled or IppJobState.Pending);
    }

    [TestMethod]
    public async Task CancelJob_UnknownUriTail_ReturnsNotFound()
    {
        using var h = new Harness();
        var cancel = IppTestMessages.New(IppOperation.CancelJob);
        cancel.Group(IppTag.OperationAttributes)!.AddUri("job-uri", "ipp://host/ipp/print/fake-printer/not-a-number");

        var response = await SendAsync(h, cancel);

        Assert.AreEqual((short)IppStatus.NotFound, response.Code);
    }

    // ------------------------------------------------------------------ helpers / harness

    private static async Task<int> SubmitPrintJob(Harness h, Action<IppAttributeGroup> configureJob)
    {
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var job = request.AddGroup(IppTag.JobAttributes);
        configureJob(job);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());
        Assert.AreEqual((short)IppStatus.Ok, response.Code, StatusMessage(response));
        return JobId(response);
    }

    private static async Task<IppMessage> SendAsync(Harness h, IppMessage request, byte[]? document = null)
    {
        var bytes = await h.Handler.HandleAsync(IppTestMessages.Context(request, document), CancellationToken.None);
        return IppMessageReader.Parse(bytes);
    }

    private static int JobId(IppMessage response) =>
        response.Group(IppTag.JobAttributes)!.Get("job-id")!.First!.AsInt()!.Value;

    private static string? StatusMessage(IppMessage message) =>
        message.Group(IppTag.OperationAttributes)?.Get("status-message")?.First?.AsString();

    private sealed class Harness : IDisposable
    {
        private readonly TempDirectory _cacheDir = new();

        public Harness()
        {
            Cache = new FileCacheService(_cacheDir.Path);
            Formats = new IppDocumentFormats(
                new IDocumentRenderer[] { new FakeDocumentRenderer(".pdf", ".jpg", ".urf", ".pwg") });
            var attributeBuilder = new IppPrinterAttributeBuilder(Registry, Store, Formats, Settings);
            Handler = new IppRequestHandler(Registry, Store, attributeBuilder, Formats, Cache, Pipeline, Events, Settings);

            Registry.SharedPrinters.Add(IppTestMessages.Printer());
            Registry.Capabilities["Fake Printer"] = TestData.Printer();
            Registry.Statuses["Fake Printer"] = TestData.Status();
        }

        public FakeSharedPrinterRegistry Registry { get; } = new();
        public IppJobStore Store { get; } = new();
        public PrintEventHub Events { get; } = new();
        public FakePrintJobPipeline Pipeline { get; } = new();
        public IppServerSettings Settings { get; } = new() { MaxJobBytes = 1_000_000, MaxPendingJobs = 50 };
        public IFileCacheService Cache { get; }
        public IppDocumentFormats Formats { get; }
        public IppRequestHandler Handler { get; }

        public void Dispose() => _cacheDir.Dispose();
    }
}
