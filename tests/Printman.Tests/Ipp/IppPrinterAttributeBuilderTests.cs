using Printman.Core.Models;
using Printman.Services.Ipp;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Ipp;

[TestClass]
public sealed class IppPrinterAttributeBuilderTests
{
    private const string PrinterUri = "ipp://host:631/ipp/print/fake-printer";
    private const string WebUri = "http://host:5000/";

    private static IppDocumentFormats Formats(params string[] extensions) =>
        new([new FakeDocumentRenderer(extensions)]);

    private static IppPrinterAttributeBuilder Builder(
        FakeSharedPrinterRegistry registry,
        IppDocumentFormats? formats = null,
        IppServerSettings? settings = null,
        IppJobStore? jobs = null) =>
        new(registry, jobs ?? new IppJobStore(), formats ?? Formats(".pdf"), settings ?? new IppServerSettings());

    private static SharedPrinter Printer(string windowsName = "Fake Printer") =>
        IppTestMessages.Printer("fake-printer", windowsName);

    private static IppAttributeGroup Build(
        FakeSharedPrinterRegistry registry,
        IReadOnlyCollection<string>? requested = null,
        IppDocumentFormats? formats = null,
        IppServerSettings? settings = null,
        IppJobStore? jobs = null,
        SharedPrinter? printer = null) =>
        Builder(registry, formats, settings, jobs)
            .Build(printer ?? Printer(), PrinterUri, WebUri, requested);

    private static string?[] Strings(IppAttributeGroup group, string name) =>
        group.Get(name)!.Values.Select(v => v.AsString()).ToArray();

    private static PrinterStatusInfo Status(
        bool jam = false,
        bool outOfPaper = false,
        bool door = false,
        bool paused = false,
        bool online = true,
        bool hasError = false,
        string text = "Ready") => new()
    {
        PrinterName = "Fake Printer",
        StatusText = text,
        IsOnline = online,
        HasError = hasError,
        IsPaperJam = jam,
        IsOutOfPaper = outOfPaper,
        IsDoorOpen = door,
        IsPaused = paused
    };

    [TestMethod]
    public void GetUrfSupported_NullInfo_ReturnsBaseTokensWithoutDuplex()
    {
        var tokens = IppPrinterAttributeBuilder.GetUrfSupported(null);

        Assert.AreEqual(8, tokens.Count);
        Assert.IsTrue(tokens.Contains("V1.4"));
        Assert.IsTrue(tokens.Contains("CP1"));
        Assert.IsTrue(tokens.Contains("PQ4"));
        Assert.IsTrue(tokens.Contains("RS300"));
        Assert.IsTrue(tokens.Contains("SRGB24"));
        Assert.IsTrue(tokens.Contains("W8"));
        Assert.IsTrue(tokens.Contains("IS1"));
        Assert.IsTrue(tokens.Contains("MT1"));
        Assert.IsFalse(tokens.Contains("DM1"));
    }

    [TestMethod]
    public void GetUrfSupported_DuplexPrinter_AddsDm1()
    {
        var tokens = IppPrinterAttributeBuilder.GetUrfSupported(TestData.Printer(canDuplex: true));

        Assert.AreEqual(9, tokens.Count);
        Assert.AreEqual("DM1", tokens[^1]);
    }

    [TestMethod]
    public void GetUrfSupported_SimplexPrinter_DoesNotAddDm1() =>
        Assert.IsFalse(IppPrinterAttributeBuilder
            .GetUrfSupported(TestData.Printer(canDuplex: false))
            .Contains("DM1"));

