using System.Reflection;
using System.Text;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Services;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Services;

[TestClass]
public sealed class SharedPrinterRegistryTests
{
    private static SharedPrinterRegistry Registry(
        FakePrinterDiscoveryService? discovery = null,
        FakePrintQueueService? queue = null) =>
        new(discovery ?? new FakePrinterDiscoveryService(), queue ?? new FakePrintQueueService());

    private static void ExpireCapabilitiesCache(SharedPrinterRegistry registry, string systemName, PrinterInfo? info)
    {
        var field = typeof(SharedPrinterRegistry)
            .GetField("_capabilities", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_capabilities field not found.");
        var dictionary = field.GetValue(registry)!;
        var stale = DateTime.UtcNow - TimeSpan.FromMinutes(5);
        var entry = Activator.CreateInstance(typeof(ValueTuple<PrinterInfo, DateTime>), info, stale)!;
        dictionary.GetType().GetProperty("Item")!.SetValue(dictionary, entry, [systemName]);
    }

    private static void ExpireStatusCache(SharedPrinterRegistry registry, string systemName, PrinterStatusInfo? status)
    {
        var field = typeof(SharedPrinterRegistry)
            .GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_status field not found.");
        var dictionary = field.GetValue(registry)!;
        var stale = DateTime.UtcNow - TimeSpan.FromMinutes(5);
        var entry = Activator.CreateInstance(typeof(ValueTuple<PrinterStatusInfo, DateTime>), status, stale)!;
        dictionary.GetType().GetProperty("Item")!.SetValue(dictionary, entry, [systemName]);
    }

    [TestMethod]
    public void Configure_EmptyList_SharesDefaultPrinter()
    {
        var discovery = new FakePrinterDiscoveryService { DefaultPrinter = TestData.Printer("Default One", isDefault: true) };
        var registry = Registry(discovery);

        var unresolved = registry.Configure([]);

        Assert.AreEqual(0, unresolved.Count);
        Assert.AreEqual(1, registry.Printers.Count);
        Assert.AreEqual("Default One", registry.Printers[0].SystemName);
        Assert.AreEqual("default-one", registry.Printers[0].Slug);
        Assert.AreEqual("Printman - Default One", registry.Printers[0].DisplayName);
    }

    [TestMethod]
    public void Configure_EmptyList_NoDefaultPrinter_LeavesEmpty()
    {
        var registry = Registry();

        var unresolved = registry.Configure([]);

        Assert.AreEqual(0, unresolved.Count);
        Assert.AreEqual(0, registry.Printers.Count);
    }

    [TestMethod]
    public void Configure_FuzzyName_ResolvesPrinter()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("HP LaserJet Pro"));
        var registry = Registry(discovery);

        var unresolved = registry.Configure(["laser"]);

