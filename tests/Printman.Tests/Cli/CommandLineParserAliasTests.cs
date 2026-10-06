using Printman.CLI;
using Printman.Core.Models;

namespace Printman.Tests.Cli;

/// <summary>
/// Exhaustive alias/value-form coverage for every flag in both parser switches.
/// Covers short/long aliases, the <c>--flag=value</c> form, missing values and guards.
/// </summary>
[TestClass]
public sealed class CommandLineParserAliasTests
{
    // ------------------------------------------------------------------ version command

    [TestMethod]
    [DataRow("version")]
    [DataRow("-v")]
    [DataRow("--version")]
    public void Version_CommandAliases_AreAccepted(string command) =>
        Assert.AreEqual(CliCommandType.Version, CommandLineParser.Parse([command]).Command);

    // ------------------------------------------------------------------ serve: value options

    [TestMethod]
    [DataRow("--port", "8080")]
    [DataRow("-port", "8080")]
    [DataRow("-p", "8080")]
    public void Serve_PortAliases_AreAccepted(string flag, string value)
    {
        var args = CommandLineParser.Parse(["serve", flag, value]);
        Assert.AreEqual(8080, args.ServerPort);
    }

    [TestMethod]
    public void Serve_PortEqualsForm_IsAccepted()
    {
        var args = CommandLineParser.Parse(["serve", "--port=9000"]);
        Assert.AreEqual(9000, args.ServerPort);
    }