    [TestMethod]
    public void Build_IdentityAndProtocolAttributes()
    {
        var printer = Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Capabilities[printer.WindowsName] = TestData.Printer();

        var g = Build(registry, printer: printer);

        Assert.AreEqual(IppTag.PrinterAttributes, g.Tag);
        Assert.AreEqual(PrinterUri, g.Get("printer-uri-supported")!.First!.AsString());
        Assert.AreEqual("none", g.Get("uri-security-supported")!.First!.AsString());
        Assert.AreEqual("none", g.Get("uri-authentication-supported")!.First!.AsString());
        Assert.AreEqual(printer.DisplayName, g.Get("printer-name")!.First!.AsString());
        Assert.AreEqual(printer.DisplayName, g.Get("printer-dns-sd-name")!.First!.AsString());
        Assert.AreEqual(printer.DisplayName, g.Get("printer-info")!.First!.AsString());
        Assert.AreEqual(Environment.MachineName, g.Get("printer-location")!.First!.AsString());
        Assert.AreEqual($"Printman {printer.WindowsName}", g.Get("printer-make-and-model")!.First!.AsString());
        Assert.AreEqual(WebUri, g.Get("printer-more-info")!.First!.AsString());
        Assert.AreEqual($"urn:uuid:{printer.Uuid}", g.Get("printer-uuid")!.First!.AsString());
        CollectionAssert.AreEqual(new[] { "document", "photo" }, Strings(g, "printer-kind"));
        CollectionAssert.AreEqual(new[] { "1.1", "2.0" }, Strings(g, "ipp-versions-supported"));
        CollectionAssert.AreEqual(new[] { "ipp-everywhere" }, Strings(g, "ipp-features-supported"));
        CollectionAssert.AreEqual(new[] { "utf-8" }, Strings(g, "charset-configured"));
        CollectionAssert.AreEqual(new[] { "utf-8" }, Strings(g, "charset-supported"));
        CollectionAssert.AreEqual(new[] { "en" }, Strings(g, "natural-language-configured"));
        CollectionAssert.AreEqual(new[] { "en" }, Strings(g, "generated-natural-language-supported"));
        Assert.AreEqual("none", g.Get("compression-supported")!.First!.AsString());
        Assert.AreEqual(false, g.Get("multiple-document-jobs-supported")!.First!.AsBool());
        Assert.AreEqual("display", g.Get("identify-actions-default")!.First!.AsString());
    }

    [TestMethod]
    public void Build_OperationsSupported_ContainsEveryAdvertisedOperation()
    {
        var g = Build(new FakeSharedPrinterRegistry());

        var operations = g.Get("operations-supported")!.Values.Select(v => v.AsInt()!.Value).ToArray();

        CollectionAssert.AreEqual(
            new int[]
            {
                IppOperation.PrintJob,
                IppOperation.ValidateJob,
                IppOperation.CreateJob,
                IppOperation.SendDocument,
                IppOperation.CancelJob,
                IppOperation.GetJobAttributes,
                IppOperation.GetJobs,
                IppOperation.GetPrinterAttributes,
                IppOperation.CancelMyJobs,
                IppOperation.CloseJob,
                IppOperation.IdentifyPrinter
            },
            operations);
    }

    [TestMethod]
    public void Build_UrfSupported_AddsAirprintFeatureAndUrfTokens()
    {
        var printer = Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Capabilities[printer.WindowsName] = TestData.Printer(canDuplex: true);

        var g = Build(registry, formats: Formats(".pdf", ".urf"), printer: printer);

        CollectionAssert.AreEqual(new[] { "ipp-everywhere", "airprint-1.4" }, Strings(g, "ipp-features-supported"));
        CollectionAssert.AreEqual(
            IppPrinterAttributeBuilder.GetUrfSupported(TestData.Printer(canDuplex: true)).ToArray(),
            Strings(g, "urf-supported"));
    }

    [TestMethod]
    public void Build_UrfNotSupported_OmitsUrfTokensAndAirprintFeature()
    {
        var g = Build(new FakeSharedPrinterRegistry(), formats: Formats(".pdf"));

        Assert.IsFalse(g.Attributes.Any(a => a.Name == "urf-supported"));
        CollectionAssert.AreEqual(new[] { "ipp-everywhere" }, Strings(g, "ipp-features-supported"));
    }