        Assert.AreEqual(0, unresolved.Count);
        Assert.AreEqual(1, registry.Printers.Count);
        Assert.AreEqual("HP LaserJet Pro", registry.Printers[0].SystemName);
    }

    [TestMethod]
    public void Configure_UnknownNames_AreReturnedInOrder()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("Alpha"));
        var registry = Registry(discovery);

        var unresolved = registry.Configure(["Beta", "Gamma"]);

        CollectionAssert.AreEqual(new[] { "Beta", "Gamma" }, unresolved.ToArray());
        Assert.AreEqual(0, registry.Printers.Count);
    }

    [TestMethod]
    public void Configure_MixedNames_ResolvesKnownAndReportsUnknown()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("Alpha"));
        var registry = Registry(discovery);

        var unresolved = registry.Configure(["Alpha", "Missing"]);

        CollectionAssert.AreEqual(new[] { "Missing" }, unresolved.ToArray());
        Assert.AreEqual(1, registry.Printers.Count);
        Assert.AreEqual("Alpha", registry.Printers[0].SystemName);
    }

    [TestMethod]
    public void Configure_DuplicateResolutions_AreSharedOnce()
    {
        var discovery = new FakePrinterDiscoveryService
        {
            FindOverride = _ => TestData.Printer("Duplicate")
        };
        var registry = Registry(discovery);

        var unresolved = registry.Configure(["first", "second"]);

        Assert.AreEqual(0, unresolved.Count);
        Assert.AreEqual(1, registry.Printers.Count);
        Assert.AreEqual("Duplicate", registry.Printers[0].SystemName);
    }

    [TestMethod]
    public void Configure_SlugCollisions_GetNumericSuffix()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("Printer A"));
        discovery.Printers.Add(TestData.Printer("Printer-A"));
        var registry = Registry(discovery);

        registry.Configure(["Printer A", "Printer-A"]);

        Assert.AreEqual(2, registry.Printers.Count);
        Assert.AreEqual("printer-a", registry.Printers[0].Slug);
        Assert.AreEqual("printer-a-2", registry.Printers[1].Slug);
    }

    [TestMethod]
    public void Configure_Slug_RemovesSymbolsAndKeepsDigits()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("HP 400 (USB)"));
        var registry = Registry(discovery);

        registry.Configure(["HP 400 (USB)"]);

        Assert.AreEqual("hp-400-usb", registry.Printers[0].Slug);
    }

    [TestMethod]
    public void Configure_SymbolOnlyName_FallsBackToPrinterSlug()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("!!!"));
        var registry = Registry(discovery);

        registry.Configure(["!!!"]);

        Assert.AreEqual("printer", registry.Printers[0].Slug);
    }

    [TestMethod]
    public void Configure_LongName_TruncatesSlugAndDisplayName()
    {
        var name = new string('N', 100);
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer(name));
        var registry = Registry(discovery);

        registry.Configure([name]);

        var shared = registry.Printers[0];
        Assert.AreEqual(new string('n', 40), shared.Slug);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(shared.DisplayName) <= 63);
        StringAssert.StartsWith(shared.DisplayName, "Printman - ");
    }

    [TestMethod]
    public void Configure_LongName_TruncatedSlugTrimsTrailingDash()
    {
        var name = new string('a', 39) + " b";
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer(name));
        var registry = Registry(discovery);

        registry.Configure([name]);

        Assert.AreEqual(new string('a', 39), registry.Printers[0].Slug);
    }

    [TestMethod]
    public void Configure_CachesCapabilities()
    {
        var info = TestData.Printer("Cached", supportsColor: true);
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(info);
        var registry = Registry(discovery);

        registry.Configure(["Cached"]);

        Assert.AreSame(info, registry.GetCapabilities(registry.Printers[0]));
    }

    [TestMethod]
    public void Configure_SamePrinterTwice_ProducesSameUuid()
    {
        var name = "Stable Printer";
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer(name));
        var registry = Registry(discovery);

        registry.Configure([name]);
        var first = registry.Printers[0].Uuid;
        registry.Configure([name]);
        var second = registry.Printers[0].Uuid;

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Configure_AcrossInstances_ProducesDeterministicUuid()
    {
        var name = "Stable Printer";
        var firstDiscovery = new FakePrinterDiscoveryService();
        firstDiscovery.Printers.Add(TestData.Printer(name));
        var secondDiscovery = new FakePrinterDiscoveryService();
        secondDiscovery.Printers.Add(TestData.Printer(name));

        var firstRegistry = Registry(firstDiscovery);
        firstRegistry.Configure([name]);
        var secondRegistry = Registry(secondDiscovery);
        secondRegistry.Configure([name]);

        Assert.AreEqual(firstRegistry.Printers[0].Uuid, secondRegistry.Printers[0].Uuid);
    }

    [TestMethod]
    public void Configure_DifferentNames_ProduceDifferentUuids()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("Alpha"));
        discovery.Printers.Add(TestData.Printer("Beta"));
        var registry = Registry(discovery);

        registry.Configure(["Alpha", "Beta"]);

        Assert.AreNotEqual(registry.Printers[0].Uuid, registry.Printers[1].Uuid);
    }

    [TestMethod]
    public void Find_NullOrWhitespace_ReturnsFirstPrinter()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("Alpha"));
        discovery.Printers.Add(TestData.Printer("Beta"));
        var registry = Registry(discovery);
        registry.Configure(["Alpha", "Beta"]);

        Assert.AreEqual("Alpha", registry.Find(null)!.SystemName);
        Assert.AreEqual("Alpha", registry.Find("")!.SystemName);
        Assert.AreEqual("Alpha", registry.Find("   ")!.SystemName);
    }

    [TestMethod]
    public void Find_BySlug_IsCaseInsensitive()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("Alpha"));
        var registry = Registry(discovery);
        registry.Configure(["Alpha"]);

        var shared = registry.Printers[0];
        Assert.AreSame(shared, registry.Find(shared.Slug));
        Assert.AreSame(shared, registry.Find(shared.Slug.ToUpperInvariant()));
    }

    [TestMethod]
    public void Find_UnknownSlug_ReturnsNull()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("Alpha"));
        var registry = Registry(discovery);
        registry.Configure(["Alpha"]);

        Assert.IsNull(registry.Find("does-not-exist"));
    }

    [TestMethod]
    public void Find_WhenNothingShared_ReturnsNull()
    {
        var registry = Registry();

        Assert.IsNull(registry.Find(null));
        Assert.IsNull(registry.Find("any"));
    }

    [TestMethod]
    public void GetCapabilities_NotCached_FetchesFromDiscovery()
    {
        var info = TestData.Printer("HP", supportsColor: true);
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(info);
        var registry = Registry(discovery);

        var capabilities = registry.GetCapabilities(TestData.Shared("HP"));

        Assert.AreSame(info, capabilities);
    }

    [TestMethod]
    public void GetCapabilities_CachedWithinTtl_DoesNotRefresh()
    {
        var info = TestData.Printer("HP", supportsColor: true);
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(info);
        var registry = Registry(discovery);
        var shared = TestData.Shared("HP");
        var first = registry.GetCapabilities(shared);

        discovery.Printers.Clear();
        var second = registry.GetCapabilities(shared);

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetCapabilities_NotFound_ReturnsNullAndCachesNull()
    {
        var registry = Registry();
        var shared = TestData.Shared("Missing");

        Assert.IsNull(registry.GetCapabilities(shared));
        Assert.IsNull(registry.GetCapabilities(shared));
    }

    [TestMethod]
    public void GetCapabilities_DiscoveryThrows_ReturnsNull()
    {
        var registry = new SharedPrinterRegistry(new ThrowingDiscoveryService(), new FakePrintQueueService());

        Assert.IsNull(registry.GetCapabilities(TestData.Shared("Broken")));
    }

    [TestMethod]
    public void GetCapabilities_StaleCache_FallsBackToLastKnown()
    {
        var info = TestData.Printer("HP", supportsColor: true);
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(info);
        var registry = Registry(discovery);
        var shared = TestData.Shared("HP");
        Assert.AreSame(info, registry.GetCapabilities(shared));

        discovery.Printers.Clear();
        ExpireCapabilitiesCache(registry, "HP", info);

        Assert.AreSame(info, registry.GetCapabilities(shared));
    }

    [TestMethod]
    public void GetStatus_ReturnsQueueStatus_AndCachesWithinTtl()
    {
        var status = TestData.Status("HP");
        var queue = new FakePrintQueueService { Status = status };
        var registry = Registry(queue: queue);
        var shared = TestData.Shared("HP");
        var first = registry.GetStatus(shared);

        queue.Status = TestData.Status("HP");
        var second = registry.GetStatus(shared);

        Assert.IsNotNull(first);
        Assert.AreEqual("HP", first!.PrinterName);
        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetStatus_StaleCache_RefreshesFromQueue()
    {
        var status = TestData.Status("HP");
        var queue = new FakePrintQueueService { Status = status };
        var registry = Registry(queue: queue);
        var shared = TestData.Shared("HP");
        var first = registry.GetStatus(shared);

        var replacement = TestData.Status("HP");
        replacement.IsBusy = true;
        queue.Status = replacement;
        ExpireStatusCache(registry, "HP", first);

        var second = registry.GetStatus(shared);

        Assert.AreSame(replacement, second);
        Assert.IsTrue(second!.IsBusy);
    }

    [TestMethod]
    public void GetStatus_QueueThrows_ReturnsNull()
    {
        var registry = new SharedPrinterRegistry(new FakePrinterDiscoveryService(), new ThrowingQueueService());

        Assert.IsNull(registry.GetStatus(TestData.Shared("HP")));
    }

    [TestMethod]
    public void GetStatus_QueueReturnsNull_ReturnsNull()
    {
        var registry = new SharedPrinterRegistry(new FakePrinterDiscoveryService(), new NullStatusQueueService());

        Assert.IsNull(registry.GetStatus(TestData.Shared("HP")));
    }

    private sealed class ThrowingDiscoveryService : IPrinterDiscoveryService
    {
        public IReadOnlyList<PrinterInfo> GetPrinters() => throw new InvalidOperationException("discovery failed");

        public PrinterInfo? GetDefaultPrinter() => null;

        public PrinterInfo? FindPrinter(string query) => null;
    }

    private sealed class ThrowingQueueService : IPrintQueueService
    {
        public IReadOnlyList<PrintJobInfo> GetJobs(string? printerName = null) => throw new NotSupportedException();

        public PrinterStatusInfo GetPrinterStatus(string printerName) => throw new InvalidOperationException("queue failed");

        public bool CancelSpoolerJob(string printerName, int jobId) => throw new NotSupportedException();

        public int PurgeSpoolerQueue(string printerName) => throw new NotSupportedException();

        public void RegisterPipelineJob(string pipelineJobId, string printerName, string documentName, int totalPages, CancellationTokenSource cts) => throw new NotSupportedException();

        public void UpdatePipelineJob(string pipelineJobId, int pagesPrinted, PrintJobStatusCode status, string description) => throw new NotSupportedException();

        public void UnregisterPipelineJob(string pipelineJobId) => throw new NotSupportedException();

        public bool CancelPipelineJob(string pipelineJobId) => throw new NotSupportedException();

        public bool CancelJob(string? printerName, string jobIdOrPipelineId) => throw new NotSupportedException();
    }

    private sealed class NullStatusQueueService : IPrintQueueService
    {
        public IReadOnlyList<PrintJobInfo> GetJobs(string? printerName = null) => throw new NotSupportedException();

        public PrinterStatusInfo GetPrinterStatus(string printerName) => null!;

        public bool CancelSpoolerJob(string printerName, int jobId) => throw new NotSupportedException();

        public int PurgeSpoolerQueue(string printerName) => throw new NotSupportedException();

        public void RegisterPipelineJob(string pipelineJobId, string printerName, string documentName, int totalPages, CancellationTokenSource cts) => throw new NotSupportedException();

        public void UpdatePipelineJob(string pipelineJobId, int pagesPrinted, PrintJobStatusCode status, string description) => throw new NotSupportedException();

        public void UnregisterPipelineJob(string pipelineJobId) => throw new NotSupportedException();

        public bool CancelPipelineJob(string pipelineJobId) => throw new NotSupportedException();

        public bool CancelJob(string? printerName, string jobIdOrPipelineId) => throw new NotSupportedException();
    }
}
