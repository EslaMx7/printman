using Printman.CLI;
using Printman.Core.Models;

namespace Printman.Tests.Cli;

/// <summary>Every spelling of every flag is accepted (single dash, double dash, short form, key=value).</summary>
[TestClass]
public sealed class CommandLineParserFlagAliasTests
{
    [TestMethod]
    [DataRow("--port")]
    [DataRow("-port")]
    [DataRow("-p")]
    public void Serve_PortAliases(string flag)
    {
        Assert.AreEqual(8080, CommandLineParser.Parse(["serve", flag, "8080"]).ServerPort);
        Assert.AreEqual(8081, CommandLineParser.Parse(["serve", $"{flag}=8081"]).ServerPort);
    }

    [TestMethod]
    [DataRow("--ip")]
    [DataRow("-ip")]
    [DataRow("--bind")]
    [DataRow("-bind")]
    public void Serve_BindAliases(string flag)
    {
        Assert.AreEqual("192.168.1.9", CommandLineParser.Parse(["serve", flag, "192.168.1.9"]).BindAddress);
    }

    [TestMethod]
    [DataRow("--pin")]
    [DataRow("-pin")]
    public void Serve_PinAliases(string flag)
    {
        Assert.AreEqual("1234", CommandLineParser.Parse(["serve", flag, "1234"]).ServerPin);
    }

    [TestMethod]
    [DataRow("--no-auth")]
    [DataRow("-no-auth")]
    [DataRow("--allow-anonymous")]
    [DataRow("-allow-anonymous")]
    public void Serve_NoAuthAliases(string flag)
    {
        Assert.IsFalse(CommandLineParser.Parse(["serve", flag]).RequireAuth);
    }

    [TestMethod]
    [DataRow("--max-upload-mb")]
    [DataRow("-max-upload-mb")]
    public void Serve_MaxUploadAliases(string flag)
    {
        Assert.AreEqual(12, CommandLineParser.Parse(["serve", flag, "12"]).MaxUploadMb);
    }

    [TestMethod]
    [DataRow("--cache-limit-mb")]
    [DataRow("-cache-limit-mb")]
    public void Serve_CacheLimitAliases(string flag)
    {
        Assert.AreEqual(123, CommandLineParser.Parse(["serve", flag, "123"]).CacheLimitMb);
    }

    [TestMethod]
    [DataRow("--share")]
    [DataRow("-share")]
    public void Serve_ShareAliases(string flag)
    {
        var args = CommandLineParser.Parse(["serve", flag, "HP"]);

        Assert.IsTrue(args.SharePrinters);
        CollectionAssert.AreEqual(new[] { "HP" }, args.SharedPrinterNames);
    }

    [TestMethod]
    [DataRow("--share-select")]
    [DataRow("-share-select")]
    public void Serve_ShareSelectAliases(string flag)
    {
        var args = CommandLineParser.Parse(["serve", flag]);

        Assert.IsTrue(args.SharePrinters);
        Assert.IsTrue(args.SelectSharedPrinters);
    }

    [TestMethod]
    [DataRow("--select")]
    [DataRow("-select")]
    public void Share_SelectAliases(string flag)
    {
        Assert.IsTrue(CommandLineParser.Parse(["share", flag]).SelectSharedPrinters);
    }

    [TestMethod]
    [DataRow("--all")]
    [DataRow("-all")]
    public void Share_AllAliases(string flag)
    {
        Assert.IsTrue(CommandLineParser.Parse(["share", flag]).ShareAllPrinters);
    }

    [TestMethod]
    [DataRow("--web")]
    [DataRow("-web")]
    [DataRow("--ui")]
    [DataRow("-ui")]
    public void Share_WebAliases(string flag)
    {
        Assert.IsTrue(CommandLineParser.Parse(["share", flag]).EnableWebUi);
    }

    [TestMethod]
    [DataRow("--ipp-port")]
    [DataRow("-ipp-port")]
    public void Share_IppPortAliases(string flag)
    {
        Assert.AreEqual(9631, CommandLineParser.Parse(["share", flag, "9631"]).IppPort);
        Assert.AreEqual(9632, CommandLineParser.Parse(["serve", $"{flag}=9632"]).IppPort);
    }