    [TestMethod]
    public void Build_PwgRasterSupported_AddsRasterAttributes()
    {
        var g = Build(new FakeSharedPrinterRegistry(), formats: Formats(".pwg"));

        var resolution = g.Get("pwg-raster-document-resolution-supported");
        Assert.IsNotNull(resolution);
        var value = (IppResolution)resolution.First!.Value!;
        Assert.AreEqual(IppPrinterAttributeBuilder.RasterDpi, value.CrossFeed);
        Assert.AreEqual(IppPrinterAttributeBuilder.RasterDpi, value.Feed);
        CollectionAssert.AreEqual(new[] { "sgray_8", "srgb_8" }, Strings(g, "pwg-raster-document-type-supported"));
        CollectionAssert.AreEqual(new[] { "normal" }, Strings(g, "pwg-raster-document-sheet-back"));
    }

    [TestMethod]
    public void Build_PwgRasterNotSupported_OmitsRasterAttributes() =>
        Assert.IsNull(Build(new FakeSharedPrinterRegistry(), formats: Formats(".pdf"))
            .Get("pwg-raster-document-resolution-supported"));

    [TestMethod]
    public void Build_DocumentFormats_AppendOctetStream()
    {
        var g = Build(
            new FakeSharedPrinterRegistry(),
            formats: Formats(".pdf", ".urf", ".pwg", ".jpg", ".png"));

        CollectionAssert.AreEqual(
            new[]
            {
                IppDocumentFormats.Pdf,
                IppDocumentFormats.Urf,
                IppDocumentFormats.PwgRaster,
                IppDocumentFormats.Jpeg,
                IppDocumentFormats.Png,
                IppDocumentFormats.OctetStream
            },
            Strings(g, "document-format-supported"));
        Assert.AreEqual(IppDocumentFormats.OctetStream, g.Get("document-format-default")!.First!.AsString());
        CollectionAssert.AreEqual(
            new[] { "adobe-1.3", "adobe-1.4", "adobe-1.5", "adobe-1.6", "adobe-1.7", "iso-32000-1_2008" },
            Strings(g, "pdf-versions-supported"));
    }

    [TestMethod]
    [DataRow(true, true, true, "auto", 3)]
    [DataRow(false, false, false, "monochrome", 1)]
    [DataRow(true, false, true, "auto", 1)]
    [DataRow(false, true, false, "monochrome", 3)]
    public void Build_ColorAndDuplex_ReflectCapabilities(
        bool color,
        bool duplex,
        bool expectedColor,
        string defaultMode,
        int sidesCount)
    {
        var printer = Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Capabilities[printer.WindowsName] = TestData.Printer(supportsColor: color, canDuplex: duplex);

        var g = Build(registry, printer: printer);

        Assert.AreEqual(expectedColor, g.Get("color-supported")!.First!.AsBool());
        Assert.AreEqual(defaultMode, g.Get("print-color-mode-default")!.First!.AsString());
        Assert.AreEqual(sidesCount, g.Get("sides-supported")!.Values.Count);
        Assert.AreEqual("one-sided", g.Get("sides-default")!.First!.AsString());
        var modes = Strings(g, "print-color-mode-supported");
        Assert.AreEqual("auto", modes[0]);
        Assert.AreEqual("monochrome", modes[1]);
        Assert.AreEqual(color ? 3 : 2, modes.Length);
    }

    [TestMethod]
    public void Build_DeviceId_ReflectsFormatsAndSanitizesModel()
    {
        var printer = Printer("Acme;Model: X");

        var g = Build(
            new FakeSharedPrinterRegistry(),
            formats: Formats(".pdf", ".pwg", ".urf", ".jpg"),
            printer: printer);

        Assert.AreEqual(
            "MFG:Printman;MDL:Acme Model  X;CMD:PDF,PWGRaster,URF,JPEG;",
            g.Get("printer-device-id")!.First!.AsString());
    }

    [TestMethod]
    public void Build_DeviceId_NoSupportedFormats_HasEmptyCommandList()
    {
        var g = Build(new FakeSharedPrinterRegistry(), formats: Formats());

        Assert.AreEqual("MFG:Printman;MDL:Fake Printer;CMD:;", g.Get("printer-device-id")!.First!.AsString());
    }

