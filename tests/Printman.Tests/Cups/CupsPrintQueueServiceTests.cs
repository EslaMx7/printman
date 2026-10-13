using System.Net.Http;
using Printman.Core.Models;
using Printman.Services.Cups;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Cups;

[TestClass]
public sealed class CupsPrintQueueServiceTests
{
    private sealed class Fixture
    {
        public FakeCupsServer Server { get; } = new();
        public FakePrinterDiscoveryService Discovery { get; } = new();
        public CupsPrintQueueService Service { get; }
        public List<IppAttributeGroup> PrinterAttributes { get; } = [];
        public List<Action<IppAttributeGroup>> Jobs { get; } = [];
        public List<int> Cancelled { get; } = [];

        public Fixture()
        {
            Discovery.Printers.Add(TestData.Printer("PDF", isDefault: true));
            Discovery.Printers.Add(TestData.Printer("Laser"));
            Service = new CupsPrintQueueService(Discovery, Server.CreateClient());
            Server.Respond = r => r.Message.Code switch
            {
                IppOperation.GetJobs => FakeCupsServer.Ok(m =>
                {
                    foreach (var job in Jobs) job(m.AddGroup(IppTag.JobAttributes));
                }),
                IppOperation.GetPrinterAttributes => FakeCupsServer.Ok(m =>
                {
                    var g = m.AddGroup(IppTag.PrinterAttributes);
                    foreach (var fill in PrinterAttributes) g.Attributes.AddRange(fill.Attributes);
                }),
                IppOperation.CancelJob => Cancel(r),
                _ => FakeCupsServer.Error(IppStatus.OperationNotSupported)
            };
        }

        private IppMessage Cancel(FakeCupsServer.Received r)
        {
            var uri = r.Message.Group(IppTag.OperationAttributes)!.Get("job-uri")!.First!.AsString()!;
            int id = int.Parse(uri[(uri.LastIndexOf('/') + 1)..]);
            if (id >= 100) return FakeCupsServer.Error(IppStatus.NotFound, "Job does not exist.");
            Cancelled.Add(id);
            Jobs.RemoveAll(_ => false);
            return FakeCupsServer.Ok();
        }

        public void AddJob(int id, string printer = "PDF", int state = IppJobState.Pending, string[]? reasons = null, string? message = null, Action<IppAttributeGroup>? extra = null) =>
            Jobs.Add(g =>
            {
                g.AddInteger("job-id", id);
                g.AddName("job-name", $"doc{id}.pdf");
                g.AddName("job-originating-user-name", "alice");
                g.AddUri("job-printer-uri", CupsClient.PrinterUri(printer));
                g.AddEnum("job-state", state);
                if (reasons != null) g.AddKeywords("job-state-reasons", reasons);
                if (message != null) g.AddText("job-printer-state-message", message);
                extra?.Invoke(g);
            });

        public void Status(Action<IppAttributeGroup> fill)
        {
            var g = new IppAttributeGroup(IppTag.PrinterAttributes);
            fill(g);
            PrinterAttributes.Add(g);
        }
    }

