using Printman.Core.Models;
using Printman.Services.Cups;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Cups;

/// <summary>cupsd answers with attributes of unexpected types: every lookup degrades to a default instead of throwing.</summary>
[TestClass]
public sealed class CupsAttributeTypeTests
{
    [TestMethod]
    public void Discovery_WronglyTypedAttributes_FallBackToDefaults()
    {
        var server = new FakeCupsServer
        {
            Respond = r => r.Message.Code == CupsClient.CupsGetPrinters
                ? FakeCupsServer.Ok(m =>
                {
                    var g = m.AddGroup(IppTag.PrinterAttributes);
                    g.AddName("printer-name", "Odd");
                    g.AddText("printer-state", "idle");            // should be an enum
                    g.AddInteger("printer-is-accepting-jobs", 1);  // should be a boolean
                    g.AddInteger("color-supported", 1);
                    g.AddInteger("sides-supported", 2);
                    g.AddInteger("media-supported", 4);
                    g.AddText("printer-resolution-supported", "300dpi");
                    g.AddInteger("device-uri", 5);
                    g.AddInteger("printer-make-and-model", 6);
                    m.AddGroup(IppTag.PrinterAttributes).AddInteger("printer-name", 7); // name is not a string: skipped
                })
                : FakeCupsServer.Ok(m => m.AddGroup(IppTag.PrinterAttributes).AddInteger("printer-name", 7))
        };
        var service = new CupsPrinterDiscoveryService(server.CreateClient(), _ => null, null, TextWriter.Null);

        var printer = service.GetPrinters().Single();

        Assert.AreEqual("Odd", printer.Name);
        Assert.AreEqual("Ready", printer.Status);
        Assert.IsFalse(printer.SupportsColor);
        Assert.IsFalse(printer.CanDuplex);
        Assert.IsNull(printer.PortName);
        Assert.IsNull(printer.DriverName);
        Assert.AreEqual(0, printer.SupportedPaperSizes.Count);
        Assert.AreEqual(0, printer.SupportedResolutions.Count);
        Assert.IsFalse(printer.IsDefault, "server default without a string name means no default");
    }

    [TestMethod]
    public void Queue_WronglyTypedJobAttributes_FallBackToDefaults()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("PDF"));
        var server = new FakeCupsServer
        {
            Respond = r => r.Message.Code switch
            {
                IppOperation.GetJobs => FakeCupsServer.Ok(m =>
                {
                    var g = m.AddGroup(IppTag.JobAttributes);
                    g.AddInteger("job-id", 4);
                    g.AddInteger("job-name", 1);
                    g.AddInteger("job-originating-user-name", 2);
                    g.AddInteger("job-printer-uri", 3);
                    g.AddText("job-state", "processing");
                    g.AddInteger("job-state-reasons", 5);
                    g.AddInteger("job-printer-state-message", 6);
                    g.AddText("job-k-octets", "big");
                    g.AddText("time-at-creation", "now");
                    g.AddText("job-impressions", "many");
                    g.AddText("job-impressions-completed", "some");
                    m.AddGroup(IppTag.JobAttributes).AddText("job-id", "four"); // id not an int: skipped
                }),
                IppOperation.GetPrinterAttributes => FakeCupsServer.Ok(m =>
                {
                    var g = m.AddGroup(IppTag.PrinterAttributes);
                    g.AddText("printer-state", "idle");
                    g.AddText("queued-job-count", "two");
                    g.AddInteger("printer-state-reasons", 9);
                    g.AddText("printer-is-accepting-jobs", "yes");
                }),
                _ => FakeCupsServer.Error(IppStatus.OperationNotSupported)
            }
        };
        var service = new CupsPrintQueueService(discovery, server.CreateClient());

        var job = service.GetJobs("PDF").Single();
        Assert.AreEqual(4, job.JobId);
        Assert.AreEqual("Document", job.DocumentName);
        Assert.IsNull(job.UserName);
        Assert.AreEqual("PDF", job.PrinterName);
        Assert.AreEqual(PrintJobStatusCode.Queued, job.StatusCode);
        Assert.AreEqual(0, job.SizeBytes);
        Assert.AreEqual(0, job.TotalPages);
        Assert.AreEqual(0, job.PagesPrinted);

        var status = service.GetPrinterStatus("PDF");
        Assert.AreEqual("Ready", status.StatusText);
        Assert.AreEqual(0, status.QueuedJobCount);
        Assert.IsTrue(status.IsOnline);
    }

    [TestMethod]
    public async Task Print_ReplyWithoutJobId_StillSucceeds()
    {
        using var temp = new TempDirectory();
        var doc = temp.Combine("d.pdf");
        File.WriteAllText(doc, "%PDF-1.4");
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("PDF", isDefault: true));
        var renderer = new FakeDocumentRenderer(".pdf");
        var resolver = new FakeDocumentRendererResolver(new Dictionary<string, Printman.Core.Abstractions.IDocumentRenderer> { [".pdf"] = renderer });
        var server = new FakeCupsServer();
        var service = new CupsPrintService(discovery, resolver, new Printman.Services.PrintJobValidator(discovery, resolver), server.CreateClient(), new FakeExternalTools());

        server.Respond = _ => FakeCupsServer.Ok(m => m.AddGroup(IppTag.JobAttributes).AddText("job-state-message", "queued"));
        Assert.IsTrue((await service.PrintAsync(new PrintJobRequest { FilePath = doc })).Success);

        server.Respond = _ => FakeCupsServer.Ok(m => m.AddGroup(IppTag.JobAttributes).AddText("job-id", "seven"));
        Assert.IsTrue((await service.PrintAsync(new PrintJobRequest { FilePath = doc })).Success);
    }
}