    [TestMethod]
    public void Build_State_NoStatusAndNoJobs_IsIdleAndReady()
    {
        var g = Build(new FakeSharedPrinterRegistry());

        Assert.AreEqual(IppPrinterState.Idle, g.Get("printer-state")!.First!.AsInt());
        CollectionAssert.AreEqual(new[] { "none" }, Strings(g, "printer-state-reasons"));
        Assert.AreEqual("Ready", g.Get("printer-state-message")!.First!.AsString());
        Assert.AreEqual(0, g.Get("queued-job-count")!.First!.AsInt());
        Assert.AreEqual(true, g.Get("printer-is-accepting-jobs")!.First!.AsBool());
        Assert.IsTrue(g.Get("printer-up-time")!.First!.AsInt() >= 1);
        Assert.AreEqual(1, g.Get("printer-state-change-time")!.First!.AsInt());
        Assert.AreEqual(1, g.Get("printer-config-change-time")!.First!.AsInt());
        Assert.AreEqual(11, ((byte[])g.Get("printer-current-time")!.First!.Value!).Length);
    }

    [TestMethod]
    public void Build_State_WithActiveJob_IsProcessing()
    {
        var printer = Printer();
        var jobs = new IppJobStore();
        jobs.Create(printer.Slug, "doc", "user", new IppJobOptions());

        var g = Build(new FakeSharedPrinterRegistry(), jobs: jobs, printer: printer);

        Assert.AreEqual(IppPrinterState.Processing, g.Get("printer-state")!.First!.AsInt());
        Assert.AreEqual(1, g.Get("queued-job-count")!.First!.AsInt());
        CollectionAssert.AreEqual(new[] { "none" }, Strings(g, "printer-state-reasons"));
        Assert.AreEqual(true, g.Get("printer-is-accepting-jobs")!.First!.AsBool());
    }

    [TestMethod]
    public void Build_State_MaxPendingJobsReached_StopsAcceptingJobs()
    {
        var printer = Printer();
        var jobs = new IppJobStore();
        jobs.Create(printer.Slug, "doc", "user", new IppJobOptions());

        var g = Build(
            new FakeSharedPrinterRegistry(),
            settings: new IppServerSettings { MaxPendingJobs = 1 },
            jobs: jobs,
            printer: printer);

        Assert.AreEqual(false, g.Get("printer-is-accepting-jobs")!.First!.AsBool());
    }

    [TestMethod]
    [DataRow(true, false, false, false, true, false, "media-jam-error", IppPrinterState.Stopped)]
    [DataRow(false, true, false, false, true, false, "media-empty-error", IppPrinterState.Stopped)]
    [DataRow(false, false, true, false, true, false, "door-open-error", IppPrinterState.Stopped)]
    [DataRow(false, false, false, true, true, false, "paused", IppPrinterState.Stopped)]
    [DataRow(false, false, false, false, true, true, "other-error", IppPrinterState.Stopped)]
    [DataRow(false, false, false, false, false, false, "offline-report", IppPrinterState.Idle)]
    [DataRow(false, false, false, false, false, true, "offline-report", IppPrinterState.Idle)]
    public void Build_State_StatusFlags(
        bool jam,
        bool outOfPaper,
        bool door,
        bool paused,
        bool online,
        bool hasError,
        string expectedReason,
        int expectedState)
    {
        var printer = Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Statuses[printer.WindowsName] = Status(jam, outOfPaper, door, paused, online, hasError);

        var g = Build(registry, printer: printer);

        Assert.AreEqual(expectedState, g.Get("printer-state")!.First!.AsInt());
        CollectionAssert.Contains(Strings(g, "printer-state-reasons").ToList(), expectedReason);
    }

    [TestMethod]
    public void Build_State_MultipleErrorReasons_AreAllReported()
    {
        var printer = Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Statuses[printer.WindowsName] = Status(jam: true, online: false);

        var g = Build(registry, printer: printer);

        CollectionAssert.AreEqual(
            new[] { "media-jam-error", "offline-report" },
            Strings(g, "printer-state-reasons"));
    }

