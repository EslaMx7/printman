using Printman.CLI;

namespace Printman.Tests.Cli;

/// <summary>Unknown flags of every shape are rejected with the help hint, for serve / share and for printing.</summary>
[TestClass]
public sealed class CommandLineParserUnknownFlagTests
{
    [TestMethod]
    [DataRow("-x")]
    [DataRow("-xy")]
    [DataRow("--x")]
    [DataRow("-port-x")]
    [DataRow("--ports")]
    [DataRow("--pinned")]
    [DataRow("--no-auth-please")]
    [DataRow("--share-everything")]
    [DataRow("--ipp-ports")]
    [DataRow("--max-upload")]
    [DataRow("--cache-limit")]
    [DataRow("--output-directory")]
    [DataRow("--allow-anonymous-users")]
    [DataRow("--select-all")]
    [DataRow("--web-ui")]
    public void Serve_UnknownFlag_IsIgnored(string flag)
    {
        // serve tolerates unknown options (share and print reject them)
        var args = CommandLineParser.Parse(["serve", flag]);

        Assert.AreEqual(Printman.Core.Models.CliCommandType.Server, args.Command);
        Assert.AreEqual(5000, args.ServerPort);
        Assert.IsTrue(args.EnableWebUi);
    }

    [TestMethod]
    [DataRow("--selects")]
    [DataRow("-alls")]
    [DataRow("--webs")]
    [DataRow("--no-mdns-now")]
    [DataRow("--ipp-allow-any")]
    public void Share_UnknownFlag_IsRejected(string flag)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["share", flag]));

        StringAssert.Contains(ex.Message, flag);
    }

    [TestMethod]
    [DataRow("-x")]
    [DataRow("-pr")]
    [DataRow("-page")]
    [DataRow("-sizes")]
    [DataRow("-copy")]
    [DataRow("-orient")]
    [DataRow("-duplexed")]
    [DataRow("-colour")]
    [DataRow("-dpis")]
    [DataRow("-outputs")]
    [DataRow("-fits")]
    [DataRow("-nofits")]
    [DataRow("--printers")]
    [DataRow("--page")]
    public void Print_UnknownFlag_IsRejected(string flag)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["doc.pdf", flag]));

        StringAssert.Contains(ex.Message, flag);
    }
}
