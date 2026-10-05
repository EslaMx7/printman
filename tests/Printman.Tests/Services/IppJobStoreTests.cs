using System.Reflection;
using Printman.Core.Models;
using Printman.Services.Ipp;
using Printman.Tests.Support;

namespace Printman.Tests.Services;

[TestClass]
public sealed class IppJobStoreTests
{
    [TestMethod]
    public void Create_AssignsMonotonicallyIncreasingIds()
    {
        var store = new IppJobStore();

        var first = store.Create("printer", "job-1", "alice", new IppJobOptions());
        var second = store.Create("printer", "job-2", "bob", new IppJobOptions());
        var third = store.Create("printer", "job-3", "carol", new IppJobOptions());

        Assert.AreEqual(1, first.Id);
        Assert.AreEqual(2, second.Id);
        Assert.AreEqual(3, third.Id);
    }

    [TestMethod]
    public void Create_CopiesMetadataAndStartsAwaitingDocument()
    {
        var store = new IppJobStore();
        var options = new IppJobOptions { Copies = 3 };

        var job = store.Create("printer", "name", "alice", options);

        Assert.AreEqual("printer", job.PrinterSlug);
        Assert.AreEqual("name", job.Name);
        Assert.AreEqual("alice", job.UserName);
        Assert.AreSame(options, job.Options);
        Assert.IsTrue(job.AwaitingDocument);
    }

    [TestMethod]
    public void Get_ReturnsTrackedJobOrNull()
    {
        var store = new IppJobStore();
        var job = store.Create("printer", "job", "alice", new IppJobOptions());

        Assert.AreSame(job, store.Get(job.Id));
        Assert.IsNull(store.Get(9999));
    }

    [TestMethod]
    public void List_FiltersByPrinterAndOrdersById()
    {
        var store = new IppJobStore();
        var first = store.Create("a", "job", "alice", new IppJobOptions());
        _ = store.Create("b", "job", "bob", new IppJobOptions());
        var third = store.Create("a", "job", "carol", new IppJobOptions());

        var listed = store.List("a");

        CollectionAssert.AreEqual(new[] { first.Id, third.Id }, listed.Select(j => j.Id).ToArray());
        Assert.AreEqual(0, store.List("missing").Count);
    }

    [TestMethod]
    public void ActiveCount_CountsOnlyNonTerminalJobsForRequestedPrinter()
    {
        var store = new IppJobStore();
        _ = store.Create("a", "job-1", "alice", new IppJobOptions());
        var second = store.Create("a", "job-2", "bob", new IppJobOptions());
        _ = store.Create("b", "job-3", "carol", new IppJobOptions());

        Assert.AreEqual(2, store.ActiveCount("a"));
        Assert.AreEqual(1, store.ActiveCount("b"));

        second.Terminate(IppJobState.Completed, "done");

        Assert.AreEqual(1, store.ActiveCount("a"));
        Assert.AreEqual(0, store.ActiveCount("missing"));
    }

    [TestMethod]
    public void GetState_WithoutDocument_IsPendingIncoming()
    {
        var store = new IppJobStore();
        var job = store.Create("a", "job", "alice", new IppJobOptions());

        var state = job.GetState();

        Assert.AreEqual(IppJobState.Pending, state.State);
        Assert.AreEqual("job-incoming", state.Reason);
        Assert.AreEqual("Waiting for document data.", state.Message);
    }

    [TestMethod]
    public void GetState_AttachedTicketLifecycle_IsTracked()
    {
        var store = new IppJobStore();
        var job = store.Create("a", "job", "alice", new IppJobOptions());
        var ticket = new PipelineTicket(TestData.Batch());

        job.Attach(ticket, "application/pdf");

        Assert.IsFalse(job.AwaitingDocument);
        Assert.AreEqual("application/pdf", job.DocumentFormat);
        Assert.AreEqual(IppJobState.Pending, job.GetState().State);

        ticket.MarkProcessing();
        Assert.AreEqual(IppJobState.Processing, job.GetState().State);
        Assert.AreEqual("job-printing", job.GetState().Reason);
        Assert.IsNotNull(job.StartedAt);

        ticket.Finish(PipelineJobState.Completed);
        Assert.AreEqual(IppJobState.Completed, job.GetState().State);
        Assert.AreEqual("job-completed-successfully", job.GetState().Reason);
        Assert.IsNotNull(job.CompletedAt);
    }