    [TestMethod]
    public void Build_State_ErrorWithExistingReason_DoesNotAddOtherError()
    {
        var printer = Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Statuses[printer.WindowsName] = Status(jam: true, hasError: true);

        var g = Build(registry, printer: printer);

        CollectionAssert.AreEqual(new[] { "media-jam-error" }, Strings(g, "printer-state-reasons"));
        Assert.AreEqual(IppPrinterState.Stopped, g.Get("printer-state")!.First!.AsInt());
    }

    [TestMethod]
    public void Build_StateMessage_UsesStatusTextWhenPresent()
    {
        var printer = Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Statuses[printer.WindowsName] = Status(text: "Warming up");

        var g = Build(registry, printer: printer);

        Assert.AreEqual("Warming up", g.Get("printer-state-message")!.First!.AsString());
    }

    [TestMethod]
    public void Build_Media_UsesMappedPwgNamesAndCollections()
    {
        var printer = Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Capabilities[printer.WindowsName] =
            TestData.Printer(paperSizes: [TestData.A4, TestData.Letter]);

        var g = Build(registry, printer: printer);

        CollectionAssert.AreEqual(new[] { "iso_a4_210x297mm", "na_letter_8.5x11in" }, Strings(g, "media-supported"));
        Assert.AreEqual(2, g.Get("media-size-supported")!.Values.Count);
        var first = g.Get("media-size-supported")!.Values[0].AsCollection()!;
        Assert.AreEqual(21000, first.Get("x-dimension")!.First!.AsInt());
        Assert.AreEqual(29700, first.Get("y-dimension")!.First!.AsInt());

        var defaultMedia = g.Get("media-default")!.First!.AsString();
        Assert.IsTrue(Strings(g, "media-supported").Contains(defaultMedia));
        CollectionAssert.AreEqual(new[] { defaultMedia }, Strings(g, "media-ready"));

        var mediaCol = g.Get("media-col-default")!.First!.AsCollection()!;
        Assert.AreEqual(defaultMedia, mediaCol.Get("media-size-name")!.First!.AsString());
        Assert.AreEqual(423, mediaCol.Get("media-top-margin")!.First!.AsInt());
        Assert.AreEqual(423, mediaCol.Get("media-bottom-margin")!.First!.AsInt());
        Assert.AreEqual(423, mediaCol.Get("media-left-margin")!.First!.AsInt());
        Assert.AreEqual(423, mediaCol.Get("media-right-margin")!.First!.AsInt());
        Assert.AreEqual("auto", mediaCol.Get("media-source")!.First!.AsString());
        Assert.AreEqual("stationery", mediaCol.Get("media-type")!.First!.AsString());

        // media-col-default follows the region default (A4 metric / Letter else), so derive the
        // expected dimensions from the selected PWG media instead of hardcoding A4.
        var defaultPwg = PwgMediaMapper.FindByName(defaultMedia)!;
        var size = mediaCol.Get("media-size")!.First!.AsCollection()!;
        Assert.AreEqual(defaultPwg.WidthHmm, size.Get("x-dimension")!.First!.AsInt());
        Assert.AreEqual(defaultPwg.HeightHmm, size.Get("y-dimension")!.First!.AsInt());

        var ready = g.Get("media-col-ready")!.First!.AsCollection()!;
        Assert.AreEqual(defaultMedia, ready.Get("media-size-name")!.First!.AsString());
        CollectionAssert.AreEqual(
            new[]
            {
                "media-size", "media-size-name", "media-top-margin", "media-bottom-margin",
                "media-left-margin", "media-right-margin", "media-source", "media-type"
            },
            Strings(g, "media-col-supported"));
        CollectionAssert.AreEqual(new[] { "auto" }, Strings(g, "media-source-supported"));
        CollectionAssert.AreEqual(new[] { "stationery" }, Strings(g, "media-type-supported"));
        Assert.AreEqual(423, g.Get("media-top-margin-supported")!.First!.AsInt());
        Assert.AreEqual(423, g.Get("media-bottom-margin-supported")!.First!.AsInt());
        Assert.AreEqual(423, g.Get("media-left-margin-supported")!.First!.AsInt());
        Assert.AreEqual(423, g.Get("media-right-margin-supported")!.First!.AsInt());
    }

