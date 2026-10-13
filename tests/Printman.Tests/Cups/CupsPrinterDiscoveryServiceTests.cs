using System.Net.Http;
using Printman.Core.Models;
using Printman.Services.Cups;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Cups;

[TestClass]
public sealed class CupsPrinterDiscoveryServiceTests
{
    /// <summary>Scripted cupsd: CUPS-Get-Printers lists the given queues, CUPS-Get-Default names one.</summary>
    private static FakeCupsServer Server(string? serverDefault, params Action<IppAttributeGroup>[] printers) => new()
    {
        Respond = r => r.Message.Code switch
        {
            CupsClient.CupsGetPrinters when printers.Length == 0 => FakeCupsServer.Error(IppStatus.NotFound, "No destinations added."),
            CupsClient.CupsGetPrinters => FakeCupsServer.Ok(m =>
            {
                foreach (var p in printers) p(m.AddGroup(IppTag.PrinterAttributes));
            }),
            CupsClient.CupsGetDefault when serverDefault == null => FakeCupsServer.Error(IppStatus.NotFound, "No default destination."),
            CupsClient.CupsGetDefault => FakeCupsServer.Ok(m => m.AddGroup(IppTag.PrinterAttributes).AddName("printer-name", serverDefault)),
            _ => FakeCupsServer.Error(IppStatus.OperationNotSupported)
        }
    };

    private static Action<IppAttributeGroup> Queue(string name, Action<IppAttributeGroup>? extra = null) => g =>
    {
        g.AddName("printer-name", name);
        extra?.Invoke(g);
    };

    private static CupsPrinterDiscoveryService Service(FakeCupsServer server, Func<string, string?>? env = null, string? lpoptions = null, TextWriter? diagnostics = null) =>
        new(server.CreateClient(), env ?? (_ => null), lpoptions, diagnostics ?? TextWriter.Null);

    [TestMethod]
    public void GetPrinters_ParsesCapabilities_StatusAndMedia()
    {
        var server = Server("Laser",
            Queue("Laser", g =>
            {
                g.AddText("printer-make-and-model", "HP LaserJet");
                g.AddUri("device-uri", "usb://HP/LaserJet");
                g.AddEnum("printer-state", IppPrinterState.Idle);
                g.AddBoolean("printer-is-accepting-jobs", true);
                g.AddBoolean("color-supported", false);
                g.AddKeyword("sides-supported", "one-sided", "two-sided-long-edge");
                g.AddKeyword("media-supported", "iso_a4_210x297mm", "na_letter_8.5x11in", "custom_148.52x209.9mm_148.52x209.9mm", "iso_a5_148x210mm", "custom_min_0.5x0.5in", "bogus");
                g.AddResolution("printer-resolution-supported", new IppResolution(300, 300), new IppResolution(600, 600), new IppResolution(300, 300));
            }),
            Queue("Inkjet", g =>
            {
                g.AddEnum("printer-state", IppPrinterState.Processing);
                g.AddBoolean("color-supported", true);
                g.AddKeyword("sides-supported", "one-sided");
            }),
            Queue("Paused", g => g.AddEnum("printer-state", IppPrinterState.Stopped)),
            Queue("Rejecting", g => g.AddBoolean("printer-is-accepting-jobs", false)),
            g => g.AddText("printer-info", "nameless entry is skipped"));

        var printers = Service(server).GetPrinters();

        Assert.AreEqual(4, printers.Count);
        CollectionAssert.AreEqual(new[] { "Inkjet", "Laser", "Paused", "Rejecting" }, printers.Select(p => p.Name).ToArray(), "sorted by name");

        var laser = printers.Single(p => p.Name == "Laser");
        Assert.IsTrue(laser.IsDefault);
        Assert.AreEqual("Ready", laser.Status);
        Assert.AreEqual("HP LaserJet", laser.DriverName);
        Assert.AreEqual("usb://HP/LaserJet", laser.PortName);
        Assert.IsFalse(laser.SupportsColor);
        Assert.IsTrue(laser.CanDuplex);
        CollectionAssert.AreEqual(new[] { "300x300 DPI", "600x600 DPI" }, laser.SupportedResolutions.ToArray());
        // custom A5 maps to "A5" first, so the real iso_a5 keeps its keyword as a unique name; ranges and junk are dropped
        CollectionAssert.AreEqual(new[] { "A4", "Letter", "A5", "iso_a5_148x210mm" }, laser.SupportedPaperSizes.Select(p => p.Name).ToArray());
        Assert.AreEqual("iso_a4_210x297mm", laser.SupportedPaperSizes[0].Keyword);

        var inkjet = printers.Single(p => p.Name == "Inkjet");
        Assert.AreEqual("Printing", inkjet.Status);
        Assert.IsTrue(inkjet.SupportsColor);
        Assert.IsFalse(inkjet.CanDuplex);
        Assert.IsFalse(inkjet.IsDefault);
        Assert.AreEqual(0, inkjet.SupportedPaperSizes.Count);

        Assert.AreEqual("Paused", printers.Single(p => p.Name == "Paused").Status);
        Assert.AreEqual("Not accepting jobs", printers.Single(p => p.Name == "Rejecting").Status);
    }