    [TestMethod]
    public void GetState_CanceledTicket_MapsToCanceled()
    {
        var store = new IppJobStore();
        var job = store.Create("a", "job", "alice", new IppJobOptions());
        var ticket = new PipelineTicket(TestData.Batch());
        job.Attach(ticket, "application/pdf");

        ticket.Finish(PipelineJobState.Canceled);

        var state = job.GetState();
        Assert.AreEqual(IppJobState.Canceled, state.State);
        Assert.AreEqual("job-canceled-by-user", state.Reason);
    }

    [TestMethod]
    public void GetState_FailedTicket_MapsToAbortedWithTicketMessage()
    {
        var store = new IppJobStore();
        var job = store.Create("a", "job", "alice", new IppJobOptions());
        var ticket = new PipelineTicket(TestData.Batch());
        job.Attach(ticket, "application/pdf");

        ticket.Finish(PipelineJobState.Failed, "no paper");

        var state = job.GetState();
        Assert.AreEqual(IppJobState.Aborted, state.State);
        Assert.AreEqual("aborted-by-system", state.Reason);
        Assert.AreEqual("no paper", state.Message);
    }

    [TestMethod]
    public void Terminate_OverridesStateAndIsIdempotent()
    {
        var store = new IppJobStore();
        var job = store.Create("a", "job", "alice", new IppJobOptions());

        job.Terminate(IppJobState.Canceled, "job-canceled-by-user");

        Assert.IsFalse(job.AwaitingDocument);
        var state = job.GetState();
        Assert.AreEqual(IppJobState.Canceled, state.State);
        Assert.AreEqual("job-canceled-by-user", state.Reason);

        job.Terminate(IppJobState.Aborted, "aborted-by-system");
        Assert.AreEqual(IppJobState.Canceled, job.GetState().State);
    }

    [TestMethod]
    public void Terminate_WithAttachedTicket_IsIgnored()
    {
        var store = new IppJobStore();
        var job = store.Create("a", "job", "alice", new IppJobOptions());
        var ticket = new PipelineTicket(TestData.Batch());
        job.Attach(ticket, "application/pdf");

        job.Terminate(IppJobState.Canceled, "ignored");

        Assert.AreEqual(IppJobState.Pending, job.GetState().State);
    }

    [TestMethod]
    public void Sweep_RemovesOldestFinishedBeyondLimit()
    {
        var store = new IppJobStore();
        for (int i = 0; i < 101; i++)
        {
            store.Create("a", "job", "alice", new IppJobOptions()).Terminate(IppJobState.Completed, "done");
        }

        // First access sweeps the oldest finished job (id 1) out of the retained window.
        Assert.IsNotNull(store.Get(101));
        Assert.IsNull(store.Get(1));
        Assert.AreEqual(100, store.List("a").Count);
    }

    [TestMethod]
    public void Sweep_RemovesFinishedJobsOlderThanRetentionWindow()
    {
        var store = new IppJobStore();
        var job = store.Create("a", "job", "alice", new IppJobOptions());
        job.Terminate(IppJobState.Completed, "done");
        SetField(job, "_overrideCompletedAt", (DateTime?)DateTime.UtcNow.AddHours(-2));

        Assert.IsNull(store.Get(job.Id));
    }

    [TestMethod]
    public void Sweep_AbortsAbandonedJobsOlderThanTimeout()
    {
        var store = new IppJobStore();
        var job = store.Create("a", "job", "alice", new IppJobOptions());
        SetField(job, "<CreatedAt>k__BackingField", DateTime.UtcNow.AddMinutes(-2));

        var tracked = store.Get(job.Id);

        Assert.AreSame(job, tracked);
        var state = job.GetState();
        Assert.AreEqual(IppJobState.Aborted, state.State);
        Assert.AreEqual("aborted-by-system", state.Reason);
    }

    private static void SetField(object target, string fieldName, object value) =>
        target.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
