using Printman.Core.Models;
using Printman.Services;

namespace Printman.Tests.Services;

/// <summary>In-flight pipeline job bookkeeping shared by the Windows and CUPS queue services.</summary>
[TestClass]
public sealed class PipelineJobTrackerTests
{
    [TestMethod]
    public void Snapshot_FiltersByPrinter_AndReportsPipelineJobs()
    {
        var tracker = new PipelineJobTracker();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        tracker.Register("job1", "PDF", "a.pdf", 3, cts1);
        tracker.Register("job2", "Laser", "b.pdf", 1, cts2);

        var pdf = tracker.Snapshot("pdf").Single();

        Assert.AreEqual("job1", pdf.PipelineJobId);
        Assert.AreEqual("a.pdf", pdf.DocumentName);
        Assert.AreEqual(3, pdf.TotalPages);
        Assert.IsTrue(pdf.IsPrintmanPipelineJob);
        Assert.IsTrue(pdf.JobId < 0);
        Assert.AreEqual(PrintJobStatusCode.Spooling, pdf.StatusCode);
        Assert.AreEqual(2, tracker.Snapshot(null).Count());
        Assert.AreEqual(2, tracker.Snapshot(" ").Count());
    }

    [TestMethod]
    public void Update_KnownJob_ChangesProgress_UnknownJobIsIgnored()
    {
        var tracker = new PipelineJobTracker();
        using var cts = new CancellationTokenSource();
        tracker.Register("job1", "PDF", "a.pdf", 3, cts);

        tracker.Update("job1", 2, PrintJobStatusCode.Printing, "Printing page 2");
        tracker.Update("missing", 1, PrintJobStatusCode.Error, "ignored");

        var job = tracker.Snapshot(null).Single();
        Assert.AreEqual(2, job.PagesPrinted);
        Assert.AreEqual(PrintJobStatusCode.Printing, job.StatusCode);
        Assert.AreEqual("Printing page 2", job.StatusDescription);
    }

    [TestMethod]
    public void Unregister_RemovesJob()
    {
        var tracker = new PipelineJobTracker();
        using var cts = new CancellationTokenSource();
        tracker.Register("job1", "PDF", "a.pdf", 1, cts);

        tracker.Unregister("job1");

        Assert.AreEqual(0, tracker.Snapshot(null).Count());
    }

    [TestMethod]
    public void Cancel_KnownJob_CancelsTokenAndMarksDeleting()
    {
        var tracker = new PipelineJobTracker();
        using var cts = new CancellationTokenSource();
        tracker.Register("job1", "PDF", "a.pdf", 1, cts);

        Assert.IsTrue(tracker.Cancel("JOB1"));
        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.AreEqual(PrintJobStatusCode.Deleting, tracker.Snapshot(null).Single().StatusCode);
        Assert.IsFalse(tracker.Cancel("missing"));
    }

    [TestMethod]
    public void Cancel_DisposedTokenSource_ReturnsFalse()
    {
        var tracker = new PipelineJobTracker();
        var cts = new CancellationTokenSource();
        tracker.Register("job1", "PDF", "a.pdf", 1, cts);
        cts.Dispose();

        Assert.IsFalse(tracker.Cancel("job1"));
    }

    [TestMethod]
    public void CancelByNumericId_MatchesListedId()
    {
        var tracker = new PipelineJobTracker();
        using var cts = new CancellationTokenSource();
        tracker.Register("job1", "PDF", "a.pdf", 1, cts);
        int listedId = tracker.Snapshot(null).Single().JobId;

        Assert.IsFalse(tracker.CancelByNumericId(listedId == -1 ? -2 : -1));
        Assert.IsTrue(tracker.CancelByNumericId(listedId));
        Assert.IsTrue(cts.IsCancellationRequested);
    }

    [TestMethod]
    public void CancelAllForPrinter_CancelsOnlyThatPrinter()
    {
        var tracker = new PipelineJobTracker();
        using var pdf = new CancellationTokenSource();
        using var laser = new CancellationTokenSource();
        tracker.Register("job1", "PDF", "a.pdf", 1, pdf);
        tracker.Register("job2", "Laser", "b.pdf", 1, laser);

        tracker.CancelAllForPrinter("pdf");

        Assert.IsTrue(pdf.IsCancellationRequested);
        Assert.IsFalse(laser.IsCancellationRequested);
    }
}
