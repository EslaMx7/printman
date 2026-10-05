using System.Reflection;
using System.Text;
using System.Threading.Channels;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Services;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Services;

[TestClass]
public sealed class PrintJobPipelineTests
{
    [TestMethod]
    public void Enqueue_IncrementsPendingCountUntilProcessed()
    {
        using var fx = new PipelineFixture();

        var ticket = fx.Pipeline.Enqueue(TestData.Batch());

        Assert.AreEqual(PipelineJobState.Pending, ticket.State);
        Assert.AreEqual(1, fx.Pipeline.PendingCount);
    }

    [TestMethod]
    public void Enqueue_WhenQueueWriterCompleted_FinishesTicketAsFailed()
    {
        using var fx = new PipelineFixture();
        var channel = GetQueue(fx.Pipeline);
        channel.Writer.TryComplete();

        var ticket = fx.Pipeline.Enqueue(TestData.Batch());

        Assert.AreEqual(PipelineJobState.Failed, ticket.State);
        Assert.AreEqual("Print queue is not accepting jobs.", ticket.Message);
        Assert.AreEqual(0, fx.Pipeline.PendingCount);
    }

    [TestMethod]
    public async Task RunAsync_AlreadyCanceled_DrainsQueuedTicketsAsCanceled()
    {
        using var fx = new PipelineFixture();
        var ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item("abc123")));

        await fx.Pipeline.RunAsync(new CancellationToken(canceled: true)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(PipelineJobState.Canceled, ticket.State);
        Assert.AreEqual("Server stopped.", ticket.Message);
        Assert.AreEqual(0, fx.Pipeline.PendingCount);
    }

    [TestMethod]
    public async Task RunAsync_Cancellation_StopsWorkerCleanly()
    {
        using var fx = new PipelineFixture();
        using var cts = new CancellationTokenSource();

        var worker = fx.Pipeline.RunAsync(cts.Token);
        cts.Cancel();

        await worker.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task RunAsync_WhenQueueCompleted_ExitsCleanly()
    {
        using var fx = new PipelineFixture();
        GetQueue(fx.Pipeline).Writer.TryComplete();

        await fx.Pipeline.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ProcessBatch_SuccessfulPrint_CompletesAndReportsProgress()
    {
        using var fx = new PipelineFixture();
        using var sink = new EventSink(fx.EventHub);
        var cached = await fx.CacheFileAsync();

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item(cached.FileId)));
            Assert.AreEqual(PipelineJobState.Completed, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.AreEqual(1, ticket!.PagesPrinted);
        Assert.AreEqual(1, fx.PrintService.Requests.Count);
        Assert.AreEqual("Fake Printer", fx.PrintService.Requests[0].TargetPrinterName);
        Assert.IsNull(fx.PrintService.Requests[0].OutputFilePath);
        Assert.AreEqual(1, fx.Queue.RegisteredPipelineJobs.Count);
        Assert.AreEqual(1, fx.Queue.UnregisteredPipelineJobs.Count);
        Assert.AreEqual(fx.Queue.RegisteredPipelineJobs[0], fx.Queue.UnregisteredPipelineJobs[0]);
        Assert.AreEqual(0, fx.Pipeline.PendingCount);
        Assert.IsTrue(await WaitForAsync(() => sink.Events.Any(e => e.Type == "progress" && e.FileName == "doc.pdf"), TimeSpan.FromSeconds(5)));
        Assert.IsTrue(await WaitForAsync(() => sink.Events.Any(e => e.Type == "completed" && e.PagesPrinted == 1), TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task ProcessBatch_UnknownPrinter_FailsAndPublishesError()
    {
        using var fx = new PipelineFixture();
        using var sink = new EventSink(fx.EventHub);

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("No Such Printer"));
            Assert.AreEqual(PipelineJobState.Failed, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.AreEqual("Target printer could not be resolved.", ticket!.Message);
        Assert.IsTrue(await WaitForAsync(() => sink.Events.Any(e => e.Type == "error"), TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, fx.PrintService.Requests.Count);
    }

    [TestMethod]
    public async Task ProcessBatch_MissingCachedFile_Fails()
    {
        using var fx = new PipelineFixture();

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item("does-not-exist")));
            Assert.AreEqual(PipelineJobState.Failed, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.AreEqual("Document not found in cache.", ticket!.Message);
        Assert.AreEqual(0, fx.PrintService.Requests.Count);
        Assert.AreEqual(0, fx.Queue.RegisteredPipelineJobs.Count);
    }

    [TestMethod]
    public async Task ProcessBatch_PreCanceledTicket_IsCanceledBeforePrinting()
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();
        var ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item(cached.FileId)));
        ticket.Cancel();

        await RunWorkerAsync(fx.Pipeline, async () =>
            Assert.AreEqual(PipelineJobState.Canceled, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5))));

        Assert.AreEqual("Canceled before printing started.", ticket.Message);
        Assert.AreEqual(0, fx.PrintService.Requests.Count);
    }

    [TestMethod]
    public async Task ProcessBatch_ZeroCopies_IsClampedToOne()
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();
        var item = new PipelineItem { FileId = cached.FileId, Copies = 0 };

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", item));
            await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        });

        Assert.AreEqual(PipelineJobState.Completed, ticket!.State);
        Assert.AreEqual(1, fx.PrintService.Requests.Single().Copies);
    }

    [TestMethod]
    public async Task ProcessBatch_NullDisplayNameAndJobTitle_FallsBackToCachedFileName()
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();
        var item = new PipelineItem { FileId = cached.FileId };

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", item));
            await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        });

        var request = fx.PrintService.Requests.Single();
        Assert.AreEqual("doc.pdf", request.JobTitle);
        Assert.IsNull(request.OutputFilePath);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public async Task ProcessBatch_NoPrinter_UsesDefaultPrinter(string? printer)
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch(printer, TestData.Item(cached.FileId)));
            await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        });

        Assert.AreEqual(PipelineJobState.Completed, ticket!.State);
        Assert.AreEqual("Fake Printer", fx.PrintService.Requests.Single().TargetPrinterName);
    }

    [TestMethod]
    [DataRow("Fake Printer", ".prn")]
    [DataRow("Microsoft Print to PDF", ".pdf")]
    [DataRow("Microsoft XPS Document Writer", ".oxps")]
    public async Task ProcessBatch_OutputDirectory_ChoosesExtensionFromPrinter(string printerName, string expectedExtension)
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();
        fx.Discovery.FindOverride = _ => TestData.Printer(printerName);
        fx.Pipeline.OutputDirectory = fx.Temp.Path;

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch(printerName, TestData.Item(cached.FileId)));
            await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        });

        Assert.AreEqual(expectedExtension, Path.GetExtension(fx.PrintService.Requests.Single().OutputFilePath));
    }

    [TestMethod]
    public async Task ProcessBatch_OutputDirectory_SanitizesAndTruncatesDisplayName()
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();
        fx.Pipeline.OutputDirectory = fx.Temp.Path;
        var longName = new string('a', 70) + "with:invalid*chars?.pdf";
        var item = new PipelineItem { FileId = cached.FileId, DisplayName = longName };

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", item));
            await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        });

        var output = fx.PrintService.Requests.Single().OutputFilePath;
        Assert.IsNotNull(output);
        Assert.AreEqual(".prn", Path.GetExtension(output!));
        var fileStem = Path.GetFileNameWithoutExtension(output!)!;
        // timestamp (19) + '_' + truncated 60-character base name
        Assert.AreEqual(80, fileStem.Length);
        StringAssert.EndsWith(fileStem, new string('a', 60));
        Assert.IsFalse(fileStem.Contains(':'));
        Assert.IsFalse(fileStem.Contains('*'));
        Assert.IsFalse(fileStem.Contains('?'));
    }

    [TestMethod]
    public async Task ProcessBatch_TicketCanceledDuringFirstItem_StopsRemainingItems()
    {
        using var fx = new PipelineFixture();
        var first = await fx.CacheFileAsync("first.pdf", "first");
        var second = await fx.CacheFileAsync("second.pdf", "second");

        PipelineTicket? ticket = null;
        fx.PrintService.OnPrint = _ => ticket!.Cancel();

        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch(
                "Fake Printer",
                TestData.Item(first.FileId),
                TestData.Item(second.FileId)));
            Assert.AreEqual(PipelineJobState.Canceled, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.AreEqual(1, fx.PrintService.Requests.Count);
        Assert.AreEqual(1, fx.Queue.UnregisteredPipelineJobs.Count);
    }

    [TestMethod]
    public async Task ProcessBatch_PrintThrowsOperationCanceled_ReportsCancellation()
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();

        PipelineTicket? ticket = null;
        fx.PrintService.OnPrint = _ => ticket!.Cancel();
        fx.PrintService.ThrowOnPrint = new OperationCanceledException("canceled by user");

        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item(cached.FileId)));
            Assert.AreEqual(PipelineJobState.Canceled, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        });
    }

    [TestMethod]
    public async Task ProcessBatch_PrintReturnsFailure_TicketFails()
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();
        fx.PrintService.Result = PrintJobResult.Failed("Fake Printer", "paper jam");

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item(cached.FileId)));
            Assert.AreEqual(PipelineJobState.Failed, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.AreEqual("paper jam", ticket!.Message);
        Assert.AreEqual(0, ticket.PagesPrinted);
    }

    [TestMethod]
    public async Task ProcessBatch_PrintThrowsException_TicketFails()
    {
        using var fx = new PipelineFixture();
        var cached = await fx.CacheFileAsync();
        fx.PrintService.ThrowOnPrint = new InvalidOperationException("spooler exploded");

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item(cached.FileId)));
            Assert.AreEqual(PipelineJobState.Failed, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.AreEqual("spooler exploded", ticket!.Message);
        Assert.AreEqual(1, fx.Queue.RegisteredPipelineJobs.Count);
        Assert.AreEqual(1, fx.Queue.UnregisteredPipelineJobs.Count);
    }

    [TestMethod]
    public async Task ProcessBatch_PageCountInspectionThrows_StillPrintsWithFallbackPageCount()
    {
        // An empty resolver makes IDocumentRendererResolver.Resolve throw; the pipeline must
        // swallow that, fall back to a page count of one and continue printing.
        using var fx = new PipelineFixture(resolver: new FakeDocumentRendererResolver());
        var cached = await fx.CacheFileAsync();

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item(cached.FileId)));
            await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        });

        Assert.AreEqual(PipelineJobState.Completed, ticket!.State);
        Assert.AreEqual(1, fx.PrintService.Requests.Count);
    }

    [TestMethod]
    public async Task ProcessBatch_QueueRegistrationThrows_TicketFails()
    {
        using var fx = new PipelineFixture(new ThrowingQueueService());
        var cached = await fx.CacheFileAsync();

        PipelineTicket? ticket = null;
        await RunWorkerAsync(fx.Pipeline, async () =>
        {
            ticket = fx.Pipeline.Enqueue(TestData.Batch("Fake Printer", TestData.Item(cached.FileId)));
            Assert.AreEqual(PipelineJobState.Failed, await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.AreEqual("register failed", ticket!.Message);
        Assert.AreEqual(0, fx.PrintService.Requests.Count);
    }

    private static async Task RunWorkerAsync(PrintJobPipeline pipeline, Func<Task> body)
    {
        using var cts = new CancellationTokenSource();
        var worker = pipeline.RunAsync(cts.Token);
        try
        {
            await body();
        }
        finally
        {
            cts.Cancel();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    private static Channel<PipelineTicket> GetQueue(PrintJobPipeline pipeline) =>
        (Channel<PipelineTicket>)typeof(PrintJobPipeline)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(pipeline)!;

    private sealed class PipelineFixture : IDisposable
    {
        public PipelineFixture(IPrintQueueService? queueService = null, IDocumentRendererResolver? resolver = null)
        {
            Cache = new FileCacheService(Temp.Combine("cache"));
            Resolver = new FakeDocumentRendererResolver(new Dictionary<string, IDocumentRenderer>
            {
                [".pdf"] = Renderer
            });
            Discovery.Printers.Add(TestData.Printer("Fake Printer", isDefault: true));
            Pipeline = new PrintJobPipeline(Discovery, PrintService, resolver ?? Resolver, Cache, EventHub, queueService ?? Queue);
        }

        public TempDirectory Temp { get; } = new();
        public FakePrinterDiscoveryService Discovery { get; } = new();
        public FakePrintService PrintService { get; } = new();
        public FakePrintQueueService Queue { get; } = new();
        public PrintEventHub EventHub { get; } = new();
        public FileCacheService Cache { get; }
        public FakeDocumentRenderer Renderer { get; } = new(".pdf");
        public FakeDocumentRendererResolver Resolver { get; }
        public PrintJobPipeline Pipeline { get; }

        public async Task<FileCacheResult> CacheFileAsync(string name = "doc.pdf", string content = "hello")
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return await Cache.StoreFileAsync(name, stream);
        }

        public void Dispose() => Temp.Dispose();
    }

    private sealed class EventSink : IDisposable
    {
        private readonly PrintEventHub _hub;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<PrintEvent> _events = [];
        private readonly Task _pump;

        public EventSink(PrintEventHub hub)
        {
            _hub = hub;
            Reader = hub.Subscribe();
            _pump = Task.Run(async () =>
            {
                try
                {
                    await foreach (var printEvent in Reader.ReadAllAsync(_cts.Token))
                    {
                        lock (_events)
                        {
                            _events.Add(printEvent);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when the sink is disposed.
                }
            });
        }

        public ChannelReader<PrintEvent> Reader { get; }

        public IReadOnlyList<PrintEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToArray();
                }
            }
        }

        public void Dispose()
        {
            _hub.Unsubscribe(Reader);
            _cts.Cancel();
            try
            {
                _pump.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // The pump is already cancelled.
            }

            _cts.Dispose();
        }
    }

    private sealed class ThrowingQueueService : IPrintQueueService
    {
        public IReadOnlyList<PrintJobInfo> GetJobs(string? printerName = null) => [];

        public PrinterStatusInfo GetPrinterStatus(string printerName) => new() { PrinterName = printerName };

        public bool CancelSpoolerJob(string printerName, int jobId) => false;

        public int PurgeSpoolerQueue(string printerName) => 0;

        public void RegisterPipelineJob(string pipelineJobId, string printerName, string documentName, int totalPages, CancellationTokenSource cts) =>
            throw new InvalidOperationException("register failed");

        public void UpdatePipelineJob(string pipelineJobId, int pagesPrinted, PrintJobStatusCode status, string description) { }

        public void UnregisterPipelineJob(string pipelineJobId) { }

        public bool CancelPipelineJob(string pipelineJobId) => false;

        public bool CancelJob(string? printerName, string jobIdOrPipelineId) => false;
    }
}