    [TestMethod]
    [DataRow("--port")]
    [DataRow("--max-upload-mb")]
    [DataRow("--cache-limit-mb")]
    [DataRow("--ipp-port")]
    public void Serve_NumericOptionWithoutValue_Throws(string flag) =>
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["serve", flag]));

    [TestMethod]
    [DataRow("--ip", "10.0.0.5")]
    [DataRow("-ip", "10.0.0.5")]
    [DataRow("--bind", "10.0.0.5")]
    [DataRow("-bind", "10.0.0.5")]
    public void Serve_BindAliases_AreAccepted(string flag, string value)
    {
        var args = CommandLineParser.Parse(["serve", flag, value]);
        Assert.AreEqual("10.0.0.5", args.BindAddress);
    }

    [TestMethod]
    public void Serve_BindEqualsForm_IsAccepted() =>
        Assert.AreEqual("10.0.0.5", CommandLineParser.Parse(["serve", "--ip=10.0.0.5"]).BindAddress);

    [TestMethod]
    public void Serve_BindWithoutValue_LeavesDefault() =>
        Assert.AreEqual("0.0.0.0", CommandLineParser.Parse(["serve", "--ip"]).BindAddress);

    [TestMethod]
    [DataRow("--pin", "4321")]
    [DataRow("-pin", "4321")]
    public void Serve_PinAliases_AreAccepted(string flag, string value)
    {
        var args = CommandLineParser.Parse(["serve", flag, value]);
        Assert.AreEqual("4321", args.ServerPin);
        Assert.IsTrue(args.RequireAuth);
    }

    [TestMethod]
    public void Serve_PinEqualsForm_IsAccepted() =>
        Assert.AreEqual("4321", CommandLineParser.Parse(["serve", "--pin=4321"]).ServerPin);

    [TestMethod]
    public void Serve_PinWithoutValue_IsIgnored() =>
        Assert.IsNull(CommandLineParser.Parse(["serve", "--pin"]).ServerPin);

    [TestMethod]
    [DataRow("--no-auth")]
    [DataRow("-no-auth")]
    [DataRow("--allow-anonymous")]
    [DataRow("-allow-anonymous")]
    public void Serve_AnonymousAliases_DisableAuth(string flag) =>
        Assert.IsFalse(CommandLineParser.Parse(["serve", flag]).RequireAuth);

    [TestMethod]
    [DataRow("--max-upload-mb", "-max-upload-mb")]
    [DataRow("--cache-limit-mb", "-cache-limit-mb")]
    public void Serve_NumericAliases_AreAccepted(string longForm, string shortForm)
    {
        Assert.AreEqual(10, longForm.Contains("upload")
            ? CommandLineParser.Parse(["serve", longForm, "10"]).MaxUploadMb
            : CommandLineParser.Parse(["serve", longForm, "10"]).CacheLimitMb);

        Assert.AreEqual(11, shortForm.Contains("upload")
            ? CommandLineParser.Parse(["serve", shortForm, "11"]).MaxUploadMb
            : CommandLineParser.Parse(["serve", shortForm, "11"]).CacheLimitMb);
    }

    [TestMethod]
    public void Serve_NumericEqualsForms_AreAccepted()
    {
        Assert.AreEqual(12, CommandLineParser.Parse(["serve", "--max-upload-mb=12"]).MaxUploadMb);
        Assert.AreEqual(13, CommandLineParser.Parse(["serve", "--cache-limit-mb=13"]).CacheLimitMb);
    }

    // ------------------------------------------------------------------ share: sharing options

    [TestMethod]
    [DataRow("--share")]
    [DataRow("-share")]
    public void Serve_ShareAlias_EnablesSharing(string flag)
    {
        var args = CommandLineParser.Parse(["serve", flag]);
        Assert.IsTrue(args.SharePrinters);
        Assert.AreEqual(0, args.SharedPrinterNames.Count);
    }

    [TestMethod]
    public void Share_ShareFlagWithValue_AddsNamedPrinter()
    {
        var args = CommandLineParser.Parse(["share", "--share", "HP Laser"]);
        CollectionAssert.Contains(args.SharedPrinterNames, "HP Laser");
    }

    [TestMethod]
    public void Share_ShareEqualsForm_AddsNamedPrinter() =>
        CollectionAssert.Contains(CommandLineParser.Parse(["share", "--share=HP Laser"]).SharedPrinterNames, "HP Laser");

    [TestMethod]
    public void Share_ShareFlagFollowedByFlag_AddsNoName() =>
        Assert.AreEqual(0, CommandLineParser.Parse(["share", "--share", "--no-mdns"]).SharedPrinterNames.Count);

    [TestMethod]
    [DataRow("--share-select")]
    [DataRow("-share-select")]
    [DataRow("--select")]
    [DataRow("-select")]
    public void Share_SelectAliases_RequestChecklist(string flag) =>
        Assert.IsTrue(CommandLineParser.Parse(["share", flag]).SelectSharedPrinters);

    [TestMethod]
    [DataRow("--all")]
    [DataRow("-all")]
    public void Share_AllAliases_RequestEveryPrinter(string flag) =>
        Assert.IsTrue(CommandLineParser.Parse(["share", flag]).ShareAllPrinters);

    [TestMethod]
    [DataRow("--web")]
    [DataRow("-web")]
    [DataRow("--ui")]
    [DataRow("-ui")]
    public void Share_WebAliases_KeepWebUi(string flag) =>
        Assert.IsTrue(CommandLineParser.Parse(["share", flag]).EnableWebUi);

    [TestMethod]
    public void Serve_ShareOnlyGuards_AreIgnoredOnPlainServe()
    {
        var all = CommandLineParser.Parse(["serve", "--all"]);
        var select = CommandLineParser.Parse(["serve", "--select"]);
        var web = CommandLineParser.Parse(["serve", "--ui"]);

        Assert.IsFalse(all.ShareAllPrinters);
        Assert.IsFalse(select.SelectSharedPrinters);
        Assert.IsTrue(web.EnableWebUi);
    }

    [TestMethod]
    [DataRow("--ipp-port")]
    [DataRow("-ipp-port")]
    public void Share_IppPortAliases_AreAccepted(string flag) =>
        Assert.AreEqual(8631, CommandLineParser.Parse(["share", flag, "8631"]).IppPort);

    [TestMethod]
    public void Share_IppPortEqualsForm_IsAccepted() =>
        Assert.AreEqual(8631, CommandLineParser.Parse(["share", "--ipp-port=8631"]).IppPort);

    [TestMethod]
    public void Share_IppPortWithoutValue_Throws() =>
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["share", "--ipp-port"]));

    [TestMethod]
    [DataRow("--no-mdns")]
    [DataRow("-no-mdns")]
    public void Share_NoMdnsAliases_DisableDiscovery(string flag) =>
        Assert.IsFalse(CommandLineParser.Parse(["share", flag]).EnableMdns);

    [TestMethod]
    [DataRow("--ipp-allow-any-source")]
    [DataRow("-ipp-allow-any-source")]
    public void Share_AllowAnySourceAliases_AreAccepted(string flag) =>
        Assert.IsTrue(CommandLineParser.Parse(["share", flag]).IppAllowAnySource);

    [TestMethod]
    [DataRow("--output-dir")]
    [DataRow("-output-dir")]
    public void Share_OutputDirAliases_AreAccepted(string flag) =>
        Assert.AreEqual("out", CommandLineParser.Parse(["share", flag, "out"]).ServerOutputDirectory);

    [TestMethod]
    public void Share_OutputDirEqualsForm_IsAccepted() =>
        Assert.AreEqual("out", CommandLineParser.Parse(["share", "--output-dir=out"]).ServerOutputDirectory);

    [TestMethod]
    public void Share_OutputDirWithoutValue_IsIgnored() =>
        Assert.IsNull(CommandLineParser.Parse(["share", "--output-dir"]).ServerOutputDirectory);

    [TestMethod]
    public void Share_PositionalNameStartingWithDash_Throws() =>
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["share", "--nope"]));

    [TestMethod]
    public void Serve_UnknownOption_IsIgnored()
    {
        var args = CommandLineParser.Parse(["serve", "--definitely-not-a-flag"]);
        Assert.AreEqual(CliCommandType.Server, args.Command);
    }

    // ------------------------------------------------------------------ print: aliases and forms

    [TestMethod]
    [DataRow("-printer")]
    [DataRow("--printer")]
    [DataRow("-p")]
    public void Print_PrinterAliases_AreAccepted(string flag) =>
        Assert.AreEqual("HP", CommandLineParser.Parse(["doc.pdf", flag, "HP"]).TargetPrinterName);

    [TestMethod]
    public void Print_PrinterEqualsForm_IsAccepted() =>
        Assert.AreEqual("HP Laser", CommandLineParser.Parse(["doc.pdf", "--printer=HP Laser"]).TargetPrinterName);

    [TestMethod]
    [DataRow("-pages")]
    [DataRow("--pages")]
    public void Print_PagesAliases_AreAccepted(string flag)
    {
        var args = CommandLineParser.Parse(["doc.pdf", flag, "1:2"]);
        Assert.AreEqual("1,2", string.Join(',', args.PageRange.ResolvePages(5)));
    }

    [TestMethod]
    public void Print_PagesEqualsForm_IsAccepted() =>
        Assert.AreEqual("1,2", string.Join(',', CommandLineParser.Parse(["doc.pdf", "--pages=1-2"]).PageRange.ResolvePages(5)));

    [TestMethod]
    [DataRow("-size")]
    [DataRow("--size")]
    [DataRow("-s")]
    public void Print_SizeAliases_AreAccepted(string flag) =>
        Assert.AreEqual("A4", CommandLineParser.Parse(["doc.pdf", flag, "A4"]).PaperSizeName);

    [TestMethod]
    public void Print_SizeEqualsForm_IsAccepted() =>
        Assert.AreEqual("A4", CommandLineParser.Parse(["doc.pdf", "-size=A4"]).PaperSizeName);

    [TestMethod]
    [DataRow("-copies")]
    [DataRow("--copies")]
    [DataRow("-c")]
    public void Print_CopiesAliases_AreAccepted(string flag) =>
        Assert.AreEqual(3, CommandLineParser.Parse(["doc.pdf", flag, "3"]).Copies);

    [TestMethod]
    public void Print_CopiesEqualsForm_IsAccepted() =>
        Assert.AreEqual(3, CommandLineParser.Parse(["doc.pdf", "--copies=3"]).Copies);

    [TestMethod]
    public void Print_InvalidCopies_Throws() =>
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["doc.pdf", "-copies", "0"]));

    [TestMethod]
    [DataRow("-orientation")]
    [DataRow("--orientation")]
    [DataRow("-o")]
    public void Print_OrientationAliases_AreAccepted(string flag) =>
        Assert.AreEqual(PrintOrientation.Portrait, CommandLineParser.Parse(["doc.pdf", flag, "portrait"]).Orientation);

    [TestMethod]
    public void Print_OrientationEqualsForm_IsAccepted() =>
        Assert.AreEqual(PrintOrientation.Landscape, CommandLineParser.Parse(["doc.pdf", "-orientation=landscape"]).Orientation);

    [TestMethod]
    [DataRow("portrait", PrintOrientation.Portrait)]
    [DataRow("p", PrintOrientation.Portrait)]
    [DataRow("landscape", PrintOrientation.Landscape)]
    [DataRow("l", PrintOrientation.Landscape)]
    [DataRow("sideways", PrintOrientation.Auto)]
    public void Print_OrientationValues_AreMapped(string value, PrintOrientation expected) =>
        Assert.AreEqual(expected, CommandLineParser.Parse(["doc.pdf", "-orientation", value]).Orientation);

    [TestMethod]
    [DataRow("-duplex")]
    [DataRow("--duplex")]
    [DataRow("-d")]
    public void Print_DuplexAliases_AreAccepted(string flag) =>
        Assert.AreEqual(PrintDuplex.Simplex, CommandLineParser.Parse(["doc.pdf", flag, "simplex"]).Duplex);

    [TestMethod]
    public void Print_DuplexEqualsForm_IsAccepted() =>
        Assert.AreEqual(PrintDuplex.Vertical, CommandLineParser.Parse(["doc.pdf", "-duplex=vertical"]).Duplex);

    [TestMethod]
    [DataRow("simplex", PrintDuplex.Simplex)]
    [DataRow("single", PrintDuplex.Simplex)]
    [DataRow("1", PrintDuplex.Simplex)]
    [DataRow("vertical", PrintDuplex.Vertical)]
    [DataRow("long", PrintDuplex.Vertical)]
    [DataRow("2", PrintDuplex.Vertical)]
    [DataRow("horizontal", PrintDuplex.Horizontal)]
    [DataRow("short", PrintDuplex.Horizontal)]
    [DataRow("nonsense", PrintDuplex.Default)]
    public void Print_DuplexValues_AreMapped(string value, PrintDuplex expected) =>
        Assert.AreEqual(expected, CommandLineParser.Parse(["doc.pdf", "-duplex", value]).Duplex);

    [TestMethod]
    [DataRow("-color")]
    [DataRow("--color")]
    public void Print_ColorAliases_AreAccepted(string flag) =>
        Assert.AreEqual(PrintColorMode.Color, CommandLineParser.Parse(["doc.pdf", flag, "color"]).ColorMode);

    [TestMethod]
    public void Print_ColorEqualsForm_IsAccepted() =>
        Assert.AreEqual(PrintColorMode.Monochrome, CommandLineParser.Parse(["doc.pdf", "--color=bw"]).ColorMode);

    [TestMethod]
    [DataRow("color", PrintColorMode.Color)]
    [DataRow("mono", PrintColorMode.Monochrome)]
    [DataRow("monochrome", PrintColorMode.Monochrome)]
    [DataRow("bw", PrintColorMode.Monochrome)]
    [DataRow("grayscale", PrintColorMode.Monochrome)]
    [DataRow("rainbow", PrintColorMode.Default)]
    public void Print_ColorValues_AreMapped(string value, PrintColorMode expected) =>
        Assert.AreEqual(expected, CommandLineParser.Parse(["doc.pdf", "-color", value]).ColorMode);

    [TestMethod]
    [DataRow("-dpi")]
    [DataRow("--dpi")]
    public void Print_DpiAliases_AreAccepted(string flag) =>
        Assert.AreEqual(600, CommandLineParser.Parse(["doc.pdf", flag, "600"]).Dpi);

    [TestMethod]
    public void Print_DpiEqualsForm_IsAccepted() =>
        Assert.AreEqual(600, CommandLineParser.Parse(["doc.pdf", "--dpi=600"]).Dpi);

    [TestMethod]
    public void Print_InvalidDpi_KeepsDefault() =>
        Assert.AreEqual(300, CommandLineParser.Parse(["doc.pdf", "-dpi", "abc"]).Dpi);

    [TestMethod]
    [DataRow("-output")]
    [DataRow("--output")]
    [DataRow("-out")]
    public void Print_OutputAliases_AreAccepted(string flag) =>
        Assert.AreEqual("out.xps", CommandLineParser.Parse(["doc.pdf", flag, "out.xps"]).OutputFilePath);

    [TestMethod]
    public void Print_OutputEqualsForm_IsAccepted() =>
        Assert.AreEqual("out.xps", CommandLineParser.Parse(["doc.pdf", "-output=out.xps"]).OutputFilePath);

    [TestMethod]
    [DataRow("-fit")]
    [DataRow("--fit")]
    public void Print_FitAliases_EnableFit(string flag) =>
        Assert.IsTrue(CommandLineParser.Parse(["doc.pdf", flag]).FitToPage);

    [TestMethod]
    [DataRow("-nofit")]
    [DataRow("--nofit")]
    public void Print_NoFitAliases_DisableFit(string flag) =>
        Assert.IsFalse(CommandLineParser.Parse(["doc.pdf", flag]).FitToPage);

    [TestMethod]
    public void Print_MissingValueForOption_Throws() =>
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["doc.pdf", "-size", "-copies", "2"]));

    [TestMethod]
    public void Print_UnknownOption_Throws() =>
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["doc.pdf", "-bogus"]));

    [TestMethod]
    public void Print_LeadingSlashArgument_IsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["/tmp/report.pdf"]));

    [TestMethod]
    public void Print_NoFilePath_Throws() =>
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["-copies", "2"]));

    // ------------------------------------------------------------------ queue / cancel / purge

    [TestMethod]
    [DataRow("queue")]
    [DataRow("q")]
    [DataRow("jobs")]
    public void Queue_CommandAliases_AreAccepted(string command) =>
        Assert.AreEqual(CliCommandType.Queue, CommandLineParser.Parse([command]).Command);

    [TestMethod]
    [DataRow("--watch")]
    [DataRow("-w")]
    public void Queue_WatchAliases_AreAccepted(string flag) =>
        Assert.IsTrue(CommandLineParser.Parse(["queue", flag]).WatchQueue);

    [TestMethod]
    public void Queue_PrinterForms_AreAccepted()
    {
        Assert.AreEqual("HP", CommandLineParser.Parse(["queue", "--printer", "HP"]).TargetPrinterName);
        Assert.AreEqual("HP", CommandLineParser.Parse(["queue", "-printer=HP"]).TargetPrinterName);
        Assert.AreEqual("HP", CommandLineParser.Parse(["queue", "HP"]).TargetPrinterName);
    }

    [TestMethod]
    public void Cancel_PrinterAndJobIdForms_AreAccepted()
    {
        var spaced = CommandLineParser.Parse(["cancel", "--printer", "HP", "14"]);
        Assert.AreEqual("HP", spaced.TargetPrinterName);
        Assert.AreEqual("14", spaced.JobId);

        var equals = CommandLineParser.Parse(["cancel", "-printer=HP", "15"]);
        Assert.AreEqual("HP", equals.TargetPrinterName);
        Assert.AreEqual("15", equals.JobId);
    }

    [TestMethod]
    public void Purge_PrinterForms_AreAccepted()
    {
        Assert.AreEqual("HP", CommandLineParser.Parse(["purge", "--printer", "HP"]).TargetPrinterName);
        Assert.AreEqual("HP", CommandLineParser.Parse(["purge", "-printer=HP"]).TargetPrinterName);
        Assert.AreEqual("HP", CommandLineParser.Parse(["purge", "HP"]).TargetPrinterName);
    }

    [TestMethod]
    public void Print_LastPositionalArgument_Wins()
    {
        // The parser assigns the positional path each time; the last one wins.
        var args = CommandLineParser.Parse(["doc.pdf", "second.pdf"]);
        Assert.AreEqual("second.pdf", args.FilePath);
    }
}
