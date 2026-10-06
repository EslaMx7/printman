using System.Text;
using Printman.Core.Models;
using Printman.Services.Ipp;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Ipp;

[TestClass]
public sealed class IppDnsSdServiceFactoryTests
{
    private static IppDocumentFormats Formats(params string[] extensions) =>
        new([new FakeDocumentRenderer(extensions)]);

    private static IppDnsSdServiceFactory Factory(
        FakeSharedPrinterRegistry registry,
        IppDocumentFormats? formats = null,
        IppServerSettings? settings = null) =>
        new(registry, formats ?? Formats(".pdf"), settings ?? new IppServerSettings());

    private static Dictionary<string, string> Txt(DnsSdService service) =>
        service.Txt.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    [TestMethod]
    public void Create_EmptyList_ReturnsEmpty()
    {
        var services = Factory(new FakeSharedPrinterRegistry()).Create([]);

        Assert.AreEqual(0, services.Count);
    }

    [TestMethod]
    public void Create_BuildsCoreRecordAndTxtKeys()
    {
        var printer = IppTestMessages.Printer("hp", "HP LaserJet");
        var registry = new FakeSharedPrinterRegistry();
        registry.Capabilities[printer.SystemName] =
            TestData.Printer("HP LaserJet", supportsColor: true, canDuplex: true);
        var settings = new IppServerSettings { IppPort = 631, WebUiEnabled = false, MdnsHostName = null };

        var service = Factory(registry, Formats(".pdf"), settings).Create([printer])[0];
        var txt = Txt(service);

        Assert.AreEqual(printer.DisplayName, service.InstanceName);
        Assert.AreEqual("_ipp._tcp", service.ServiceType);
        Assert.AreEqual(631, service.Port);
        CollectionAssert.AreEqual(new[] { "_print" }, service.Subtypes.ToArray());
        Assert.AreEqual("1", txt["txtvers"]);
        Assert.AreEqual("1", txt["qtotal"]);
        Assert.AreEqual(printer.ResourcePath, txt["rp"]);
        Assert.AreEqual($"Printman {printer.SystemName}", txt["ty"]);
        Assert.AreEqual($"(Printman {printer.SystemName})", txt["product"]);
        Assert.AreEqual(Environment.MachineName, txt["note"]);
        Assert.AreEqual(printer.Uuid.ToString(), txt["UUID"]);
        Assert.AreEqual("application/pdf,application/octet-stream", txt["pdl"]);
        Assert.AreEqual("T", txt["Color"]);
        Assert.AreEqual("T", txt["Duplex"]);
        Assert.AreEqual("T", txt["Copies"]);
        Assert.AreEqual("F", txt["Collate"]);
        Assert.AreEqual("F", txt["Staple"]);
        Assert.AreEqual("F", txt["Punch"]);
        Assert.AreEqual("F", txt["Bind"]);
        Assert.AreEqual("F", txt["Sort"]);
        Assert.AreEqual("F", txt["Scan"]);
        Assert.AreEqual("F", txt["Fax"]);
        Assert.AreEqual("legal-A4", txt["PaperMax"]);
        Assert.AreEqual("document,photo", txt["kind"]);
        Assert.AreEqual("50", txt["priority"]);
        Assert.AreEqual("none", txt["air"]);
        Assert.IsFalse(txt.ContainsKey("adminurl"));
        Assert.IsFalse(txt.ContainsKey("URF"));
    }

    [TestMethod]
    public void Create_NullCapabilities_ReportsColorAndDuplexFalseWithFallbackMedia()
    {
        var printer = IppTestMessages.Printer();

        var txt = Txt(Factory(new FakeSharedPrinterRegistry()).Create([printer])[0]);

        Assert.AreEqual("F", txt["Color"]);
        Assert.AreEqual("F", txt["Duplex"]);
        Assert.AreEqual("legal-A4", txt["PaperMax"]);
        Assert.IsFalse(txt.ContainsKey("URF"));
    }

    [TestMethod]
    public void Create_LargeFormatPaper_ReportsTabloidA3()
    {
        var printer = IppTestMessages.Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Capabilities[printer.SystemName] = TestData.Printer(
            paperSizes: [new PaperSizeOption("A3", 8, 1169, 1654)]);

        var txt = Txt(Factory(registry).Create([printer])[0]);

        Assert.AreEqual("tabloid-A3", txt["PaperMax"]);
    }

    [TestMethod]
    public void Create_UrfSupported_AddsUniversalSubtypeAndUrfKey()
    {
        var printer = IppTestMessages.Printer();
        var registry = new FakeSharedPrinterRegistry();
        registry.Capabilities[printer.SystemName] = TestData.Printer(canDuplex: true);

        var service = Factory(registry, Formats(".pdf", ".urf")).Create([printer])[0];
        var txt = Txt(service);

        CollectionAssert.AreEqual(new[] { "_universal", "_print" }, service.Subtypes.ToArray());
        Assert.AreEqual(
            string.Join(',', IppPrinterAttributeBuilder.GetUrfSupported(registry.Capabilities[printer.SystemName])),
            txt["URF"]);
        Assert.IsTrue(txt["URF"].EndsWith(",DM1", StringComparison.Ordinal));
        Assert.IsTrue(txt["pdl"].Contains(IppDocumentFormats.Urf, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(true, "printman.local", true)]
    [DataRow(true, null, false)]
    [DataRow(true, "", false)]
    [DataRow(false, "printman.local", false)]
    public void Create_AdminUrl_DependsOnWebUiAndMdnsHost(bool webUi, string? host, bool expected)
    {
        var printer = IppTestMessages.Printer();
        var settings = new IppServerSettings { WebUiEnabled = webUi, MdnsHostName = host, WebPort = 8080 };

        var txt = Txt(Factory(new FakeSharedPrinterRegistry(), settings: settings).Create([printer])[0]);

        Assert.AreEqual(expected, txt.ContainsKey("adminurl"));
        if (expected)
        {
            Assert.AreEqual("http://printman.local:8080/", txt["adminurl"]);
        }
    }

    [TestMethod]
    public void Create_LongNames_AreTruncatedToTwoHundredUtf8Bytes()
    {
        var printer = IppTestMessages.Printer("long", new string('A', 250));

        var txt = Txt(Factory(new FakeSharedPrinterRegistry()).Create([printer])[0]);

        Assert.AreEqual(200, Encoding.UTF8.GetByteCount(txt["ty"]));
        Assert.AreEqual(200, Encoding.UTF8.GetByteCount(txt["product"]));
    }

    [TestMethod]
    public void Create_MultiplePrinters_ReturnsOneServiceEach()
    {
        var first = IppTestMessages.Printer("a", "Alpha");
        var second = IppTestMessages.Printer("b", "Beta");

        var services = Factory(new FakeSharedPrinterRegistry()).Create([first, second]);

        Assert.AreEqual(2, services.Count);
        Assert.AreEqual(first.DisplayName, services[0].InstanceName);
        Assert.AreEqual(second.DisplayName, services[1].InstanceName);
    }

    [TestMethod]
    public void Create_UsesConfiguredIppPort()
    {
        var settings = new IppServerSettings { IppPort = 8631 };

        var service = Factory(new FakeSharedPrinterRegistry(), settings: settings)
            .Create([IppTestMessages.Printer()])[0];

        Assert.AreEqual(8631, service.Port);
    }
}