    [TestMethod]
    public void Build_Media_WithoutCapabilities_FallsBackToA4AndLetter()
    {
        var g = Build(new FakeSharedPrinterRegistry());

        CollectionAssert.AreEqual(
            new[] { "iso_a4_210x297mm", "na_letter_8.5x11in" },
            Strings(g, "media-supported"));
    }

    [TestMethod]
    public void Build_MediaColDatabase_IncludedOnlyWhenRequested()
    {
        var notRequested = Build(new FakeSharedPrinterRegistry());
        Assert.IsNull(notRequested.Get("media-col-database"));

        var requested = Build(new FakeSharedPrinterRegistry(), requested: ["media-col-database"]);
        Assert.IsNotNull(requested.Get("media-col-database"));
        Assert.AreEqual(2, requested.Get("media-col-database")!.Values.Count);
    }

    [TestMethod]
    public void Build_JobTemplateAttributes_ArePresent()
    {
        var g = Build(new FakeSharedPrinterRegistry());

        Assert.AreEqual(1, g.Get("copies-default")!.First!.AsInt());
        Assert.AreEqual(new IppRange(1, 99), g.Get("copies-supported")!.First!.AsRange()!.Value);
        Assert.AreEqual(1, g.Get("number-up-default")!.First!.AsInt());
        Assert.AreEqual(3, g.Get("finishings-default")!.First!.AsInt());
        Assert.AreEqual("auto", g.Get("print-scaling-default")!.First!.AsString());
        Assert.AreEqual("face-down", g.Get("output-bin-default")!.First!.AsString());
        Assert.IsTrue(g.Get("page-ranges-supported")!.First!.AsBool());
        Assert.AreEqual(4, g.Get("print-quality-default")!.First!.AsInt());
        CollectionAssert.AreEqual(
            new[] { "auto", "auto-fit", "fit", "fill", "none" },
            Strings(g, "print-scaling-supported"));
        Assert.AreEqual(
            IppPrinterAttributeBuilder.RasterDpi,
            ((IppResolution)g.Get("printer-resolution-default")!.First!.Value!).CrossFeed);
        Assert.AreEqual(
            IppPrinterAttributeBuilder.RasterDpi,
            ((IppResolution)g.Get("printer-resolution-supported")!.First!.Value!).Feed);
    }

    [TestMethod]
    public void Build_RequestedNullAndEverythingKeywords_ReturnEverythingExceptMediaColDatabase()
    {
        var requests = new[]
        {
            (IReadOnlyCollection<string>?)null,
            Array.Empty<string>(),
            new[] { "all" },
            new[] { "printer-description" },
            new[] { "job-template" }
        };

        foreach (var requested in requests)
        {
            var g = Build(new FakeSharedPrinterRegistry(), requested: requested);

            Assert.IsNotNull(g.Get("printer-name"), $"requested={requested}");
            Assert.IsNotNull(g.Get("copies-default"), $"requested={requested}");
            Assert.IsNotNull(g.Get("media-default"), $"requested={requested}");
            Assert.IsNull(g.Get("media-col-database"), $"requested={requested}");
        }
    }

    [TestMethod]
    public void Build_RequestedSpecificSet_ReturnsOnlyThoseAttributes()
    {
        var g = Build(new FakeSharedPrinterRegistry(), requested: ["printer-name", "printer-state"]);

        Assert.AreEqual(2, g.Attributes.Count);
        Assert.IsNotNull(g.Get("printer-name"));
        Assert.IsNotNull(g.Get("printer-state"));
        Assert.IsNull(g.Get("copies-default"));
    }

    [TestMethod]
    public void Build_RequestedMediaColDatabasePlusName_ReturnsBoth()
    {
        var g = Build(new FakeSharedPrinterRegistry(), requested: ["printer-name", "media-col-database"]);

        Assert.AreEqual(2, g.Attributes.Count);
        Assert.IsNotNull(g.Get("printer-name"));
        Assert.IsNotNull(g.Get("media-col-database"));
    }
}
