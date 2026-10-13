using System.Net.Http;
using Printman.Core.Models;
using Printman.Services.Cups;
using Printman.Tests.Fakes;

namespace Printman.Tests.Cups;

[TestClass]
public sealed class CupsReadinessTests
{
    private static readonly CupsEndpoint LocalSocket = new("/run/cups/cups.sock", "localhost", 631);
    private static readonly CupsEndpoint LocalTcp = new(null, "localhost", 631);

    [TestMethod]
    public void Needs_OnlyHelpAndVersionSkipTheCheck()
    {
        Assert.IsFalse(CupsReadiness.Needs(CliCommandType.Help));
        Assert.IsFalse(CupsReadiness.Needs(CliCommandType.Version));
        Assert.IsTrue(CupsReadiness.Needs(CliCommandType.ListPrinters));
        Assert.IsTrue(CupsReadiness.Needs(CliCommandType.Server));
        Assert.IsTrue(CupsReadiness.Needs(CliCommandType.Interactive));
    }

    [TestMethod]
    public void DetectInstallCommand_FirstPackageManagerOnPathWins()
    {
        var tools = new FakeExternalTools();
        Assert.IsNull(CupsReadiness.DetectInstallCommand(tools));

        tools.Installed["pacman"] = "/usr/bin/pacman";
        Assert.AreEqual("sudo pacman -S cups cups-filters", CupsReadiness.DetectInstallCommand(tools));

        tools.Installed["apt-get"] = "/usr/bin/apt-get";
        Assert.AreEqual("sudo apt install cups cups-filters", CupsReadiness.DetectInstallCommand(tools));
    }

    [TestMethod]
    public void Explain_Linux_NotInstalled_ShowsInstallAndEnable()
    {
        var lines = CupsReadiness.Explain("Cannot reach x", LocalSocket, macOS: false, cupsdInstalled: false, "sudo apt install cups cups-filters");

        Assert.AreEqual("[ERROR] Cannot reach x", lines[0]);
        CollectionAssert.Contains(lines.ToList(), "    sudo apt install cups cups-filters");
        CollectionAssert.Contains(lines.ToList(), "    sudo systemctl enable --now cups");
        Assert.IsFalse(lines.Any(l => l.Contains("launchctl")));
        Assert.IsFalse(lines.Any(l => l.Contains("CUPS_SERVER")));
    }

    [TestMethod]
    public void Explain_Linux_UnknownPackageManager_StillTellsWhatToInstall()
    {
        var lines = CupsReadiness.Explain("down", LocalTcp, macOS: false, cupsdInstalled: false, null);

        Assert.IsTrue(lines.Any(l => l.Contains("'cups' and 'cups-filters'")));
    }

    [TestMethod]
    public void Explain_Linux_InstalledButStopped_OnlyAsksToStart()
    {
        var lines = CupsReadiness.Explain("down", LocalSocket, macOS: false, cupsdInstalled: true, "sudo apt install cups cups-filters");

        Assert.IsTrue(lines.Any(l => l.Contains("installed but the service is not running")));
        CollectionAssert.Contains(lines.ToList(), "    sudo systemctl enable --now cups");
        Assert.IsFalse(lines.Any(l => l.Contains("apt install")));
    }

    [TestMethod]
    public void Explain_MacOS_NeverSuggestsInstalling()
    {
        var lines = CupsReadiness.Explain("down", LocalTcp, macOS: true, cupsdInstalled: true, "brew install cups");

        Assert.IsTrue(lines.Any(l => l.Contains("CUPS is part of macOS")));
        Assert.IsTrue(lines.Any(l => l.Contains("launchctl load -w")));
        Assert.IsFalse(lines.Any(l => l.Contains("install", StringComparison.OrdinalIgnoreCase) && l.Contains("brew")));
        Assert.IsFalse(lines.Any(l => l.Contains("systemctl")));
    }

    [TestMethod]
    public void Explain_RemoteCupsServer_MentionsTheVariable()
    {
        var lines = CupsReadiness.Explain("down", new CupsEndpoint(null, "printhost", 8631), macOS: false, cupsdInstalled: true, null);

        Assert.IsTrue(lines.Any(l => l.Contains("CUPS_SERVER points at printhost:8631")));
    }

    [TestMethod]
    public async Task EnsureAsync_Ready_ReturnsNull_WritesNothing()
    {
        var server = new FakeCupsServer();
        using var client = server.CreateClient();
        var output = new StringWriter();

        var result = await CupsReadiness.EnsureAsync(client, new FakeExternalTools(), () => null, output, interactive: true);

        Assert.IsNull(result);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public async Task EnsureAsync_NotReady_NonInteractive_ExplainsAndExits1()
    {
        var server = new FakeCupsServer { Fail = () => new HttpRequestException("Connection refused") };
        using var client = server.CreateClient(viaSocket: false);
        var output = new StringWriter();

        var result = await CupsReadiness.EnsureAsync(client, new FakeExternalTools(), () => throw new InvalidOperationException("must not prompt"), output, interactive: false);

        Assert.AreEqual(1, result);
        StringAssert.Contains(output.ToString(), "Cannot reach the CUPS print service");
        StringAssert.Contains(output.ToString(), "CUPS is not answering");
        Assert.IsFalse(output.ToString().Contains("Press Enter"));
    }

    [TestMethod]
    public async Task EnsureAsync_Interactive_RetriesUntilReady()
    {
        var server = new FakeCupsServer { Fail = () => new HttpRequestException("Connection refused") };
        using var client = server.CreateClient();
        var output = new StringWriter();
        int prompts = 0;

        var result = await CupsReadiness.EnsureAsync(client, new FakeExternalTools(), () =>
        {
            prompts++;
            if (prompts == 2) server.Fail = null; // "the user started cupsd"
            return "";
        }, output, interactive: true);

        Assert.IsNull(result);
        Assert.AreEqual(2, prompts);
        StringAssert.Contains(output.ToString(), "Press Enter to retry");
    }

    [TestMethod]
    [DataRow("q")]
    [DataRow("Q")]
    [DataRow(null)]
    public async Task EnsureAsync_Interactive_QuitOrEof_Exits1(string? answer)
    {
        var server = new FakeCupsServer { Fail = () => new HttpRequestException("Connection refused") };
        using var client = server.CreateClient();

        var result = await CupsReadiness.EnsureAsync(client, new FakeExternalTools(), () => answer, new StringWriter(), interactive: true);

        Assert.AreEqual(1, result);
    }
}
