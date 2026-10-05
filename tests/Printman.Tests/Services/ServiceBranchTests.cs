using Printman.Services;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Services;

/// <summary>Branches not exercised by the main service suites.</summary>
[TestClass]
public sealed class ServiceBranchTests
{
    [TestMethod]
    public void FileCacheService_DefaultConstructor_UsesExecutableCacheFolder()
    {
        var cache = new FileCacheService();

        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "cache"), cache.CacheDirectory);
        Assert.IsTrue(Directory.Exists(cache.CacheDirectory));
    }

    [TestMethod]
    public void SharedPrinterRegistry_SlugKeepsLettersDigitsAndSingleSeparators()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("HP LaserJet 1102 (Office)"));
        var registry = new SharedPrinterRegistry(discovery, new FakePrintQueueService());

        var unresolved = registry.Configure(["HP LaserJet 1102 (Office)"]);

        Assert.AreEqual(0, unresolved.Count);
        Assert.AreEqual("hp-laserjet-1102-office", registry.Printers[0].Slug);
    }

    [TestMethod]
    public void SharedPrinterRegistry_LongName_TruncatesSlugToFortyCharacters()
    {
        var longName = new string('a', 50);
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer(longName));
        var registry = new SharedPrinterRegistry(discovery, new FakePrintQueueService());

        registry.Configure([longName]);

        Assert.AreEqual(new string('a', 40), registry.Printers[0].Slug);
    }

    [TestMethod]
    public void SharedPrinterRegistry_SlugWithNoUsableCharacters_FallsBackToPrinter()
    {
        var discovery = new FakePrinterDiscoveryService();
        discovery.Printers.Add(TestData.Printer("!!!"));
        var registry = new SharedPrinterRegistry(discovery, new FakePrintQueueService());

        registry.Configure(["!!!"]);

        Assert.AreEqual("printer", registry.Printers[0].Slug);
    }
}
