using System.Reflection;
using Printman.Core.Models;

namespace Printman.Tests.Models;

[TestClass]
public sealed class PipelineModelTests
{
    // ---------------------------------------------------------------------
    // PipelineItem / PipelineBatch
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PipelineItem_Defaults()
    {
        var item = new PipelineItem { FileId = "abc" };

        Assert.AreEqual("abc", item.FileId);
        Assert.IsNull(item.DisplayName);
        Assert.IsTrue(item.PageRange.IsAllPages);
        Assert.AreEqual(1, item.Copies);
        Assert.IsNull(item.PaperSizeName);
        Assert.AreEqual(PrintOrientation.Auto, item.Orientation);
        Assert.AreEqual(PrintDuplex.Default, item.Duplex);
        Assert.AreEqual(PrintColorMode.Default, item.ColorMode);
        Assert.IsTrue(item.FitToPage);
        Assert.IsFalse(item.FullPage);
        Assert.IsNull(item.JobTitle);
    }

    [TestMethod]
    public void PipelineItem_InitProperties_AreRetained()
    {
        var item = new PipelineItem
        {
            FileId = "abc",
            DisplayName = "doc.pdf",
            PageRange = PageRange.Parse("1-2"),
            Copies = 3,
            PaperSizeName = "A4",
            Orientation = PrintOrientation.Landscape,
            Duplex = PrintDuplex.Horizontal,
            ColorMode = PrintColorMode.Color,
            FitToPage = false,
            FullPage = true,
            JobTitle = "title"
        };

        Assert.AreEqual("doc.pdf", item.DisplayName);
        Assert.AreEqual("1,2", string.Join(',', item.PageRange.ResolvePages(5)));
        Assert.AreEqual(3, item.Copies);
        Assert.AreEqual("A4", item.PaperSizeName);
        Assert.AreEqual(PrintOrientation.Landscape, item.Orientation);
        Assert.AreEqual(PrintDuplex.Horizontal, item.Duplex);
        Assert.AreEqual(PrintColorMode.Color, item.ColorMode);
        Assert.IsFalse(item.FitToPage);
        Assert.IsTrue(item.FullPage);
        Assert.AreEqual("title", item.JobTitle);
    }

    [TestMethod]
    public void PipelineBatch_Defaults()
    {
        var batch = new PipelineBatch();

        Assert.IsNull(batch.Printer);
        Assert.AreEqual(0, batch.Items.Count);
        Assert.AreEqual("web", batch.Source);
    }

    [TestMethod]
    public void PipelineBatch_InitProperties_AreRetained()
    {
        var item = new PipelineItem { FileId = "abc" };
        var batch = new PipelineBatch
        {
            Printer = "HP",
            Items = [item],
            Source = "ipp"
        };

        Assert.AreEqual("HP", batch.Printer);
        Assert.AreEqual(1, batch.Items.Count);
        Assert.AreSame(item, batch.Items[0]);
        Assert.AreEqual("ipp", batch.Source);
    }

    // ---------------------------------------------------------------------
    // PipelineTicket
    // ---------------------------------------------------------------------

    [TestMethod]
    public void PipelineTicket_Defaults()
    {
        var batch = new PipelineBatch();
        var ticket = new PipelineTicket(batch);

        Assert.AreEqual(8, ticket.Id.Length);
        Assert.AreSame(batch, ticket.Batch);
        Assert.AreEqual(PipelineJobState.Pending, ticket.State);
        Assert.AreEqual(0, ticket.PagesPrinted);
        Assert.IsNull(ticket.Message);
        Assert.IsNull(ticket.StartedAt);
        Assert.IsNull(ticket.CompletedAt);
        Assert.IsFalse(ticket.IsFinished);
        Assert.IsFalse(ticket.CancellationToken.IsCancellationRequested);
        Assert.IsTrue(ticket.CreatedAt <= DateTime.UtcNow);
    }

    [TestMethod]
    public void PipelineTicket_Cancel_ReturnsTrueUntilFinished()
    {
        var ticket = new PipelineTicket(new PipelineBatch());

        Assert.IsTrue(ticket.Cancel());
        Assert.IsTrue(ticket.CancellationToken.IsCancellationRequested);
        // Cancellation is only a request; the worker decides the final state.
        Assert.IsFalse(ticket.IsFinished);
    }

    [TestMethod]
    public void PipelineTicket_Cancel_ReturnsFalseOnceFinished()
    {
        var ticket = new PipelineTicket(new PipelineBatch());
        ticket.Finish(PipelineJobState.Completed);

        Assert.IsFalse(ticket.Cancel());
    }

    [TestMethod]
    public void PipelineTicket_CancelAfterSourceDisposed_IsSwallowed()
    {
        var ticket = new PipelineTicket(new PipelineBatch());
        var source = (CancellationTokenSource)typeof(PipelineTicket)
            .GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ticket)!;
        source.Dispose();

        Assert.IsTrue(ticket.Cancel());
    }

    [TestMethod]
    public void PipelineTicket_MarkProcessing_SetsStateAndStartTime()
    {
        var ticket = new PipelineTicket(new PipelineBatch());

        ticket.MarkProcessing();

        Assert.AreEqual(PipelineJobState.Processing, ticket.State);
        Assert.IsNotNull(ticket.StartedAt);
        Assert.IsFalse(ticket.IsFinished);
    }

    [TestMethod]
    public void PipelineTicket_MarkProcessingAfterFinish_IsNoOp()
    {
        var ticket = new PipelineTicket(new PipelineBatch());
        ticket.Finish(PipelineJobState.Completed);

        ticket.MarkProcessing();

        Assert.AreEqual(PipelineJobState.Completed, ticket.State);
        Assert.IsNull(ticket.StartedAt);
    }

    [TestMethod]
    public void PipelineTicket_AddPagesPrinted_Accumulates()
    {
        var ticket = new PipelineTicket(new PipelineBatch());

        ticket.AddPagesPrinted(2);
        ticket.AddPagesPrinted(3);

        Assert.AreEqual(5, ticket.PagesPrinted);
    }

    [TestMethod]
    public void PipelineTicket_Finish_IsIdempotent()
    {
        var ticket = new PipelineTicket(new PipelineBatch());

        ticket.Finish(PipelineJobState.Completed, "done");
        ticket.Finish(PipelineJobState.Failed, "ignored");

        Assert.AreEqual(PipelineJobState.Completed, ticket.State);
        Assert.AreEqual("done", ticket.Message);
        Assert.IsNotNull(ticket.CompletedAt);
        Assert.AreEqual(PipelineJobState.Completed, ticket.Completion.GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow(PipelineJobState.Completed)]
    [DataRow(PipelineJobState.Failed)]
    [DataRow(PipelineJobState.Canceled)]
    public void PipelineTicket_TerminalStates_ReportIsFinished(PipelineJobState state)
    {
        var ticket = new PipelineTicket(new PipelineBatch());

        ticket.Finish(state, "message");

        Assert.IsTrue(ticket.IsFinished);
        Assert.AreEqual(state, ticket.State);
        Assert.AreEqual("message", ticket.Message);
        Assert.AreEqual(state, ticket.Completion.GetAwaiter().GetResult());
    }
}
