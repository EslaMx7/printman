using System.Text;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Services;
using Printman.Services.Ipp;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Ipp;

/// <summary>
/// Unit tests for <see cref="IppRequestHandler"/>. The handler is exercised through
/// real IPP wire messages so every operation, status code and job-template parser branch is validated.
/// </summary>
[TestClass]
public sealed class IppRequestHandlerTests
{
    private const string DefaultSlug = "fake-printer";

    // ------------------------------------------------------------------ Protocol / dispatch

    [TestMethod]
    public async Task Handle_MalformedBody_ReturnsBadRequest()
    {
        using var h = new Harness();

        var response = await SendRawAsync(h, [0x02, 0x00]);

        Assert.AreEqual((short)IppStatus.BadRequest, response.Code);
        Assert.AreEqual(2, response.VersionMajor);
    }

    [TestMethod]
    public async Task Handle_TruncatedAttributeValue_ReturnsBadRequest()
    {
        using var h = new Harness();

        // Valid header + operation group, then an attribute claiming 10 bytes with none present.
        byte[] body =
        [
            0x02, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01,
            0x01, 0x41, 0x00, 0x01, (byte)'x', 0x00, 0x0A
        ];

        var response = await SendRawAsync(h, body);

        Assert.AreEqual((short)IppStatus.BadRequest, response.Code);
    }

    [TestMethod]
    public async Task Handle_AttributeBeforeGroup_ReturnsBadRequest()
    {
        using var h = new Harness();

        byte[] body =
        [
            0x02, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01,
            0x41, 0x00, 0x01, (byte)'x', 0x00, 0x01, (byte)'y'
        ];

        var response = await SendRawAsync(h, body);

        Assert.AreEqual((short)IppStatus.BadRequest, response.Code);
    }

