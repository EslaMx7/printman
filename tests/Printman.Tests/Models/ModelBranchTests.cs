using System.Globalization;
using Printman.Core.Models;
using Printman.Tests.Support;

namespace Printman.Tests.Models;

/// <summary>Branch-focused coverage for attribute lookup fallbacks, job state and UTF-8 truncation.</summary>
[TestClass]
public sealed class ModelBranchTests
{
    // ------------------------------------------------------------------ IppMessage attribute lookup

    [TestMethod]
    public void FindRequestAttribute_FallsBackToJobGroup()
    {
        var message = new IppMessage();
        message.AddGroup(IppTag.JobAttributes).AddKeyword("sides", "one-sided");

        Assert.AreEqual("one-sided", message.FindRequestAttribute("sides")!.First!.AsString());
    }

    [TestMethod]
    public void FindJobAttribute_FallsBackToOperationGroup()
    {
        var message = new IppMessage();
        message.AddGroup(IppTag.OperationAttributes).AddInteger("copies", 2);

        Assert.AreEqual(2, message.FindJobAttribute("copies")!.First!.AsInt());
    }

    // ------------------------------------------------------------------ IppJob state machine

    [TestMethod]
    public void Terminate_WithNullReason_UsesFallbackReasonAndMessage()
    {
        var job = NewJob();
        job.Terminate(IppJobState.Canceled, null!);

        var (state, reason, message) = job.GetState();
        Assert.AreEqual(IppJobState.Canceled, state);
        Assert.AreEqual("none", reason);
        Assert.AreEqual(string.Empty, message);
    }

    [TestMethod]
    public void GetState_FailedTicket_ReportsAbortedWithTicketMessage()
    {
        var job = NewJob();
        var ticket = new PipelineTicket(new PipelineBatch());
        ticket.Finish(PipelineJobState.Failed, "boom");
        job.Attach(ticket, "application/pdf");

        var (state, reason, message) = job.GetState();
        Assert.AreEqual(IppJobState.Aborted, state);
        Assert.AreEqual("aborted-by-system", reason);
        Assert.AreEqual("boom", message);
    }

    [TestMethod]
    public void GetState_FailedTicketWithoutMessage_UsesDefaultMessage()
    {
        var job = NewJob();
        var ticket = new PipelineTicket(new PipelineBatch());
        ticket.Finish(PipelineJobState.Failed);
        job.Attach(ticket, "application/pdf");

        Assert.AreEqual("Printing failed.", job.GetState().Message);
    }

    [TestMethod]
    public void GetState_TicketStates_AreTranslated()
    {
        Assert.AreEqual(IppJobState.Pending, StateFor(PipelineJobState.Pending));
        Assert.AreEqual(IppJobState.Processing, StateFor(PipelineJobState.Processing));
        Assert.AreEqual(IppJobState.Completed, StateFor(PipelineJobState.Completed));
        Assert.AreEqual(IppJobState.Canceled, StateFor(PipelineJobState.Canceled));
    }

    [TestMethod]
    public void GetState_WithoutTicket_ReportsJobIncoming()
    {
        var job = NewJob();
        Assert.IsTrue(job.AwaitingDocument);
        Assert.AreEqual("job-incoming", job.GetState().Reason);
    }

    // ------------------------------------------------------------------ TruncateUtf8

    [TestMethod]
    public void TruncateUtf8_ShortValue_IsReturnedUnchanged() =>
        Assert.AreEqual("short", SharedPrinter.TruncateUtf8("short", 10));

    [TestMethod]
    public void TruncateUtf8_EmptyValue_IsReturnedUnchanged() =>
        Assert.AreEqual(string.Empty, SharedPrinter.TruncateUtf8(string.Empty, 5));

    [TestMethod]
    public void TruncateUtf8_LongAsciiValue_IsCutToByteBudget() =>
        Assert.AreEqual("aaaaa", SharedPrinter.TruncateUtf8(new string('a', 10), 5));

    [TestMethod]
    public void TruncateUtf8_MultibyteValue_NeverSplitsARune() =>
        Assert.AreEqual("\u00e9\u00e9", SharedPrinter.TruncateUtf8(new string('\u00e9', 5), 5));

    // ------------------------------------------------------------------ PageRange bounds

    [TestMethod]
    public void Parse_EndBelowOne_Throws() =>
        Assert.ThrowsExactly<FormatException>(() => PageRange.Parse("5-0"));

    [TestMethod]
    public void Parse_OpenStartBelowOne_Throws() =>
        Assert.ThrowsExactly<FormatException>(() => PageRange.Parse("0-"));

    // ------------------------------------------------------------------ PaperSizeOption

    [TestMethod]
    public void PaperSizeOption_ConvertsAndFormats()
    {
        var size = TestData.A4;

        Assert.AreEqual(210.1, size.WidthMm);
        Assert.AreEqual(296.9, size.HeightMm);
        StringAssert.Contains(size.ToString(), "A4");
        StringAssert.Contains(size.ToString(), "mm");
    }

    // ------------------------------------------------------------------ helpers

    private static IppJob NewJob() => new()
    {
        Id = 1,
        PrinterSlug = "fake-printer",
        Name = "doc",
        UserName = "user"
    };

    private static int StateFor(PipelineJobState ticketState)
    {
        var job = NewJob();
        var ticket = new PipelineTicket(new PipelineBatch());
        switch (ticketState)
        {
            case PipelineJobState.Processing: ticket.MarkProcessing(); break;
            case PipelineJobState.Completed: ticket.Finish(PipelineJobState.Completed); break;
            case PipelineJobState.Canceled: ticket.Finish(PipelineJobState.Canceled); break;
        }

        job.Attach(ticket, "application/pdf");
        return job.GetState().State;
    }
}
