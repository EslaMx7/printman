using Printman.CLI;
using Printman.Core.Models;

namespace Printman.Tests.Cli;

[TestClass]
public sealed class CommandLineParserShareTests
{
    [TestMethod]
    public void Share_WithoutArguments_EnablesSharingAndDisablesWebUi()
    {
        var args = CommandLineParser.Parse(["share"]);

        Assert.AreEqual(CliCommandType.Server, args.Command);
        Assert.IsTrue(args.SharePrinters);
        Assert.IsFalse(args.EnableWebUi);
        Assert.AreEqual(0, args.SharedPrinterNames.Count);
        Assert.AreEqual(631, args.IppPort);
        Assert.IsTrue(args.EnableMdns);
    }

    [TestMethod]
    public void Share_PositionalPrinterNames_AreCollectedInOrder()
    {
        var args = CommandLineParser.Parse(["share", "HP Laser", "Canon"]);

        CollectionAssert.AreEqual(new[] { "HP Laser", "Canon" }, args.SharedPrinterNames);
        Assert.IsTrue(args.SharePrinters);
    }

    [TestMethod]
    public void Share_SelectFlag_RequestsTheChecklist()
    {
        var args = CommandLineParser.Parse(["share", "--select"]);

        Assert.IsTrue(args.SelectSharedPrinters);
        Assert.IsTrue(args.SharePrinters);
    }

    [TestMethod]
    public void ShareSelectCommand_IsAnAliasForSelect()
    {
        var args = CommandLineParser.Parse(["share-select"]);

        Assert.AreEqual(CliCommandType.Server, args.Command);
        Assert.IsTrue(args.SelectSharedPrinters);
        Assert.IsFalse(args.EnableWebUi);
    }

    [TestMethod]
    public void Share_AllFlag_RequestsEveryPrinter()
    {
        var args = CommandLineParser.Parse(["share", "--all"]);

        Assert.IsTrue(args.ShareAllPrinters);
    }

    [TestMethod]
    public void Share_WebFlag_KeepsTheWebUiEnabled()
    {
        var args = CommandLineParser.Parse(["share", "--web"]);

        Assert.IsTrue(args.EnableWebUi);
        Assert.IsTrue(args.SharePrinters);
    }

    [TestMethod]
    public void Share_NetworkOptions_AreParsed()
    {
        var args = CommandLineParser.Parse(
            ["share", "--ipp-port", "8631", "--no-mdns", "--ipp-allow-any-source", "--output-dir", "out"]);

        Assert.AreEqual(8631, args.IppPort);
        Assert.IsFalse(args.EnableMdns);
        Assert.IsTrue(args.IppAllowAnySource);
        Assert.AreEqual("out", args.ServerOutputDirectory);
    }

    [TestMethod]
    public void Serve_ShareFlag_KeepsWebUiAndSelectsPrinter()
    {
        var args = CommandLineParser.Parse(["serve", "--share", "HP Laser"]);

        Assert.AreEqual(CliCommandType.Server, args.Command);
        Assert.IsTrue(args.SharePrinters);
        Assert.IsTrue(args.EnableWebUi);
        CollectionAssert.AreEqual(new[] { "HP Laser" }, args.SharedPrinterNames);
    }

    [TestMethod]
    public void ToServerOptions_MapsShareSettings()
    {
        var options = CommandLineParser.Parse(["share", "HP Laser", "--ipp-port", "8631"]).ToServerOptions();

        Assert.IsTrue(options.Share.Enabled);
        Assert.AreEqual(8631, options.Share.IppPort);
        Assert.IsFalse(options.EnableWebUi);
        CollectionAssert.AreEqual(new[] { "HP Laser" }, options.Share.Printers.ToArray());
    }

    [TestMethod]
    public void Share_InvalidIppPort_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["share", "--ipp-port", "70000"]));
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["share", "--ipp-port", "abc"]));
    }

    [TestMethod]
    public void Share_UnknownOption_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["share", "--bogus"]));
    }
}