    [TestMethod]
    public void GetJobs_ParsesCupsJobs_AndIncludesPipelineJobs()
    {
        var fx = new Fixture();
        fx.AddJob(1, state: IppJobState.Processing, message: "Printing page 2", extra: g =>
        {
            g.AddInteger("job-k-octets", 3);
            g.AddInteger("time-at-creation", 1_700_000_000);
            g.AddInteger("job-impressions", 6);
            g.AddInteger("job-impressions-completed", 2);
        });
        fx.AddJob(2, printer: "Laser");
        fx.Jobs.Add(g => g.AddName("job-name", "no id, skipped"));
        using var cts = new CancellationTokenSource();
        fx.Service.RegisterPipelineJob("abc", "PDF", "upload.pdf", 4, cts);

        var jobs = fx.Service.GetJobs();

        Assert.AreEqual(3, jobs.Count);
        var pipeline = jobs.Single(j => j.IsPrintmanPipelineJob);
        Assert.AreEqual("abc", pipeline.PipelineJobId);

        var printing = jobs.Single(j => j.JobId == 1);
        Assert.AreEqual("PDF", printing.PrinterName);
        Assert.AreEqual("doc1.pdf", printing.DocumentName);
        Assert.AreEqual("alice", printing.UserName);
        Assert.AreEqual(PrintJobStatusCode.Printing, printing.StatusCode);
        Assert.AreEqual("Printing: Printing page 2", printing.StatusDescription);
        Assert.AreEqual(3 * 1024, printing.SizeBytes);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).LocalDateTime, printing.SubmittedAt);
        Assert.AreEqual(6, printing.TotalPages);
        Assert.AreEqual(2, printing.PagesPrinted);
        Assert.IsTrue(printing.CanCancel);

        var queued = jobs.Single(j => j.JobId == 2);
        Assert.AreEqual("Laser", queued.PrinterName);
        Assert.AreEqual(PrintJobStatusCode.Queued, queued.StatusCode);
        Assert.AreEqual(0, queued.SizeBytes);
        Assert.IsTrue((DateTime.Now - queued.SubmittedAt).Duration() < TimeSpan.FromMinutes(1), "no creation time: now");

        var request = fx.Server.Requests.Single().Message.Group(IppTag.OperationAttributes)!;
        Assert.AreEqual("ipp://localhost/", request.Get("printer-uri")!.First!.AsString());
        Assert.AreEqual("not-completed", request.Get("which-jobs")!.First!.AsString());
    }

    [TestMethod]
    public void GetJobs_ForOnePrinter_ResolvesFuzzyName_AndFallsBackToQuery()
    {
        var fx = new Fixture();
        fx.AddJob(5, printer: "Laser");

        var jobs = fx.Service.GetJobs("las");
        Assert.AreEqual("Laser", jobs.Single().PrinterName);
        Assert.AreEqual("/printers/Laser", fx.Server.Requests.Single().Resource);
        Assert.AreEqual("ipp://localhost/printers/Laser", fx.Server.Requests.Single().Message.Group(IppTag.OperationAttributes)!.Get("printer-uri")!.First!.AsString());

        fx.Server.Requests.Clear();
        fx.Jobs.Clear();
        fx.Jobs.Add(g => g.AddInteger("job-id", 9)); // no printer uri on the job: the queried name is used
        var unknown = fx.Service.GetJobs("Unknown Queue");
        Assert.AreEqual("/printers/Unknown%20Queue", fx.Server.Requests.Single().Resource);
        Assert.AreEqual("Unknown Queue", unknown.Single().PrinterName);
        Assert.AreEqual("Document", unknown.Single().DocumentName);
    }

    [TestMethod]
    public void GetJobs_CupsDown_ReturnsPipelineJobsOnly()
    {
        var fx = new Fixture();
        fx.Server.Fail = () => new HttpRequestException("refused");
        using var cts = new CancellationTokenSource();
        fx.Service.RegisterPipelineJob("abc", "PDF", "upload.pdf", 1, cts);

        var jobs = fx.Service.GetJobs("PDF");

        Assert.AreEqual(1, jobs.Count);
        Assert.IsTrue(jobs[0].IsPrintmanPipelineJob);
    }

    [TestMethod]
    public void GetJobs_TranslatesJobStates()
    {
        var fx = new Fixture();
        fx.AddJob(1, state: IppJobState.PendingHeld);
        fx.AddJob(2, state: IppJobState.Processing);
        fx.AddJob(3, state: IppJobState.ProcessingStopped);
        fx.AddJob(4, state: IppJobState.Canceled);
        fx.AddJob(5, state: IppJobState.Aborted);
        fx.AddJob(6, state: IppJobState.Completed);
        fx.AddJob(7, state: 42);
        fx.AddJob(8, state: IppJobState.Pending, reasons: ["media-empty-error"]);
        fx.AddJob(9, state: IppJobState.Processing, reasons: ["other", "media-jam-error"]);
        fx.AddJob(10, state: IppJobState.Pending, reasons: ["media-needed"]);
        fx.Jobs.Add(g => g.AddInteger("job-id", 11)); // no job-state attribute

        var jobs = fx.Service.GetJobs().ToDictionary(j => j.JobId);

        Assert.AreEqual((PrintJobStatusCode.Paused, "Held"), (jobs[1].StatusCode, jobs[1].StatusDescription));
        Assert.AreEqual((PrintJobStatusCode.Printing, "Printing"), (jobs[2].StatusCode, jobs[2].StatusDescription));
        Assert.AreEqual((PrintJobStatusCode.Paused, "Stopped (printer paused)"), (jobs[3].StatusCode, jobs[3].StatusDescription));
        Assert.AreEqual(PrintJobStatusCode.Deleting, jobs[4].StatusCode);
        Assert.AreEqual(PrintJobStatusCode.Deleting, jobs[5].StatusCode);
        Assert.AreEqual(PrintJobStatusCode.Completed, jobs[6].StatusCode);
        Assert.AreEqual((PrintJobStatusCode.Unknown, "Unknown"), (jobs[7].StatusCode, jobs[7].StatusDescription));
        Assert.AreEqual(PrintJobStatusCode.PaperOut, jobs[8].StatusCode);
        Assert.AreEqual(PrintJobStatusCode.PaperJam, jobs[9].StatusCode);
        Assert.AreEqual(PrintJobStatusCode.PaperOut, jobs[10].StatusCode);
        Assert.AreEqual(PrintJobStatusCode.Queued, jobs[11].StatusCode);
    }

    [TestMethod]
    public void GetPrinterStatus_Ready_And_Spooling()
    {
        var fx = new Fixture();
        fx.Status(g => g.AddEnum("printer-state", IppPrinterState.Idle));

        var ready = fx.Service.GetPrinterStatus("pdf");
        Assert.AreEqual("PDF", ready.PrinterName);
        Assert.AreEqual("Ready", ready.StatusText);
        Assert.IsTrue(ready.IsOnline);
        Assert.IsFalse(ready.HasError);
        Assert.AreEqual("/printers/PDF", fx.Server.Requests.Single().Resource);

        fx.PrinterAttributes.Clear();
        fx.Status(g => g.AddInteger("queued-job-count", 2));
        var spooling = fx.Service.GetPrinterStatus("PDF");
        Assert.AreEqual("Spooling / Active", spooling.StatusText);
        Assert.AreEqual(2, spooling.QueuedJobCount);
    }

    [TestMethod]
    public void GetPrinterStatus_MapsStateReasons()
    {
        var fx = new Fixture();
        fx.Status(g =>
        {
            g.AddEnum("printer-state", IppPrinterState.Processing);
            g.AddKeywords("printer-state-reasons", ["media-jam-error", "media-empty-warning", "cover-open-error", "toner-empty-error", "other-error", "marker-supply-low-report"]);
            g.AddBoolean("printer-is-accepting-jobs", false);
        });

        var status = fx.Service.GetPrinterStatus("PDF");

        Assert.IsTrue(status.IsPaperJam);
        Assert.IsTrue(status.IsOutOfPaper);
        Assert.IsTrue(status.IsDoorOpen);
        Assert.IsTrue(status.IsBusy);
        Assert.IsTrue(status.HasError);
        Assert.IsTrue(status.IsOnline);
        Assert.IsFalse(status.IsPaused);
        Assert.AreEqual("Paper Jam, Out of Paper, Door Open, Out of Toner, other, Printing / Busy, Not accepting jobs", status.StatusText);
    }

    [TestMethod]
    [DataRow("media-needed-warning", "Out of Paper")]
    [DataRow("door-open-error", "Door Open")]
    [DataRow("interlock-open-error", "Door Open")]
    [DataRow("offline-error", "Offline")]
    [DataRow("shutdown", "Offline")]
    [DataRow("marker-supply-empty-error", "Out of Toner")]
    [DataRow("paused", "Paused")]
    [DataRow("input-tray-missing-error", "input-tray-missing")]
    [DataRow("input-tray-missing-warning", "Ready")]
    [DataRow("media-low-report", "Ready")]
    public void GetPrinterStatus_EachStateReason(string reason, string statusText)
    {
        var fx = new Fixture();
        fx.Status(g => g.AddKeyword("printer-state-reasons", reason));

        Assert.AreEqual(statusText, fx.Service.GetPrinterStatus("PDF").StatusText);
    }

    [TestMethod]
    public void GetPrinterStatus_OfflineAndPaused()
    {
        var fx = new Fixture();
        fx.Status(g =>
        {
            g.AddEnum("printer-state", IppPrinterState.Stopped);
            g.AddKeywords("printer-state-reasons", ["connecting-to-device", "paused", "door-open-report", "something-warning"]);
        });

        var status = fx.Service.GetPrinterStatus("PDF");

        Assert.IsFalse(status.IsOnline);
        Assert.IsTrue(status.IsPaused);
        Assert.IsFalse(status.IsDoorOpen, "-report reasons are informational");
        Assert.AreEqual("Offline, Paused", status.StatusText);

        fx.PrinterAttributes.Clear();
        fx.Status(g => g.AddEnum("printer-state", IppPrinterState.Stopped));
        Assert.AreEqual("Paused", fx.Service.GetPrinterStatus("PDF").StatusText);
    }

    [TestMethod]
    public void GetPrinterStatus_UnknownPrinterOrCupsDown_IsOffline()
    {
        var fx = new Fixture();
        fx.Server.Respond = _ => FakeCupsServer.Error(IppStatus.NotFound, "The printer does not exist.");

        var missing = fx.Service.GetPrinterStatus("Nope");
        Assert.AreEqual("Nope", missing.PrinterName);
        Assert.AreEqual("Offline / Unavailable", missing.StatusText);
        Assert.IsFalse(missing.IsOnline);
        Assert.IsTrue(missing.HasError);

        fx.Server.Respond = _ => FakeCupsServer.Ok(); // no printer group at all
        Assert.AreEqual("Offline / Unavailable", fx.Service.GetPrinterStatus("PDF").StatusText);
    }

    [TestMethod]
    public void CancelSpoolerJob_SendsCancelJob_ByJobUri()
    {
        var fx = new Fixture();

        Assert.IsTrue(fx.Service.CancelSpoolerJob("PDF", 12));
        Assert.IsFalse(fx.Service.CancelSpoolerJob("PDF", 100));

        CollectionAssert.AreEqual(new[] { 12 }, fx.Cancelled);
        Assert.AreEqual("/jobs/", fx.Server.Requests[0].Resource);
        Assert.AreEqual("ipp://localhost/jobs/12", fx.Server.Requests[0].Message.Group(IppTag.OperationAttributes)!.Get("job-uri")!.First!.AsString());
    }

    [TestMethod]
    public void PurgeSpoolerQueue_CancelsEachCupsJob_AndPipelineJobs()
    {
        var fx = new Fixture();
        fx.AddJob(1);
        fx.AddJob(2);
        fx.AddJob(100); // cupsd refuses this one
        using var cts = new CancellationTokenSource();
        fx.Service.RegisterPipelineJob("p1", "PDF", "a.pdf", 1, cts);
        using var other = new CancellationTokenSource();
        fx.Service.RegisterPipelineJob("p2", "Laser", "b.pdf", 1, other);

        int purged = fx.Service.PurgeSpoolerQueue("pdf");

        Assert.AreEqual(2, purged);
        CollectionAssert.AreEqual(new[] { 1, 2 }, fx.Cancelled);
        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.IsFalse(other.IsCancellationRequested);
    }

    [TestMethod]
    public void CancelJob_PipelineId_NumericPipelineId_CupsId()
    {
        var fx = new Fixture();
        using var cts = new CancellationTokenSource();
        fx.Service.RegisterPipelineJob("p1", "PDF", "a.pdf", 1, cts);
        int numeric = fx.Service.GetJobs().Single(j => j.IsPrintmanPipelineJob).JobId;

        Assert.IsTrue(fx.Service.CancelJob(null, "p1"));
        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.IsTrue(fx.Service.CancelJob(null, numeric.ToString()));
        Assert.IsFalse(fx.Service.CancelJob(null, "-123456"));
        Assert.IsTrue(fx.Service.CancelJob(null, "7"));
        Assert.IsTrue(fx.Service.CancelJob("PDF", "8"));
        Assert.IsFalse(fx.Service.CancelJob("PDF", "100"));
        Assert.IsFalse(fx.Service.CancelJob("PDF", "not-a-job"));

        CollectionAssert.AreEqual(new[] { 7, 8 }, fx.Cancelled);
    }

    [TestMethod]
    public void GetJobs_OddPrinterUris_FallBackToQueriedName()
    {
        var fx = new Fixture();
        fx.Jobs.Add(g => { g.AddInteger("job-id", 1); g.AddUri("job-printer-uri", "ipp://localhost/printers/"); });
        fx.Jobs.Add(g => { g.AddInteger("job-id", 2); g.AddUri("job-printer-uri", ""); });
        fx.Jobs.Add(g => { g.AddInteger("job-id", 3); g.AddUri("job-printer-uri", "ipp://localhost/printers/My%20Queue"); });

        var jobs = fx.Service.GetJobs("Laser").ToDictionary(j => j.JobId);

        Assert.AreEqual("Laser", jobs[1].PrinterName);
        Assert.AreEqual("Laser", jobs[2].PrinterName);
        Assert.AreEqual("My Queue", jobs[3].PrinterName);

        fx.Server.Requests.Clear();
        Assert.AreEqual("", fx.Service.GetJobs().Single(j => j.JobId == 1).PrinterName, "no printer queried and none on the job");
    }

    [TestMethod]
    public void PurgeSpoolerQueue_UnknownPrinter_UsesNameAsGiven()
    {
        var fx = new Fixture();
        fx.AddJob(3, printer: "Ghost");

        Assert.AreEqual(1, fx.Service.PurgeSpoolerQueue("Ghost"));
        Assert.AreEqual("/printers/Ghost", fx.Server.Requests[0].Resource);
    }

    [TestMethod]
    public void PipelineJobLifecycle_DelegatesToTracker()
    {
        var fx = new Fixture();
        using var cts = new CancellationTokenSource();
        fx.Service.RegisterPipelineJob("p1", "PDF", "a.pdf", 3, cts);
        fx.Service.UpdatePipelineJob("p1", 2, PrintJobStatusCode.Printing, "page 2");

        var job = fx.Service.GetJobs("PDF").Single();
        Assert.AreEqual(2, job.PagesPrinted);
        Assert.AreEqual("page 2", job.StatusDescription);

        Assert.IsTrue(fx.Service.CancelPipelineJob("p1"));
        fx.Service.UnregisterPipelineJob("p1");
        Assert.AreEqual(0, fx.Service.GetJobs("PDF").Count);
    }
}