    [TestMethod]
    [DataRow("--no-mdns")]
    [DataRow("-no-mdns")]
    public void Share_NoMdnsAliases(string flag)
    {
        Assert.IsFalse(CommandLineParser.Parse(["share", flag]).EnableMdns);
    }

    [TestMethod]
    [DataRow("--ipp-allow-any-source")]
    [DataRow("-ipp-allow-any-source")]
    public void Share_AllowAnySourceAliases(string flag)
    {
        Assert.IsTrue(CommandLineParser.Parse(["share", flag]).IppAllowAnySource);
    }

    [TestMethod]
    [DataRow("--output-dir")]
    [DataRow("-output-dir")]
    public void Share_OutputDirAliases(string flag)
    {
        Assert.AreEqual("out", CommandLineParser.Parse(["share", flag, "out"]).ServerOutputDirectory);
        Assert.AreEqual("out2", CommandLineParser.Parse(["serve", $"{flag}=out2"]).ServerOutputDirectory);
    }

    [TestMethod]
    [DataRow("-printer")]
    [DataRow("--printer")]
    [DataRow("-p")]
    public void Print_PrinterAliases(string flag)
    {
        Assert.AreEqual("HP", CommandLineParser.Parse(["doc.pdf", flag, "HP"]).TargetPrinterName);
    }

    [TestMethod]
    [DataRow("-pages")]
    [DataRow("--pages")]
    public void Print_PagesAliases(string flag)
    {
        Assert.AreEqual("2-3", CommandLineParser.Parse(["doc.pdf", flag, "2-3"]).PageRange.RawExpression);
    }

    [TestMethod]
    [DataRow("-size")]
    [DataRow("--size")]
    [DataRow("-s")]
    public void Print_SizeAliases(string flag)
    {
        Assert.AreEqual("A3", CommandLineParser.Parse(["doc.pdf", flag, "A3"]).PaperSizeName);
    }

    [TestMethod]
    [DataRow("-copies")]
    [DataRow("--copies")]
    [DataRow("-c")]
    public void Print_CopiesAliases(string flag)
    {
        Assert.AreEqual(5, CommandLineParser.Parse(["doc.pdf", flag, "5"]).Copies);
    }

    [TestMethod]
    [DataRow("-orientation")]
    [DataRow("--orientation")]
    [DataRow("-o")]
    public void Print_OrientationAliases(string flag)
    {
        Assert.AreEqual(PrintOrientation.Landscape, CommandLineParser.Parse(["doc.pdf", flag, "landscape"]).Orientation);
    }

    [TestMethod]
    [DataRow("-duplex")]
    [DataRow("--duplex")]
    [DataRow("-d")]
    public void Print_DuplexAliases(string flag)
    {
        Assert.AreEqual(PrintDuplex.Horizontal, CommandLineParser.Parse(["doc.pdf", flag, "horizontal"]).Duplex);
    }

    [TestMethod]
    [DataRow("-color")]
    [DataRow("--color")]
    public void Print_ColorAliases(string flag)
    {
        Assert.AreEqual(PrintColorMode.Color, CommandLineParser.Parse(["doc.pdf", flag, "color"]).ColorMode);
    }

    [TestMethod]
    [DataRow("-dpi")]
    [DataRow("--dpi")]
    public void Print_DpiAliases(string flag)
    {
        Assert.AreEqual(150, CommandLineParser.Parse(["doc.pdf", flag, "150"]).Dpi);
    }

    [TestMethod]
    [DataRow("-output")]
    [DataRow("--output")]
    [DataRow("-out")]
    public void Print_OutputAliases(string flag)
    {
        Assert.AreEqual("o.xps", CommandLineParser.Parse(["doc.pdf", flag, "o.xps"]).OutputFilePath);
    }

    [TestMethod]
    [DataRow("-fit", true)]
    [DataRow("--fit", true)]
    [DataRow("-nofit", false)]
    [DataRow("--nofit", false)]
    public void Print_FitAliases(string flag, bool fit)
    {
        Assert.AreEqual(fit, CommandLineParser.Parse(["doc.pdf", flag]).FitToPage);
    }
}