    [TestMethod]
    public void GetPrinters_NoQueues_ReturnsEmptyWithoutWarning()
    {
        var diagnostics = new StringWriter();

        Assert.AreEqual(0, Service(Server(null), diagnostics: diagnostics).GetPrinters().Count);
        Assert.AreEqual("", diagnostics.ToString());
    }

    [TestMethod]
    public void GetPrinters_CupsUnreachable_ReturnsEmpty_WarnsOnce()
    {
        var server = new FakeCupsServer { Fail = () => new HttpRequestException("Connection refused") };
        var diagnostics = new StringWriter();
        var service = Service(server, diagnostics: diagnostics);

        Assert.AreEqual(0, service.GetPrinters().Count);
        Assert.AreEqual(0, service.GetPrinters().Count);

        var warnings = diagnostics.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(1, warnings.Length);
        StringAssert.Contains(warnings[0], "Warning: Cannot reach the CUPS print service");
    }

    [TestMethod]
    public void GetDefaultPrinter_ServerDefault_ThenFirstPrinter()
    {
        Assert.AreEqual("Laser", Service(Server("Laser", Queue("Inkjet"), Queue("Laser"))).GetDefaultPrinter()!.Name);
        Assert.AreEqual("Inkjet", Service(Server(null, Queue("Inkjet"), Queue("Laser"))).GetDefaultPrinter()!.Name, "no default configured: first printer");
        Assert.IsNull(Service(Server(null)).GetDefaultPrinter());
    }

    [TestMethod]
    public void GetDefaultPrinter_EnvironmentVariablesWin_LikeLp()
    {
        var server = Server("Laser", Queue("Inkjet"), Queue("Laser"), Queue("Photo"));

        Assert.AreEqual("Photo", Service(server, env: v => v == "LPDEST" ? "Photo/duplex" : null).GetDefaultPrinter()!.Name, "instance suffix is stripped");
        Assert.AreEqual("Inkjet", Service(server, env: v => v == "PRINTER" ? "Inkjet" : null).GetDefaultPrinter()!.Name);
        Assert.AreEqual("Laser", Service(server, env: v => v == "PRINTER" ? "lp" : " ").GetDefaultPrinter()!.Name, "'lp' and blanks are ignored");
    }

    [TestMethod]
    public void GetDefaultPrinter_LpoptionsFile_BeatsServerDefault()
    {
        using var temp = new TempDirectory();
        var lpoptions = temp.Combine("lpoptions");
        File.WriteAllLines(lpoptions, ["# comment", "Dest Laser sides=two-sided-long-edge", "Default Photo/glossy media=a4"]);
        var server = Server("Laser", Queue("Laser"), Queue("Photo"));

        Assert.AreEqual("Photo", Service(server, lpoptions: lpoptions).GetDefaultPrinter()!.Name);

        File.WriteAllLines(lpoptions, ["Default ", "Dest Laser"]);
        Assert.AreEqual("Laser", Service(server, lpoptions: lpoptions).GetDefaultPrinter()!.Name, "empty Default line falls through to the server");

        Assert.AreEqual("Laser", Service(server, lpoptions: temp.Combine("missing")).GetDefaultPrinter()!.Name);
    }

    [TestMethod]
    public void GetDefaultPrinter_UnreadableLpoptions_FallsThrough()
    {
        using var temp = new TempDirectory();
        var lpoptions = temp.Combine("lpoptions");
        File.WriteAllText(lpoptions, "Default Photo");
        using var locked = new FileStream(lpoptions, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var server = Server("Laser", Queue("Laser"), Queue("Photo"));

        Assert.AreEqual("Laser", Service(server, lpoptions: lpoptions).GetDefaultPrinter()!.Name);
    }

    [TestMethod]
    public void GetPrinters_ServerDefaultWithoutName_MeansNoDefault()
    {
        var server = new FakeCupsServer
        {
            Respond = r => r.Message.Code == CupsClient.CupsGetPrinters
                ? FakeCupsServer.Ok(m => { m.AddGroup(IppTag.PrinterAttributes).AddName("printer-name", "Laser"); m.AddGroup(IppTag.PrinterAttributes).AddName("printer-name", ""); })
                : FakeCupsServer.Ok(m => m.AddGroup(IppTag.PrinterAttributes).AddText("printer-info", "no name"))
        };

        var printers = Service(server).GetPrinters();

        Assert.AreEqual("Laser", printers.Single().Name);
        Assert.IsFalse(printers[0].IsDefault);
    }

    [TestMethod]
    public void FindPrinter_BlankUsesDefault_OtherwiseFuzzy()
    {
        var service = Service(Server("Laser", Queue("HP_LaserJet_Pro"), Queue("Laser")));

        Assert.AreEqual("Laser", service.FindPrinter(" ")!.Name);
        Assert.AreEqual("HP_LaserJet_Pro", service.FindPrinter("hp laserjet")!.Name);
        Assert.IsNull(service.FindPrinter("Epson"));
    }
}