    [TestMethod]
    public async Task Handle_Version0_ReturnsVersionNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);
        request.VersionMajor = 0;

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.VersionNotSupported, response.Code);
        Assert.AreEqual(2, response.VersionMajor);
    }

    [TestMethod]
    public async Task Handle_Version1_IsAccepted()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);
        request.VersionMajor = 1;
        request.VersionMinor = 1;

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(1, response.VersionMajor);
        Assert.AreEqual(1, response.VersionMinor);
    }

    [TestMethod]
    public async Task Handle_Version3_ReturnsVersionNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);
        request.VersionMajor = 3;

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.VersionNotSupported, response.Code);
        Assert.AreEqual(2, response.VersionMajor);
    }

    [TestMethod]
    public async Task Handle_UnknownSlug_ReturnsNotFound()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);

        var response = await SendAsync(h, request, slug: "unknown-printer");

        Assert.AreEqual((short)IppStatus.NotFound, response.Code);
    }

    [TestMethod]
    public async Task Handle_UnknownOperation_ReturnsOperationNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(0x0099);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.OperationNotSupported, response.Code);
    }

    [TestMethod]
    public async Task Handle_OperationCanceled_IsRethrown()
    {
        using var h = new Harness();
        h.Pipeline.TicketFactory = _ => throw new OperationCanceledException();

        var request = IppTestMessages.New(IppOperation.PrintJob);
        var context = IppTestMessages.Context(request, IppTestMessages.PdfDoc());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => h.Handler.HandleAsync(context, CancellationToken.None));
    }

    [TestMethod]
    public async Task Handle_UnexpectedException_ReturnsInternalError()
    {
        using var h = new Harness();
        h.Pipeline.TicketFactory = _ => throw new InvalidOperationException("boom");

        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.InternalError, response.Code);
    }

    // ------------------------------------------------------------------ Get-Printer-Attributes

    [TestMethod]
    public async Task GetPrinterAttributes_NoRequestedAttributes_ReturnsFullDescription()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var printer = response.Group(IppTag.PrinterAttributes);
        Assert.IsNotNull(printer);
        Assert.AreEqual(
            "ipp://printman.local:631/ipp/print/fake-printer",
            printer.Get("printer-uri-supported")!.First!.AsString());
        Assert.IsNotNull(printer.Get("printer-name"));
        Assert.AreEqual("http://printman.local:5000/", printer.Get("printer-more-info")!.First!.AsString());
    }

    [TestMethod]
    public async Task GetPrinterAttributes_FiltersRequestedAttributes()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);
        request.Group(IppTag.OperationAttributes)!.AddKeywords("requested-attributes", ["printer-name"]);

        var response = await SendAsync(h, request);

        var printer = response.Group(IppTag.PrinterAttributes)!;
        Assert.IsNotNull(printer.Get("printer-name"));
        Assert.IsNull(printer.Get("printer-uri-supported"));
        Assert.IsNull(printer.Get("media-col-database"));
    }

    [TestMethod]
    public async Task GetPrinterAttributes_MediaColDatabase_OnlyWhenRequested()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);
        request.Group(IppTag.OperationAttributes)!.AddKeywords("requested-attributes", ["media-col-database"]);

        var response = await SendAsync(h, request);

        var printer = response.Group(IppTag.PrinterAttributes)!;
        Assert.IsNotNull(printer.Get("media-col-database"));
        Assert.IsNull(printer.Get("printer-name"));
    }

    [TestMethod]
    public async Task GetPrinterAttributes_NonStringRequestedAttributes_TreatedAsEverything()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);
        request.Group(IppTag.OperationAttributes)!.AddInteger("requested-attributes", 5, 6);

        var response = await SendAsync(h, request);

        var printer = response.Group(IppTag.PrinterAttributes)!;
        Assert.IsNotNull(printer.Get("printer-name"));
    }

    [TestMethod]
    public async Task GetPrinterAttributes_NoWebUi_PointsAtIppInfoPage()
    {
        using var h = new Harness();
        h.Settings.WebUiEnabled = false;
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);

        var response = await SendAsync(h, request);

        var printer = response.Group(IppTag.PrinterAttributes)!;
        Assert.AreEqual(
            "http://printman.local:631/ipp/print/fake-printer",
            printer.Get("printer-more-info")!.First!.AsString());
    }

    [TestMethod]
    [DataRow("printman.local", "http://printman.local:5000/")]
    [DataRow("[::1]:631", "http://[::1]:5000/")]
    public async Task GetPrinterAttributes_HostVariants_BuildWebUri(string host, string expected)
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetPrinterAttributes);

        var response = await SendAsync(h, request, host: host);

        var printer = response.Group(IppTag.PrinterAttributes)!;
        Assert.AreEqual(expected, printer.Get("printer-more-info")!.First!.AsString());
    }

    // ------------------------------------------------------------------ Validate-Job

    [TestMethod]
    public async Task ValidateJob_NoFormat_ReturnsOk()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.ValidateJob);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task ValidateJob_OctetStream_ReturnsOk()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.ValidateJob);
        request.Group(IppTag.OperationAttributes)!.AddKeyword("document-format", IppDocumentFormats.OctetStream);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task ValidateJob_SupportedFormat_ReturnsOk()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.ValidateJob);
        request.Group(IppTag.OperationAttributes)!.AddKeyword("document-format", IppDocumentFormats.Pdf);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task ValidateJob_UnsupportedFormat_ReturnsDocumentFormatNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.ValidateJob);
        request.Group(IppTag.OperationAttributes)!.AddKeyword("document-format", "application/postscript");

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.DocumentFormatNotSupported, response.Code);
        Assert.IsNotNull(StatusMessage(response));
    }

    // ------------------------------------------------------------------ Print-Job

    [TestMethod]
    public async Task PrintJob_Pdf_SucceedsAndEnqueues()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var jobGroup = response.Group(IppTag.JobAttributes)!;
        Assert.IsNotNull(jobGroup);
        Assert.AreEqual("application/pdf", h.Store.Get(JobId(response))!.DocumentFormat);

        Assert.AreEqual(1, h.Pipeline.Enqueued.Count);
        var batch = h.Pipeline.Enqueued[0];
        Assert.AreEqual("ipp", batch.Source);
        Assert.AreEqual("Fake Printer", batch.Printer);
        var item = batch.Items[0];
        Assert.IsTrue(item.FullPage);
        Assert.AreEqual("Network print job (#1, 192.168.1.50)", item.DisplayName);
    }

    [TestMethod]
    public async Task PrintJob_Raster_OverridesOrientationAndFitToPage()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddEnum("orientation-requested", 4);
        job.AddKeyword("print-scaling", "none");

        var response = await SendAsync(h, request, Encoding.ASCII.GetBytes("RaS2 raster page data"));

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var item = h.Pipeline.Enqueued[0].Items[0];
        Assert.AreEqual(PrintOrientation.Portrait, item.Orientation);
        Assert.IsTrue(item.FitToPage);
        Assert.AreEqual("image/pwg-raster", h.Store.Get(JobId(response))!.DocumentFormat);
    }

    [TestMethod]
    public async Task PrintJob_NonRaster_RespectsOrientationAndScaling()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddEnum("orientation-requested", 4);
        job.AddKeyword("print-scaling", "none");

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var item = h.Pipeline.Enqueued[0].Items[0];
        Assert.AreEqual(PrintOrientation.Landscape, item.Orientation);
        Assert.IsFalse(item.FitToPage);
    }

    [TestMethod]
    public async Task PrintJob_DeclaredFormatUsedWhenContentUnknown()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        request.Group(IppTag.OperationAttributes)!.AddKeyword("document-format", IppDocumentFormats.Pdf);

        var response = await SendAsync(h, request, Encoding.ASCII.GetBytes("%XYZ opaque payload"));

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual("application/pdf", h.Store.Get(JobId(response))!.DocumentFormat);
    }

    [TestMethod]
    public async Task PrintJob_UnsupportedContent_ReturnsDocumentFormatNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request, Encoding.ASCII.GetBytes("garbage bytes here"));

        Assert.AreEqual((short)IppStatus.DocumentFormatNotSupported, response.Code);
        Assert.AreEqual(0, h.Pipeline.Enqueued.Count);
    }

    [TestMethod]
    public async Task PrintJob_OctetStreamUnsniffable_ReturnsDocumentFormatNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        request.Group(IppTag.OperationAttributes)!.AddKeyword("document-format", IppDocumentFormats.OctetStream);

        var response = await SendAsync(h, request, Encoding.ASCII.GetBytes("garbage bytes here"));

        Assert.AreEqual((short)IppStatus.DocumentFormatNotSupported, response.Code);
    }

    [TestMethod]
    public async Task PrintJob_UnsupportedDeclaredFormat_ReturnsDocumentFormatNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        request.Group(IppTag.OperationAttributes)!.AddKeyword("document-format", "application/postscript");

        var response = await SendAsync(h, request, Encoding.ASCII.GetBytes("opaque payload"));

        Assert.AreEqual((short)IppStatus.DocumentFormatNotSupported, response.Code);
    }

    [TestMethod]
    public async Task PrintJob_SniffedFormatWithoutRenderer_ReturnsDocumentFormatNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

        var response = await SendAsync(h, request, png);

        Assert.AreEqual((short)IppStatus.DocumentFormatNotSupported, response.Code);
    }

    [TestMethod]
    public async Task PrintJob_EmptyDocument_ReturnsBadRequest()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.BadRequest, response.Code);
    }

    [TestMethod]
    public async Task PrintJob_OversizedDocument_ReturnsRequestEntityTooLarge()
    {
        using var h = new Harness();
        h.Settings.MaxJobBytes = 4;
        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.RequestEntityTooLarge, response.Code);
        Assert.AreEqual(0, h.Pipeline.Enqueued.Count);
    }

    [TestMethod]
    public async Task PrintJob_Busy_ReturnsBusy()
    {
        using var h = new Harness();
        h.Settings.MaxPendingJobs = 0;
        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.Busy, response.Code);
    }

    [TestMethod]
    public async Task PrintJob_Jpeg_Succeeds()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04];

        var response = await SendAsync(h, request, jpeg);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual("image/jpeg", h.Store.Get(JobId(response))!.DocumentFormat);
    }

    [TestMethod]
    public async Task PrintJob_LongAndInvalidJobName_IsSanitizedForCache()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var name = new string('x', 100) + "/bad*name";
        request.AddGroup(IppTag.JobAttributes).AddName("job-name", name);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(name, h.Store.Get(JobId(response))!.Name);
    }

    // ------------------------------------------------------------------ Create-Job / job record

    [TestMethod]
    public async Task CreateJob_DefaultsNameAndUserFromRemote()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.CreateJob);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var job = h.Store.Get(JobId(response))!;
        Assert.AreEqual("Network print job", job.Name);
        Assert.AreEqual("192.168.1.50", job.UserName);
        Assert.AreEqual(IppJobState.Pending, job.GetState().State);
    }

    [TestMethod]
    public async Task CreateJob_UsesJobNameAndUser()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.CreateJob);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddName("job-name", "  My Report  ");
        job.AddName("requesting-user-name", "  alice  ");

        var response = await SendAsync(h, request);

        var created = h.Store.Get(JobId(response))!;
        Assert.AreEqual("My Report", created.Name);
        Assert.AreEqual("alice", created.UserName);
    }

    [TestMethod]
    public async Task CreateJob_UsesDocumentNameWhenNoJobName()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.CreateJob);
        request.AddGroup(IppTag.JobAttributes).AddName("document-name", "doc.pdf");

        var response = await SendAsync(h, request);

        Assert.AreEqual("doc.pdf", h.Store.Get(JobId(response))!.Name);
    }

    [TestMethod]
    public async Task CreateJob_NoRemoteNoUser_UsesAnonymous()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.CreateJob);

        var response = await SendNoRemoteAsync(h, request);

        var job = h.Store.Get(JobId(response))!;
        Assert.AreEqual("anonymous", job.UserName);
    }

    [TestMethod]
    public async Task CreateJob_WhitespaceName_UsesDefault()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.CreateJob);
        request.AddGroup(IppTag.JobAttributes).AddName("job-name", "   ");

        var response = await SendAsync(h, request);

        Assert.AreEqual("Network print job", h.Store.Get(JobId(response))!.Name);
    }

    [TestMethod]
    public async Task CreateJob_Busy_ReturnsBusy()
    {
        using var h = new Harness();
        h.Settings.MaxPendingJobs = 0;
        var request = IppTestMessages.New(IppOperation.CreateJob);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Busy, response.Code);
    }

    // ------------------------------------------------------------------ Job template parsing

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(5, 5)]
    [DataRow(200, 99)]
    public async Task CreateJob_ClampsCopies(int input, int expected)
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.AddInteger("copies", input));

        Assert.AreEqual(expected, Options(h, id).Copies);
    }

    [TestMethod]
    [DataRow("one-sided", PrintDuplex.Simplex)]
    [DataRow("two-sided-long-edge", PrintDuplex.Vertical)]
    [DataRow("two-sided-short-edge", PrintDuplex.Horizontal)]
    [DataRow("unexpected", PrintDuplex.Default)]
    public async Task CreateJob_ParsesSides(string value, PrintDuplex expected)
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.AddKeyword("sides", value));

        Assert.AreEqual(expected, Options(h, id).Duplex);
    }

    [TestMethod]
    [DataRow("color", PrintColorMode.Color)]
    [DataRow("monochrome", PrintColorMode.Monochrome)]
    [DataRow("process-monochrome", PrintColorMode.Monochrome)]
    [DataRow("bi-level", PrintColorMode.Monochrome)]
    [DataRow("process-bi-level", PrintColorMode.Monochrome)]
    [DataRow("auto-monochrome", PrintColorMode.Monochrome)]
    [DataRow("unexpected", PrintColorMode.Default)]
    public async Task CreateJob_ParsesColorMode(string value, PrintColorMode expected)
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.AddKeyword("print-color-mode", value));

        Assert.AreEqual(expected, Options(h, id).ColorMode);
    }

    [TestMethod]
    [DataRow(3, PrintOrientation.Portrait)]
    [DataRow(6, PrintOrientation.Portrait)]
    [DataRow(4, PrintOrientation.Landscape)]
    [DataRow(5, PrintOrientation.Landscape)]
    [DataRow(1, PrintOrientation.Auto)]
    public async Task CreateJob_ParsesOrientation(int value, PrintOrientation expected)
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.AddEnum("orientation-requested", value));

        Assert.AreEqual(expected, Options(h, id).Orientation);
    }

    [TestMethod]
    [DataRow("none", false)]
    [DataRow("auto", true)]
    public async Task CreateJob_ParsesPrintScaling(string value, bool expectedFitToPage)
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.AddKeyword("print-scaling", value));

        Assert.AreEqual(expectedFitToPage, Options(h, id).FitToPage);
    }

    [TestMethod]
    public async Task CreateJob_ParsesPageRanges_AndIgnoresInvalidOnes()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.Add(
            "page-ranges",
            IppTag.RangeOfInteger,
            new IppRange(1, 1),
            new IppRange(2, 4),
            new IppRange(0, 5),
            new IppRange(6, 3)));

        Assert.AreEqual("1,2-4", Options(h, id).PageRange.RawExpression);
    }

    [TestMethod]
    public async Task CreateJob_PageRangeTooLong_FallsBackToAllPages()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.Add(
            "page-ranges",
            IppTag.RangeOfInteger,
            new IppRange(60000, 60001)));

        Assert.IsTrue(Options(h, id).PageRange.IsAllPages);
    }

    [TestMethod]
    public async Task CreateJob_ParsesMediaByName()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.AddKeyword("media", "na_letter_8.5x11in"));

        Assert.AreEqual("Letter", Options(h, id).PaperSizeName);
    }

    [TestMethod]
    public async Task CreateJob_ParsesMediaColByName()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j =>
        {
            var col = new IppCollection();
            col.AddKeyword("media-size-name", "iso_a4_210x297mm");
            j.AddCollection("media-col", col);
        });

        Assert.AreEqual("A4", Options(h, id).PaperSizeName);
    }

    [TestMethod]
    public async Task CreateJob_ParsesMediaColBySize()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j =>
        {
            var size = new IppCollection();
            size.AddInteger("x-dimension", 21590);
            size.AddInteger("y-dimension", 27940);
            var col = new IppCollection();
            col.AddCollection("media-size", size);
            j.AddCollection("media-col", col);
        });

        Assert.AreEqual("Letter", Options(h, id).PaperSizeName);
    }

    [TestMethod]
    public async Task CreateJob_MediaColUnknownName_FallsBackToSize()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j =>
        {
            var size = new IppCollection();
            size.AddInteger("x-dimension", 21000);
            size.AddInteger("y-dimension", 29700);
            var col = new IppCollection();
            col.AddKeyword("media-size-name", "bogus_size");
            col.AddCollection("media-size", size);
            j.AddCollection("media-col", col);
        });

        Assert.AreEqual("A4", Options(h, id).PaperSizeName);
    }

    [TestMethod]
    public async Task CreateJob_MediaColMissingAxis_FallsBackToMediaName()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j =>
        {
            var size = new IppCollection();
            size.AddInteger("x-dimension", 21000);
            var col = new IppCollection();
            col.AddCollection("media-size", size);
            j.AddCollection("media-col", col);
            j.AddKeyword("media", "na_letter_8.5x11in");
        });

        Assert.AreEqual("Letter", Options(h, id).PaperSizeName);
    }

    [TestMethod]
    public async Task CreateJob_MediaColUnknownSize_LeavesPaperNull()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j =>
        {
            var size = new IppCollection();
            size.AddInteger("x-dimension", 1);
            size.AddInteger("y-dimension", 1);
            var col = new IppCollection();
            col.AddCollection("media-size", size);
            j.AddCollection("media-col", col);
        });

        Assert.IsNull(Options(h, id).PaperSizeName);
    }

    [TestMethod]
    public async Task CreateJob_MediaWithoutCapabilities_LeavesPaperNull()
    {
        using var h = new Harness();
        h.Registry.Capabilities.Clear();
        var id = await CreateJobAsync(h, j => j.AddKeyword("media", "iso_a4_210x297mm"));

        Assert.IsNull(Options(h, id).PaperSizeName);
    }

    [TestMethod]
    public async Task CreateJob_NoMediaAttributes_LeavesPaperNull()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);

        Assert.IsNull(Options(h, id).PaperSizeName);
    }

    // ------------------------------------------------------------------ Send-Document

    [TestMethod]
    public async Task SendDocument_JobNotFound_ReturnsNotFound()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.SendDocument);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", 999);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.NotFound, response.Code);
    }

    [TestMethod]
    public async Task SendDocument_SubmitsDocument_ReturnsOk()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.SendDocument);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", id);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(1, h.Pipeline.Enqueued.Count);
        Assert.IsNotNull(h.Store.Get(id)!.Ticket);
    }

    [TestMethod]
    public async Task SendDocument_EmptyLastDocument_AbortsJobWithOkResponse()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.SendDocument);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddInteger("job-id", id);
        job.AddBoolean("last-document", true);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var state = h.Store.Get(id)!.GetState();
        Assert.AreEqual(IppJobState.Aborted, state.State);
        Assert.AreEqual("aborted-by-system", state.Reason);
    }

    [TestMethod]
    public async Task SendDocument_EmptyNonLastDocument_ReturnsBadRequest()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.SendDocument);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddInteger("job-id", id);
        job.AddBoolean("last-document", false);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.BadRequest, response.Code);
        Assert.AreEqual(IppJobState.Aborted, h.Store.Get(id)!.GetState().State);
    }

    [TestMethod]
    public async Task SendDocument_UnsupportedFormat_ReturnsDocumentFormatNotSupported()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.SendDocument);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", id);

        var response = await SendAsync(h, request, Encoding.ASCII.GetBytes("not a known document"));

        Assert.AreEqual((short)IppStatus.DocumentFormatNotSupported, response.Code);
        Assert.AreEqual(IppJobState.Aborted, h.Store.Get(id)!.GetState().State);
    }

    [TestMethod]
    public async Task SendDocument_AfterSubmit_NoMoreData_ReturnsOkClosingJob()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        await SendDocumentAsync(h, id, IppTestMessages.PdfDoc(), lastDocument: true);

        var response = await SendDocumentAsync(h, id, null, lastDocument: true);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(1, h.Pipeline.Enqueued.Count);
    }

    [TestMethod]
    public async Task SendDocument_AfterSubmit_WithMoreData_ReturnsMultipleDocumentNotSupported()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        await SendDocumentAsync(h, id, IppTestMessages.PdfDoc(), lastDocument: true);

        var response = await SendDocumentAsync(h, id, IppTestMessages.PdfDoc(), lastDocument: true);

        Assert.AreEqual((short)IppStatus.MultipleDocumentJobsNotSupported, response.Code);
    }

    [TestMethod]
    public async Task SendDocument_AfterSubmit_NonLast_ReturnsMultipleDocumentNotSupported()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        await SendDocumentAsync(h, id, IppTestMessages.PdfDoc(), lastDocument: true);

        var response = await SendDocumentAsync(h, id, null, lastDocument: false);

        Assert.AreEqual((short)IppStatus.MultipleDocumentJobsNotSupported, response.Code);
    }

    [TestMethod]
    public async Task SendDocument_TerminatedJobWithoutTicket_ReturnsMultipleDocumentNotSupported()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        await CancelAsync(h, id); // Terminates the job without ever attaching a ticket.

        var response = await SendDocumentAsync(h, id, IppTestMessages.PdfDoc(), lastDocument: true);

        Assert.AreEqual((short)IppStatus.MultipleDocumentJobsNotSupported, response.Code);
    }

    // ------------------------------------------------------------------ Close-Job

    [TestMethod]
    public async Task CloseJob_JobNotFound_ReturnsNotFound()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.CloseJob);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", 4242);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.NotFound, response.Code);
    }

    [TestMethod]
    public async Task CloseJob_AwaitingDocument_AbortsJob()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.CloseJob);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", id);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(IppJobState.Aborted, h.Store.Get(id)!.GetState().State);
    }

    [TestMethod]
    public async Task CloseJob_SubmittedJob_KeepsPipelineState()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        await SendDocumentAsync(h, id, IppTestMessages.PdfDoc(), lastDocument: true);
        var request = IppTestMessages.New(IppOperation.CloseJob);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", id);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(IppJobState.Pending, h.Store.Get(id)!.GetState().State);
    }

    // ------------------------------------------------------------------ Get-Job-Attributes

    [TestMethod]
    public async Task GetJobAttributes_JobNotFound_ReturnsNotFound()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetJobAttributes);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", 12345);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.NotFound, response.Code);
    }

    [TestMethod]
    public async Task GetJobAttributes_WrongPrinter_ReturnsNotFound()
    {
        using var h = new Harness();
        var other = h.Store.Create("some-other-slug", "job", "alice", new IppJobOptions());
        var request = IppTestMessages.New(IppOperation.GetJobAttributes);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", other.Id);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.NotFound, response.Code);
    }

    [TestMethod]
    public async Task GetJobAttributes_ReturnsFullGroup()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h, j => j.AddName("job-name", "Report"));
        var request = IppTestMessages.New(IppOperation.GetJobAttributes);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", id);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var job = response.Group(IppTag.JobAttributes)!;
        Assert.AreEqual(id, job.Get("job-id")!.First!.AsInt());
        Assert.AreEqual("Report", job.Get("job-name")!.First!.AsString());
        Assert.IsNull(job.Get("time-at-processing")!.First!.AsInt());
        Assert.IsNull(job.Get("time-at-completed")!.First!.AsInt());
    }

    [TestMethod]
    [DataRow("all", true)]
    [DataRow("job-description", true)]
    [DataRow("job-template", true)]
    [DataRow("job-id", false)]
    public async Task GetJobAttributes_RequestedAttributeFiltering(string requested, bool expectEverything)
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.GetJobAttributes);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddInteger("job-id", id);
        job.AddKeywords("requested-attributes", [requested]);

        var response = await SendAsync(h, request);

        var group = response.Group(IppTag.JobAttributes)!;
        Assert.IsNotNull(group.Get("job-id"));
        if (expectEverything)
        {
            Assert.IsNotNull(group.Get("job-name"));
        }
        else
        {
            Assert.IsNull(group.Get("job-name"));
        }
    }

    [TestMethod]
    public async Task GetJobAttributes_NonStringRequestedAttributes_ReturnsFullGroup()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.GetJobAttributes);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddInteger("job-id", id);
        job.AddInteger("requested-attributes", 5, 6);

        var response = await SendAsync(h, request);

        var group = response.Group(IppTag.JobAttributes)!;
        Assert.IsNotNull(group.Get("job-name"));
    }

    [TestMethod]
    public async Task GetJobAttributes_AfterPrint_IncludesFormatAndTimestamps()
    {
        using var h = new Harness();
        PipelineTicket? ticket = null;
        h.Pipeline.TicketFactory = batch => ticket = new PipelineTicket(batch);
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var printResponse = await SendAsync(h, request, IppTestMessages.PdfDoc());
        var id = JobId(printResponse);

        ticket!.MarkProcessing();
        ticket.AddPagesPrinted(3);
        ticket.Finish(PipelineJobState.Completed);

        var query = IppTestMessages.New(IppOperation.GetJobAttributes);
        query.AddGroup(IppTag.JobAttributes).AddInteger("job-id", id);
        var response = await SendAsync(h, query);

        var job = response.Group(IppTag.JobAttributes)!;
        Assert.AreEqual("application/pdf", job.Get("document-format")!.First!.AsString());
        Assert.IsNotNull(job.Get("time-at-processing")!.First!.AsInt());
        Assert.IsNotNull(job.Get("time-at-completed")!.First!.AsInt());
        Assert.AreEqual(3, job.Get("job-impressions-completed")!.First!.AsInt());
    }

    [TestMethod]
    public async Task GetJobAttributes_ByJobUri_IsResolved()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.GetJobAttributes);
        request.AddGroup(IppTag.JobAttributes)
            .AddUri("job-uri", $"ipp://{IppTestMessages.Host}/ipp/print/{DefaultSlug}/{id}/");

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task GetJobAttributes_NonNumericJobUri_ReturnsNotFound()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetJobAttributes);
        request.AddGroup(IppTag.JobAttributes)
            .AddUri("job-uri", $"ipp://{IppTestMessages.Host}/ipp/print/{DefaultSlug}/not-a-number");

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.NotFound, response.Code);
    }

    // ------------------------------------------------------------------ Get-Jobs

    [TestMethod]
    public async Task GetJobs_Empty_ReturnsOkWithoutJobGroups()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetJobs);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(0, response.Groups.Count(g => g.Tag == IppTag.JobAttributes));
    }

    [TestMethod]
    public async Task GetJobs_NotCompleted_IncludesPendingJobs()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.GetJobs);

        var response = await SendAsync(h, request);

        var groups = response.Groups.Where(g => g.Tag == IppTag.JobAttributes).ToList();
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(id, groups[0].Get("job-id")!.First!.AsInt());
        Assert.IsNotNull(groups[0].Get("job-uri"));
    }

    [TestMethod]
    public async Task GetJobs_CompletedAndNotCompletedAndAll_RespectJobState()
    {
        using var h = new Harness();
        var done = await CreateJobAsync(h);
        await CancelAsync(h, done);
        var pending = await CreateJobAsync(h);

        var completed = await SendJobsAsync(h, "completed");
        Assert.AreEqual(1, completed.Groups.Count(g => g.Tag == IppTag.JobAttributes));
        Assert.AreEqual(done, completed.Groups.First(g => g.Tag == IppTag.JobAttributes).Get("job-id")!.First!.AsInt());

        var notCompleted = await SendJobsAsync(h, "not-completed");
        Assert.AreEqual(1, notCompleted.Groups.Count(g => g.Tag == IppTag.JobAttributes));
        Assert.AreEqual(pending, notCompleted.Groups.First(g => g.Tag == IppTag.JobAttributes).Get("job-id")!.First!.AsInt());

        var all = await SendJobsAsync(h, "all");
        Assert.AreEqual(2, all.Groups.Count(g => g.Tag == IppTag.JobAttributes));
    }

    [TestMethod]
    public async Task GetJobs_MyJobs_FiltersByRequestingUser()
    {
        using var h = new Harness();
        var alice = await CreateJobAsync(h, j => j.AddName("requesting-user-name", "alice"));
        await CreateJobAsync(h, j => j.AddName("requesting-user-name", "bob"));

        var request = IppTestMessages.New(IppOperation.GetJobs);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddBoolean("my-jobs", true);
        job.AddName("requesting-user-name", "ALICE");

        var response = await SendAsync(h, request);

        var groups = response.Groups.Where(g => g.Tag == IppTag.JobAttributes).ToList();
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(alice, groups[0].Get("job-id")!.First!.AsInt());
    }

    [TestMethod]
    public async Task GetJobs_MyJobsWithoutUser_IncludesEveryJob()
    {
        using var h = new Harness();
        await CreateJobAsync(h, j => j.AddName("requesting-user-name", "alice"));
        await CreateJobAsync(h, j => j.AddName("requesting-user-name", "bob"));

        var request = IppTestMessages.New(IppOperation.GetJobs);
        request.AddGroup(IppTag.JobAttributes).AddBoolean("my-jobs", true);

        var response = await SendAsync(h, request);

        Assert.AreEqual(2, response.Groups.Count(g => g.Tag == IppTag.JobAttributes));
    }

    [TestMethod]
    public async Task GetJobs_InvalidWhich_ReturnsAttributesNotSupported()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.GetJobs);
        request.AddGroup(IppTag.JobAttributes).AddKeyword("which-jobs", "sometimes");

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.AttributesOrValuesNotSupported, response.Code);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 1)]
    [DataRow(-5, 1)]
    [DataRow(2, 2)]
    public async Task GetJobs_Limit_TakesAtLeastOne(int limit, int expected)
    {
        using var h = new Harness();
        await CreateJobAsync(h);
        await CreateJobAsync(h);
        await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.GetJobs);
        request.AddGroup(IppTag.JobAttributes).AddInteger("limit", limit);

        var response = await SendAsync(h, request);

        Assert.AreEqual(expected, response.Groups.Count(g => g.Tag == IppTag.JobAttributes));
    }

    [TestMethod]
    public async Task GetJobs_FiltersRequestedAttributes()
    {
        using var h = new Harness();
        await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.GetJobs);
        request.AddGroup(IppTag.JobAttributes).AddKeywords("requested-attributes", ["job-id"]);

        var response = await SendAsync(h, request);

        var group = response.Groups.First(g => g.Tag == IppTag.JobAttributes);
        Assert.IsNotNull(group.Get("job-id"));
        Assert.IsNull(group.Get("job-name"));
    }

    // ------------------------------------------------------------------ Cancel-Job

    [TestMethod]
    public async Task CancelJob_JobNotFound_ReturnsNotFound()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.CancelJob);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", 77);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.NotFound, response.Code);
    }

    [TestMethod]
    public async Task CancelJob_PendingWithoutTicket_CancelsJob()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.CancelJob);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", id);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        var state = h.Store.Get(id)!.GetState();
        Assert.AreEqual(IppJobState.Canceled, state.State);
        Assert.AreEqual("job-canceled-by-user", state.Reason);
    }

    [TestMethod]
    public async Task CancelJob_AlreadyTerminal_ReturnsNotPossible()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        await CancelAsync(h, id);

        var response = await SendCancelAsync(h, id);

        Assert.AreEqual((short)IppStatus.NotPossible, response.Code);
    }

    [TestMethod]
    public async Task CancelJob_WithActiveTicket_CancelsTicket()
    {
        using var h = new Harness();
        PipelineTicket? ticket = null;
        h.Pipeline.TicketFactory = batch => ticket = new PipelineTicket(batch);
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var printResponse = await SendAsync(h, request, IppTestMessages.PdfDoc());
        var id = JobId(printResponse);

        var response = await SendCancelAsync(h, id);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.IsTrue(ticket!.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task CancelJob_WithFinishedTicket_ReturnsNotPossible()
    {
        using var h = new Harness();
        PipelineTicket? ticket = null;
        h.Pipeline.TicketFactory = batch => ticket = new PipelineTicket(batch);
        var request = IppTestMessages.New(IppOperation.PrintJob);
        var printResponse = await SendAsync(h, request, IppTestMessages.PdfDoc());
        var id = JobId(printResponse);
        ticket!.Finish(PipelineJobState.Completed);

        var response = await SendCancelAsync(h, id);

        Assert.AreEqual((short)IppStatus.NotPossible, response.Code);
    }

    // ------------------------------------------------------------------ Cancel-My-Jobs

    [TestMethod]
    public async Task CancelMyJobs_NoFilters_CancelsEveryJob()
    {
        using var h = new Harness();
        var first = await CreateJobAsync(h, j => j.AddName("requesting-user-name", "alice"));
        var second = await CreateJobAsync(h, j => j.AddName("requesting-user-name", "bob"));
        var request = IppTestMessages.New(IppOperation.CancelMyJobs);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(IppJobState.Canceled, h.Store.Get(first)!.GetState().State);
        Assert.AreEqual(IppJobState.Canceled, h.Store.Get(second)!.GetState().State);
    }

    [TestMethod]
    public async Task CancelMyJobs_ByUser_CancelsOnlyMatchingJobs()
    {
        using var h = new Harness();
        var alice = await CreateJobAsync(h, j => j.AddName("requesting-user-name", "alice"));
        var bob = await CreateJobAsync(h, j => j.AddName("requesting-user-name", "bob"));
        var request = IppTestMessages.New(IppOperation.CancelMyJobs);
        request.AddGroup(IppTag.JobAttributes).AddName("requesting-user-name", "ALICE");

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(IppJobState.Canceled, h.Store.Get(alice)!.GetState().State);
        Assert.AreNotEqual(IppJobState.Canceled, h.Store.Get(bob)!.GetState().State);
    }

    [TestMethod]
    public async Task CancelMyJobs_ByJobIds_CancelsOnlyListedJobs()
    {
        using var h = new Harness();
        var first = await CreateJobAsync(h);
        var second = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.CancelMyJobs);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-ids", first);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreEqual(IppJobState.Canceled, h.Store.Get(first)!.GetState().State);
        Assert.AreNotEqual(IppJobState.Canceled, h.Store.Get(second)!.GetState().State);
    }

    [TestMethod]
    public async Task CancelMyJobs_NonIntegerJobIds_CancelsNothing()
    {
        using var h = new Harness();
        var id = await CreateJobAsync(h);
        var request = IppTestMessages.New(IppOperation.CancelMyJobs);
        request.AddGroup(IppTag.JobAttributes).AddKeyword("job-ids", "not-an-int");

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.AreNotEqual(IppJobState.Canceled, h.Store.Get(id)!.GetState().State);
    }

    // ------------------------------------------------------------------ Identify-Printer

    [TestMethod]
    public async Task IdentifyPrinter_WithUserName_ReturnsOk()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.IdentifyPrinter);
        request.Group(IppTag.OperationAttributes)!.AddName("requesting-user-name", "alice");

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task IdentifyPrinter_WithoutUserName_UsesRemoteAddress()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.IdentifyPrinter);

        var response = await SendAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    [TestMethod]
    public async Task IdentifyPrinter_WithoutUserOrRemote_UsesFallback()
    {
        using var h = new Harness();
        var request = IppTestMessages.New(IppOperation.IdentifyPrinter);

        var response = await SendNoRemoteAsync(h, request);

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
    }

    // ------------------------------------------------------------------ PrefixedReadStream

    [TestMethod]
    public async Task ReceiveDocument_PrefixedStream_ExercisesEveryMember()
    {
        var probe = new ProbeFileCacheService();
        using var h = new Harness(probe);
        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.Ok, response.Code);
        Assert.IsTrue(probe.CanRead);
        Assert.IsFalse(probe.CanSeek);
        Assert.IsFalse(probe.CanWrite);
        Assert.IsTrue(probe.SyncReadCalled);
        Assert.IsTrue(probe.ArrayReadAsyncCalled);
        Assert.IsTrue(probe.FlushCalled);
        Assert.IsTrue(probe.SyncReadResult > 0);
        Assert.IsTrue(probe.LengthThrew);
        Assert.IsTrue(probe.PositionGetThrew);
        Assert.IsTrue(probe.PositionSetThrew);
        Assert.IsTrue(probe.SeekThrew);
        Assert.IsTrue(probe.SetLengthThrew);
        Assert.IsTrue(probe.WriteThrew);
    }

    // ------------------------------------------------------------------ Helpers

    private static async Task<IppMessage> SendJobsAsync(Harness h, string which)
    {
        var request = IppTestMessages.New(IppOperation.GetJobs);
        request.AddGroup(IppTag.JobAttributes).AddKeyword("which-jobs", which);
        return await SendAsync(h, request);
    }

    private static async Task<IppMessage> SendCancelAsync(Harness h, int jobId)
    {
        var request = IppTestMessages.New(IppOperation.CancelJob);
        request.AddGroup(IppTag.JobAttributes).AddInteger("job-id", jobId);
        return await SendAsync(h, request);
    }

    private static async Task CancelAsync(Harness h, int jobId)
    {
        var response = await SendCancelAsync(h, jobId);
        Assert.AreEqual((short)IppStatus.Ok, response.Code, StatusMessage(response));
    }

    private static async Task<int> CreateJobAsync(Harness h, Action<IppAttributeGroup>? configure = null)
    {
        var request = IppTestMessages.New(IppOperation.CreateJob);
        configure?.Invoke(request.AddGroup(IppTag.JobAttributes));
        var response = await SendAsync(h, request);
        Assert.AreEqual((short)IppStatus.Ok, response.Code, StatusMessage(response));
        return JobId(response);
    }

    private static async Task<IppMessage> SendDocumentAsync(
        Harness h,
        int jobId,
        byte[]? document,
        bool? lastDocument)
    {
        var request = IppTestMessages.New(IppOperation.SendDocument);
        var job = request.AddGroup(IppTag.JobAttributes);
        job.AddInteger("job-id", jobId);
        if (lastDocument is bool value)
        {
            job.AddBoolean("last-document", value);
        }
        return await SendAsync(h, request, document);
    }

    private static async Task<IppMessage> SendAsync(
        Harness h,
        IppMessage request,
        byte[]? document = null,
        string? slug = null,
        string host = IppTestMessages.Host)
    {
        var context = IppTestMessages.Context(request, document, slug, host);
        var bytes = await h.Handler.HandleAsync(context, CancellationToken.None);
        return IppMessageReader.Parse(bytes);
    }

    private static async Task<IppMessage> SendRawAsync(Harness h, byte[] body)
    {
        var bytes = await h.Handler.HandleAsync(IppTestMessages.Context(body), CancellationToken.None);
        return IppMessageReader.Parse(bytes);
    }

    private static async Task<IppMessage> SendNoRemoteAsync(Harness h, IppMessage request)
    {
        var body = IppTestMessages.Body(request);
        var context = new IppRequestContext(
            new MemoryStream(body, writable: false),
            PrinterSlug: null,
            Host: IppTestMessages.Host,
            RemoteAddress: null);
        var bytes = await h.Handler.HandleAsync(context, CancellationToken.None);
        return IppMessageReader.Parse(bytes);
    }

    private static int JobId(IppMessage response) =>
        response.Group(IppTag.JobAttributes)!.Get("job-id")!.First!.AsInt()!.Value;

    private static IppJobOptions Options(Harness h, int jobId) => h.Store.Get(jobId)!.Options;

    private static string? StatusMessage(IppMessage message) =>
        message.Group(IppTag.OperationAttributes)?.Get("status-message")?.First?.AsString();

    // ------------------------------------------------------------------ Cache failure paths

    [TestMethod]
    public async Task PrintJob_CacheRejectsFile_ReturnsDocumentFormatNotSupported()
    {
        using var h = new Harness(cache: new ThrowingFileCacheService(new ArgumentException("Unsupported file extension.")));
        var request = IppTestMessages.New(IppOperation.PrintJob);

        var response = await SendAsync(h, request, IppTestMessages.PdfDoc());

        Assert.AreEqual((short)IppStatus.DocumentFormatNotSupported, response.Code);
    }

    /// <summary>Cache double whose writes fail, exercising the handler's defensive mapping.</summary>
    private sealed class ThrowingFileCacheService(Exception exception) : IFileCacheService
    {
        public string CacheDirectory => "throwing-cache";
        public long MaxFileSizeBytes { get; set; } = long.MaxValue;
        public long MaxCacheSizeBytes { get; set; } = long.MaxValue;

        public Task<FileCacheResult> StoreFileAsync(string originalFileName, Stream contentStream, CancellationToken ct = default) =>
            throw exception;

        public Task<FileCacheResult> StoreFileAsync(string originalFileName, Stream contentStream, long maxFileSizeBytes, CancellationToken ct = default) =>
            throw exception;

        public FileCacheResult? GetFile(string fileId) => null;

        public Task CleanupOldFilesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    // ------------------------------------------------------------------ Test fixtures

    /// <summary>Builds an <see cref="IppRequestHandler"/> with real, isolated dependencies.</summary>
    private sealed class Harness : IDisposable
    {
        private static readonly string[] DefaultRendererExtensions = [".pdf", ".jpg", ".urf", ".pwg"];
        private readonly TempDirectory _cacheDir = new();

        public Harness(IFileCacheService? cache = null, string[]? extensions = null)
        {
            Cache = cache ?? new FileCacheService(_cacheDir.Path);
            Formats = new IppDocumentFormats(
                new IDocumentRenderer[] { new FakeDocumentRenderer(extensions ?? DefaultRendererExtensions) });
            AttributeBuilder = new IppPrinterAttributeBuilder(Registry, Store, Formats, Settings);
            Handler = new IppRequestHandler(Registry, Store, AttributeBuilder, Formats, Cache, Pipeline, Events, Settings);

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
        public IppPrinterAttributeBuilder AttributeBuilder { get; }
        public IppRequestHandler Handler { get; }

        public void Dispose() => _cacheDir.Dispose();
    }

    /// <summary>Cache spy that drives the handler's private <c>PrefixedReadStream</c> through every member.</summary>
    private sealed class ProbeFileCacheService : IFileCacheService
    {
        public bool CanRead { get; private set; }
        public bool CanSeek { get; private set; }
        public bool CanWrite { get; private set; }
        public bool SyncReadCalled { get; private set; }
        public bool ArrayReadAsyncCalled { get; private set; }
        public bool FlushCalled { get; private set; }
        public int SyncReadResult { get; private set; }
        public bool LengthThrew { get; private set; }
        public bool PositionGetThrew { get; private set; }
        public bool PositionSetThrew { get; private set; }
        public bool SeekThrew { get; private set; }
        public bool SetLengthThrew { get; private set; }
        public bool WriteThrew { get; private set; }

        public string CacheDirectory => "probe-cache";
        public long MaxFileSizeBytes { get; set; } = long.MaxValue;
        public long MaxCacheSizeBytes { get; set; } = long.MaxValue;

        public Task<FileCacheResult> StoreFileAsync(string originalFileName, Stream contentStream, CancellationToken ct = default) =>
            StoreFileAsync(originalFileName, contentStream, MaxFileSizeBytes, ct);

        public Task<FileCacheResult> StoreFileAsync(
            string originalFileName,
            Stream contentStream,
            long maxFileSizeBytes,
            CancellationToken ct = default)
        {
            CanRead = contentStream.CanRead;
            CanSeek = contentStream.CanSeek;
            CanWrite = contentStream.CanWrite;

            var buffer = new byte[4];
            SyncReadResult = contentStream.Read(buffer, 0, buffer.Length);
            SyncReadCalled = true;

            ArrayReadAsyncCalled = true;
            _ = contentStream.ReadAsync(buffer, 0, buffer.Length, ct).GetAwaiter().GetResult();

            contentStream.Flush();
            FlushCalled = true;

            LengthThrew = Throws(() => _ = contentStream.Length);
            PositionGetThrew = Throws(() => _ = contentStream.Position);
            PositionSetThrew = Throws(() => contentStream.Position = 0);
            SeekThrew = Throws(() => contentStream.Seek(0, SeekOrigin.Begin));
            SetLengthThrew = Throws(() => contentStream.SetLength(0));
            WriteThrew = Throws(() => contentStream.Write(buffer, 0, 1));

            return Task.FromResult(new FileCacheResult(
                FileId: "probe",
                OriginalFileName: originalFileName,
                CachedFilePath: "probe-cache/probe.pdf",
                FileSizeBytes: SyncReadResult,
                IsDuplicate: false,
                Extension: ".pdf"));
        }

        public FileCacheResult? GetFile(string fileId) => null;

        public Task CleanupOldFilesAsync(CancellationToken ct = default) => Task.CompletedTask;

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (NotSupportedException)
            {
                return true;
            }
        }
    }
}
