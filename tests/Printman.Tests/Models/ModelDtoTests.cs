using System.Drawing.Printing;
using Printman.Core.Models;

namespace Printman.Tests.Models;

[TestClass]
public sealed class ModelDtoTests
{
    // ---------------------------------------------------------------------
    // PrintJobResult
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PrintJobResult_Failed_SetsFailureFields()
    {
        var result = PrintJobResult.Failed("HP", "jam");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("HP", result.PrinterUsed);
        Assert.AreEqual("jam", result.ErrorMessage);
        Assert.AreEqual(0, result.PagesPrinted);
        Assert.AreEqual(0, result.CopiesPrinted);
        Assert.IsNull(result.PaperSizeUsed);
    }

    [TestMethod]
    public void PrintJobResult_Succeeded_SetsSuccessFields()
    {
        var result = PrintJobResult.Succeeded("HP", 3, 2, "A4");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("HP", result.PrinterUsed);
        Assert.AreEqual(3, result.PagesPrinted);
        Assert.AreEqual(2, result.CopiesPrinted);
        Assert.AreEqual("A4", result.PaperSizeUsed);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void PrintJobResult_Succeeded_WithoutPaperSize_LeavesItNull()
    {
        var result = PrintJobResult.Succeeded("HP", 1, 1);

        Assert.IsNull(result.PaperSizeUsed);
    }

    // ---------------------------------------------------------------------
    // PrintJobInfo
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PrintJobInfo_Defaults()
    {
        var info = new PrintJobInfo
        {
            JobId = 1,
            PrinterName = "HP",
            DocumentName = "doc"
        };

        Assert.IsNull(info.PipelineJobId);
        Assert.IsNull(info.UserName);
        Assert.AreEqual(0, info.TotalPages);
        Assert.AreEqual(0, info.PagesPrinted);
        Assert.AreEqual(0, info.SizeBytes);
        Assert.AreEqual(PrintJobStatusCode.Queued, info.StatusCode);
        Assert.AreEqual("Queued", info.StatusDescription);
        Assert.IsFalse(info.IsPrintmanPipelineJob);
        Assert.IsTrue(info.CanCancel);
        Assert.IsTrue(info.SubmittedAt <= DateTime.UtcNow);
    }

    [TestMethod]
    public void PrintJobInfo_InitProperties_AreRetained()
    {
        var submitted = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var info = new PrintJobInfo
        {
            JobId = 7,
            PipelineJobId = "pipe",
            PrinterName = "HP",
            DocumentName = "doc",
            UserName = "alice",
            TotalPages = 4,
            PagesPrinted = 2,
            SizeBytes = 1234,
            SubmittedAt = submitted,
            StatusCode = PrintJobStatusCode.Printing,
            StatusDescription = "Printing",
            IsPrintmanPipelineJob = true,
            CanCancel = false
        };

        Assert.AreEqual("pipe", info.PipelineJobId);
        Assert.AreEqual("alice", info.UserName);
        Assert.AreEqual(4, info.TotalPages);
        Assert.AreEqual(2, info.PagesPrinted);
        Assert.AreEqual(1234, info.SizeBytes);
        Assert.AreEqual(submitted, info.SubmittedAt);
        Assert.AreEqual(PrintJobStatusCode.Printing, info.StatusCode);
        Assert.AreEqual("Printing", info.StatusDescription);
        Assert.IsTrue(info.IsPrintmanPipelineJob);
        Assert.IsFalse(info.CanCancel);
    }

    // ---------------------------------------------------------------------
    // PrintJobRequest
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PrintJobRequest_Defaults()
    {
        var request = new PrintJobRequest { FilePath = "doc.pdf" };

        Assert.AreEqual("doc.pdf", request.FilePath);
        Assert.IsNull(request.TargetPrinterName);
        Assert.IsTrue(request.PageRange.IsAllPages);
        Assert.IsNull(request.PaperSizeName);
        Assert.AreEqual(1, request.Copies);
        Assert.AreEqual(PrintOrientation.Auto, request.Orientation);
        Assert.AreEqual(PrintDuplex.Default, request.Duplex);
        Assert.AreEqual(PrintColorMode.Default, request.ColorMode);
        Assert.AreEqual(300, request.Dpi);
        Assert.IsTrue(request.FitToPage);
        Assert.IsFalse(request.FullPage);
        Assert.IsNull(request.JobTitle);
        Assert.IsNull(request.OutputFilePath);
    }

    // ---------------------------------------------------------------------
    // PrinterInfo / PrinterStatusInfo
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PrinterInfo_DefaultsAndToString()
    {
        var info = new PrinterInfo { Name = "HP" };

        Assert.AreEqual("HP", info.Name);
        Assert.IsFalse(info.IsDefault);
        Assert.AreEqual("Ready", info.Status);
        Assert.IsNull(info.PortName);
        Assert.IsNull(info.DriverName);
        Assert.IsFalse(info.SupportsColor);
        Assert.IsFalse(info.CanDuplex);
        Assert.AreEqual(0, info.SupportedPaperSizes.Count);
        Assert.AreEqual(0, info.SupportedResolutions.Count);
        Assert.AreEqual("HP (Port: N/A)", info.ToString());
    }

    [TestMethod]
    public void PrinterInfo_ToString_MarksDefaultAndPort()
    {
        var info = new PrinterInfo { Name = "HP", IsDefault = true, PortName = "USB001" };

        Assert.AreEqual("HP [DEFAULT] (Port: USB001)", info.ToString());
    }

    [TestMethod]
    public void PrinterStatusInfo_DefaultsAndMutation()
    {
        var status = new PrinterStatusInfo { PrinterName = "HP" };

        Assert.AreEqual("Ready", status.StatusText);
        Assert.IsTrue(status.IsOnline);
        Assert.IsFalse(status.HasError);
        Assert.IsFalse(status.IsPaperJam);
        Assert.IsFalse(status.IsOutOfPaper);
        Assert.IsFalse(status.IsDoorOpen);
        Assert.IsFalse(status.IsBusy);
        Assert.IsFalse(status.IsPaused);
        Assert.AreEqual(0, status.QueuedJobCount);

        status.StatusText = "Busy";
        status.IsOnline = false;
        status.HasError = true;
        status.IsPaperJam = true;
        status.IsOutOfPaper = true;
        status.IsDoorOpen = true;
        status.IsBusy = true;
        status.IsPaused = true;
        status.QueuedJobCount = 2;

        Assert.AreEqual("Busy", status.StatusText);
        Assert.IsFalse(status.IsOnline);
        Assert.IsTrue(status.HasError);
        Assert.IsTrue(status.IsPaperJam);
        Assert.IsTrue(status.IsOutOfPaper);
        Assert.IsTrue(status.IsDoorOpen);
        Assert.IsTrue(status.IsBusy);
        Assert.IsTrue(status.IsPaused);
        Assert.AreEqual(2, status.QueuedJobCount);
    }

    // ---------------------------------------------------------------------
    // Server models
    // ---------------------------------------------------------------------

    [TestMethod]
    public void FileCacheResult_RetainsValues()
    {
        var result = new FileCacheResult("id", "doc.pdf", "C:\\cache\\id.pdf", 42, true, ".pdf");

        Assert.AreEqual("id", result.FileId);
        Assert.AreEqual("doc.pdf", result.OriginalFileName);
        Assert.AreEqual("C:\\cache\\id.pdf", result.CachedFilePath);
        Assert.AreEqual(42, result.FileSizeBytes);
        Assert.IsTrue(result.IsDuplicate);
        Assert.AreEqual(".pdf", result.Extension);
    }

    [TestMethod]
    public void WebUploadModels_DefaultsAndValues()
    {
        var response = new UploadResponse
        {
            FileId = "id",
            FileName = "doc.pdf",
            FileSize = 10,
            PageCount = 2,
            IsDuplicate = true,
            Extension = ".pdf"
        };

        Assert.AreEqual("id", response.FileId);
        Assert.AreEqual("doc.pdf", response.FileName);
        Assert.AreEqual(10, response.FileSize);
        Assert.AreEqual(2, response.PageCount);
        Assert.IsTrue(response.IsDuplicate);
        Assert.AreEqual(".pdf", response.Extension);

        var item = new WebPrintItem { FileId = "id" };
        Assert.IsNull(item.Pages);
        Assert.AreEqual(1, item.Copies);
        Assert.IsNull(item.PaperSize);
        Assert.IsNull(item.Orientation);
        Assert.IsNull(item.Duplex);
        Assert.IsNull(item.Color);

        var batch = new WebBatchPrintRequest();
        Assert.IsNull(batch.Printer);
        Assert.AreEqual(0, batch.Items.Count);
    }

    [TestMethod]
    public void PrintEvent_DefaultsAndValues()
    {
        var printEvent = new PrintEvent { Type = "queued", Message = "Queued" };

        Assert.AreEqual(8, printEvent.Id.Length);
        Assert.AreEqual("queued", printEvent.Type);
        Assert.IsNull(printEvent.JobId);
        Assert.IsNull(printEvent.FileName);
        Assert.IsNull(printEvent.Printer);
        Assert.AreEqual("Queued", printEvent.Message);
        Assert.IsNull(printEvent.PagesPrinted);
        Assert.IsNull(printEvent.TotalPages);
        Assert.IsTrue(printEvent.Timestamp <= DateTime.UtcNow);
    }

    [TestMethod]
    public void RequestDtos_RetainValues()
    {
        var pinRequest = new PinVerifyRequest { Pin = "1234" };
        Assert.AreEqual("1234", pinRequest.Pin);

        var cancel = new CancelJobRequest { Printer = "HP", JobId = "7" };
        Assert.AreEqual("HP", cancel.Printer);
        Assert.AreEqual("7", cancel.JobId);

        var purge = new PurgeQueueRequest { Printer = "HP" };
        Assert.AreEqual("HP", purge.Printer);

        var queue = new QueueResponse();
        Assert.IsNull(queue.Printer);
        Assert.IsNull(queue.Status);
        Assert.AreEqual(0, queue.Jobs.Count);
    }

    [TestMethod]
    public void DnsSdService_DefaultsAndValues()
    {
        var service = new DnsSdService
        {
            InstanceName = "Printman - HP",
            ServiceType = "_ipp._tcp",
            Subtypes = ["_universal", "_print"],
            Port = 631,
            Txt = [new KeyValuePair<string, string>("ty", "Printman")]
        };

        Assert.AreEqual("Printman - HP", service.InstanceName);
        Assert.AreEqual("_ipp._tcp", service.ServiceType);
        CollectionAssert.AreEqual(new[] { "_universal", "_print" }, service.Subtypes.ToArray());
        Assert.AreEqual(631, service.Port);
        Assert.AreEqual("ty", service.Txt[0].Key);
        Assert.AreEqual("Printman", service.Txt[0].Value);

        var minimal = new DnsSdService { InstanceName = "n", ServiceType = "_print._tcp", Port = 1 };
        Assert.AreEqual(0, minimal.Subtypes.Count);
        Assert.AreEqual(0, minimal.Txt.Count);
    }

    // ---------------------------------------------------------------------
    // ServerOptions / ShareOptions
    // ---------------------------------------------------------------------

    [TestMethod]
    public void ServerOptions_Defaults()
    {
        var options = new ServerOptions();

        Assert.AreEqual(5000, options.Port);
        Assert.AreEqual("0.0.0.0", options.BindAddress);
        Assert.IsNull(options.Pin);
        Assert.IsTrue(options.RequireAuth);
        Assert.AreEqual(50, options.MaxUploadMb);
        Assert.AreEqual(500, options.CacheLimitMb);
        Assert.IsNull(options.OutputDirectory);
        Assert.IsTrue(options.EnableWebUi);
        Assert.IsFalse(options.Share.Enabled);
        Assert.AreEqual(0, options.Share.Printers.Count);
        Assert.AreEqual(631, options.Share.IppPort);
        Assert.IsTrue(options.Share.EnableMdns);
        Assert.IsFalse(options.Share.AllowAnySource);
        Assert.AreEqual(256, options.Share.MaxJobMb);
    }

    [TestMethod]
    public void ShareOptions_InitProperties_AreRetained()
    {
        var options = new ShareOptions
        {
            Enabled = true,
            Printers = ["HP"],
            IppPort = 8631,
            EnableMdns = false,
            AllowAnySource = true,
            MaxJobMb = 10
        };

        Assert.IsTrue(options.Enabled);
        CollectionAssert.AreEqual(new[] { "HP" }, options.Printers.ToArray());
        Assert.AreEqual(8631, options.IppPort);
        Assert.IsFalse(options.EnableMdns);
        Assert.IsTrue(options.AllowAnySource);
        Assert.AreEqual(10, options.MaxJobMb);
    }

    // ---------------------------------------------------------------------
    // PageRange gaps not exercised by PageRangeTests
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PageRange_ToStringAndRawExpression()
    {
        Assert.AreEqual("all", PageRange.All.RawExpression);
        Assert.AreEqual("All Pages", PageRange.All.ToString());
        Assert.AreEqual("1:3", PageRange.Parse("1:3").ToString());
        Assert.AreEqual("1:3", PageRange.Parse("  1:3  ").RawExpression);
    }

    [TestMethod]
    public void PageRange_OnlySeparators_IsNotAllButResolvesEmpty()
    {
        var range = PageRange.Parse(",");

        Assert.IsFalse(range.IsAllPages);
        Assert.AreEqual(0, range.ResolvePages(5).Count);
        Assert.AreEqual(",", range.ToString());
    }

    [TestMethod]
    public void PageRange_SelectorsFullyOutsideDocument_ResolveEmpty()
    {
        Assert.AreEqual(0, PageRange.Parse("5-6").ResolvePages(3).Count);
        Assert.AreEqual(0, PageRange.Parse("5-").ResolvePages(3).Count);
        Assert.AreEqual(0, PageRange.Parse("5").ResolvePages(3).Count);
    }

    [TestMethod]
    public void PageRange_OpenEndedBoundsBeyondLimit_Throw()
    {
        Assert.ThrowsExactly<FormatException>(() => PageRange.Parse("60000-"));
        Assert.ThrowsExactly<FormatException>(() => PageRange.Parse("-60000"));
    }

    // ---------------------------------------------------------------------
    // PaperSizeOption
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PaperSizeOption_MillimeterConversion()
    {
        var option = new PaperSizeOption("A4", 9, 827, 1169);

        Assert.AreEqual(210.1, option.WidthMm);
        Assert.AreEqual(296.9, option.HeightMm);
    }

    [TestMethod]
    public void PaperSizeOption_ToString_FormatsNameAndMillimeters()
    {
        var option = new PaperSizeOption("Test", 0, 0, 0);

        Assert.AreEqual("Test (0 x 0 mm)", option.ToString());
    }

    [TestMethod]
    public void PaperSizeOption_Keyword_DefaultsToNullAndCanBeSet()
    {
        var windows = new PaperSizeOption("A4", 9, 827, 1169);
        var cups = windows with { Keyword = "iso_a4_210x297mm" };

        Assert.IsNull(windows.Keyword);
        Assert.AreEqual("iso_a4_210x297mm", cups.Keyword);
        Assert.AreEqual(9, cups.RawKind);
        Assert.AreEqual(827, cups.WidthHundredthsInch);
        Assert.AreEqual(1169, cups.HeightHundredthsInch);
    }

    // ---------------------------------------------------------------------
    // Enums
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PrintJobStatusCode_HasExpectedMembers()
    {
        Assert.AreEqual(0, (int)PrintJobStatusCode.Queued);
        Assert.AreEqual(10, (int)PrintJobStatusCode.Unknown);
    }
}
