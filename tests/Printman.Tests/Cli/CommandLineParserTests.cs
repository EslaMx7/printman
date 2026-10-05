using Printman.CLI;
using Printman.Core.Models;

namespace Printman.Tests.Cli;

[TestClass]
public sealed class CommandLineParserTests
{
    // ---------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Parse_EmptyArgs_IsInteractive()
    {
        var args = CommandLineParser.Parse([]);

        Assert.AreEqual(CliCommandType.Interactive, args.Command);
    }

    [TestMethod]
    [DataRow("help")]
    [DataRow("-h")]
    [DataRow("--help")]
    [DataRow("/?")]
    public void Parse_HelpAliases(string first)
    {
        var args = CommandLineParser.Parse([first]);

        Assert.AreEqual(CliCommandType.Help, args.Command);
    }

    [TestMethod]
    [DataRow("list")]
    [DataRow("-list")]
    [DataRow("--list")]
    public void Parse_ListAliases(string first)
    {
        var args = CommandLineParser.Parse([first]);

        Assert.AreEqual(CliCommandType.ListPrinters, args.Command);
    }

    [TestMethod]
    [DataRow("info")]
    [DataRow("-info")]
    [DataRow("--info")]
    public void Parse_InfoAliases_CaptureTarget(string first)
    {
        var args = CommandLineParser.Parse([first, "HP Laser"]);

        Assert.AreEqual(CliCommandType.PrinterInfo, args.Command);
        Assert.AreEqual("HP Laser", args.QueryTarget);
    }

    [TestMethod]
    public void Parse_InfoWithoutTarget_HasNullTarget()
    {
        var args = CommandLineParser.Parse(["info"]);

        Assert.AreEqual(CliCommandType.PrinterInfo, args.Command);
        Assert.IsNull(args.QueryTarget);
    }

    [TestMethod]
    [DataRow("interactive")]
    [DataRow("-i")]
    [DataRow("--interactive")]
    public void Parse_InteractiveAliases(string first)
    {
        var args = CommandLineParser.Parse([first]);

        Assert.AreEqual(CliCommandType.Interactive, args.Command);
    }

    [TestMethod]
    [DataRow("server")]
    [DataRow("serve")]
    [DataRow("--server")]
    [DataRow("--serve")]
    public void Parse_ServerAliases_DoNotShareByDefault(string first)
    {
        var args = CommandLineParser.Parse([first]);

        Assert.AreEqual(CliCommandType.Server, args.Command);
        Assert.IsFalse(args.SharePrinters);
        Assert.IsTrue(args.EnableWebUi);
    }

    // ---------------------------------------------------------------------
    // Server flags
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Parse_ServerPort_AllForms()
    {
        Assert.AreEqual(8080, CommandLineParser.Parse(["serve", "--port", "8080"]).ServerPort);
        Assert.AreEqual(9090, CommandLineParser.Parse(["serve", "--port=9090"]).ServerPort);
        Assert.AreEqual(8081, CommandLineParser.Parse(["serve", "-port", "8081"]).ServerPort);
        Assert.AreEqual(8082, CommandLineParser.Parse(["serve", "-p", "8082"]).ServerPort);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("65536")]
    [DataRow("abc")]
    [DataRow("")]
    public void Parse_ServerPort_Invalid_Throws(string value)
    {
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["serve", "--port", value]));
    }

    [TestMethod]
    public void Parse_ServerPort_MissingValue_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["serve", "--port"]));
    }

    [TestMethod]
    public void Parse_ServerBindAddress_AllForms()
    {
        Assert.AreEqual("10.0.0.1", CommandLineParser.Parse(["serve", "--ip", "10.0.0.1"]).BindAddress);
        Assert.AreEqual("10.0.0.2", CommandLineParser.Parse(["serve", "--bind=10.0.0.2"]).BindAddress);
        Assert.AreEqual("10.0.0.3", CommandLineParser.Parse(["serve", "-ip", "10.0.0.3"]).BindAddress);
        Assert.AreEqual("10.0.0.4", CommandLineParser.Parse(["serve", "-bind", "10.0.0.4"]).BindAddress);
    }

    [TestMethod]
    public void Parse_ServerBindAddress_Blank_KeepsDefault()
    {
        var args = CommandLineParser.Parse(["serve", "--ip", "   "]);

        Assert.AreEqual("0.0.0.0", args.BindAddress);
    }

    [TestMethod]
    public void Parse_ServerPin_SetsPinAndRequiresAuth()
    {
        var args = CommandLineParser.Parse(["serve", "--pin", "1234"]);

        Assert.AreEqual("1234", args.ServerPin);
        Assert.IsTrue(args.RequireAuth);
    }

    [TestMethod]
    public void Parse_ServerNoAuth_ClearsRequireAuth()
    {
        Assert.IsFalse(CommandLineParser.Parse(["serve", "--no-auth"]).RequireAuth);
        Assert.IsFalse(CommandLineParser.Parse(["serve", "-no-auth"]).RequireAuth);
        Assert.IsFalse(CommandLineParser.Parse(["serve", "--allow-anonymous"]).RequireAuth);
        Assert.IsFalse(CommandLineParser.Parse(["serve", "-allow-anonymous"]).RequireAuth);
    }

    [TestMethod]
    public void Parse_ServerPinAfterNoAuth_ReenablesAuth()
    {
        var args = CommandLineParser.Parse(["serve", "--no-auth", "--pin", "9999"]);

        Assert.IsTrue(args.RequireAuth);
        Assert.AreEqual("9999", args.ServerPin);
    }

    [TestMethod]
    public void Parse_ServerUploadAndCacheLimits()
    {
        var args = CommandLineParser.Parse(["serve", "--max-upload-mb", "10", "--cache-limit-mb=20"]);

        Assert.AreEqual(10, args.MaxUploadMb);
        Assert.AreEqual(20, args.CacheLimitMb);
    }

    [TestMethod]
    [DataRow("--max-upload-mb", "0")]
    [DataRow("--max-upload-mb", "-5")]
    [DataRow("--max-upload-mb", "abc")]
    [DataRow("--cache-limit-mb", "0")]
    [DataRow("--cache-limit-mb", "abc")]
    public void Parse_ServerLimits_Invalid_Throw(string flag, string value)
    {
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["serve", flag, value]));
    }

    [TestMethod]
    public void Parse_ServerLimits_MissingValue_Throw()
    {
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["serve", "--max-upload-mb"]));
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["serve", "--cache-limit-mb"]));
    }

    [TestMethod]
    public void Parse_ServerShare_FormsAndDeduplication()
    {
        var withName = CommandLineParser.Parse(["serve", "--share", "HP Laser"]);
        Assert.IsTrue(withName.SharePrinters);
        Assert.IsTrue(withName.EnableWebUi);
        CollectionAssert.AreEqual(new[] { "HP Laser" }, withName.SharedPrinterNames);

        var equals = CommandLineParser.Parse(["serve", "--share=HP Laser"]);
        CollectionAssert.AreEqual(new[] { "HP Laser" }, equals.SharedPrinterNames);

        var shortForm = CommandLineParser.Parse(["serve", "-share", "Canon"]);
        CollectionAssert.AreEqual(new[] { "Canon" }, shortForm.SharedPrinterNames);

        var bare = CommandLineParser.Parse(["serve", "--share"]);
        Assert.IsTrue(bare.SharePrinters);
        Assert.AreEqual(0, bare.SharedPrinterNames.Count);

        var empty = CommandLineParser.Parse(["serve", "--share="]);
        Assert.IsTrue(empty.SharePrinters);
        Assert.AreEqual(0, empty.SharedPrinterNames.Count);

        var deduped = CommandLineParser.Parse(["serve", "--share", "HP", "--share", "hp"]);
        CollectionAssert.AreEqual(new[] { "HP" }, deduped.SharedPrinterNames);
    }

    [TestMethod]
    public void Parse_ServerShareFollowedByFlag_DoesNotConsumeTheFlag()
    {
        var args = CommandLineParser.Parse(["serve", "--share", "--port", "8080"]);

        Assert.IsTrue(args.SharePrinters);
        Assert.AreEqual(0, args.SharedPrinterNames.Count);
        Assert.AreEqual(8080, args.ServerPort);
    }

    [TestMethod]
    public void Parse_ServerShareSelect_Forms()
    {
        var longForm = CommandLineParser.Parse(["serve", "--share-select"]);
        Assert.IsTrue(longForm.SharePrinters);
        Assert.IsTrue(longForm.SelectSharedPrinters);

        var shortForm = CommandLineParser.Parse(["serve", "-share-select"]);
        Assert.IsTrue(shortForm.SelectSharedPrinters);

        var selectOnShare = CommandLineParser.Parse(["share", "--select"]);
        Assert.IsTrue(selectOnShare.SelectSharedPrinters);

        var shortSelect = CommandLineParser.Parse(["share", "-select"]);
        Assert.IsTrue(shortSelect.SelectSharedPrinters);
    }

    [TestMethod]
    public void Parse_SelectOnServeWithoutShare_IsIgnored()
    {
        var args = CommandLineParser.Parse(["serve", "--select"]);

        Assert.IsFalse(args.SelectSharedPrinters);
        Assert.IsFalse(args.SharePrinters);
    }

    [TestMethod]
    public void Parse_ShareAllAndWebFlags()
    {
        Assert.IsTrue(CommandLineParser.Parse(["share", "--all"]).ShareAllPrinters);
        Assert.IsTrue(CommandLineParser.Parse(["share", "-all"]).ShareAllPrinters);

        Assert.IsTrue(CommandLineParser.Parse(["share", "--web"]).EnableWebUi);
        Assert.IsTrue(CommandLineParser.Parse(["share", "-web"]).EnableWebUi);
        Assert.IsTrue(CommandLineParser.Parse(["share", "--ui"]).EnableWebUi);
        Assert.IsTrue(CommandLineParser.Parse(["share", "-ui"]).EnableWebUi);
    }

    [TestMethod]
    public void Parse_AllAndWebOnServeWithoutShare_AreIgnored()
    {
        var args = CommandLineParser.Parse(["serve", "--all", "--web"]);

        Assert.IsFalse(args.ShareAllPrinters);
        Assert.IsTrue(args.EnableWebUi);
    }

    [TestMethod]
    public void Parse_SharePositionalPrinters_DeduplicatesCaseInsensitively()
    {
        var args = CommandLineParser.Parse(["share", "HP Laser", "hp laser", "Canon"]);

        CollectionAssert.AreEqual(new[] { "HP Laser", "Canon" }, args.SharedPrinterNames);
    }

    [TestMethod]
    public void Parse_ShareUnknownOption_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["share", "--bogus"]));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["share", "-x"]));
    }

    [TestMethod]
    public void Parse_ServeUnknownOption_IsIgnored()
    {
        var args = CommandLineParser.Parse(["serve", "--bogus"]);

        Assert.AreEqual(CliCommandType.Server, args.Command);
    }

    [TestMethod]
    public void Parse_ServerIppPort_Forms()
    {
        Assert.AreEqual(8631, CommandLineParser.Parse(["serve", "--ipp-port", "8631"]).IppPort);
        Assert.AreEqual(8632, CommandLineParser.Parse(["serve", "--ipp-port=8632"]).IppPort);
        Assert.AreEqual(8633, CommandLineParser.Parse(["serve", "-ipp-port", "8633"]).IppPort);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("70000")]
    [DataRow("abc")]
    public void Parse_ServerIppPort_Invalid_Throws(string value)
    {
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["serve", "--ipp-port", value]));
    }

    [TestMethod]
    public void Parse_ServerMdnsAndAllowAnySource()
    {
        Assert.IsFalse(CommandLineParser.Parse(["serve", "--no-mdns"]).EnableMdns);
        Assert.IsFalse(CommandLineParser.Parse(["serve", "-no-mdns"]).EnableMdns);
        Assert.IsTrue(CommandLineParser.Parse(["serve", "--ipp-allow-any-source"]).IppAllowAnySource);
        Assert.IsTrue(CommandLineParser.Parse(["serve", "-ipp-allow-any-source"]).IppAllowAnySource);
    }

    [TestMethod]
    public void Parse_ServerOutputDirectory_Forms()
    {
        Assert.AreEqual("out", CommandLineParser.Parse(["serve", "--output-dir", "out"]).ServerOutputDirectory);
        Assert.AreEqual("out2", CommandLineParser.Parse(["serve", "-output-dir", "out2"]).ServerOutputDirectory);
        Assert.AreEqual("out3", CommandLineParser.Parse(["serve", "--output-dir=out3"]).ServerOutputDirectory);
        Assert.IsNull(CommandLineParser.Parse(["serve", "--output-dir", "   "]).ServerOutputDirectory);
    }

    // ---------------------------------------------------------------------
    // Queue / cancel / purge
    // ---------------------------------------------------------------------

    [TestMethod]
    [DataRow("queue")]
    [DataRow("q")]
    [DataRow("jobs")]
    public void Parse_QueueAliases(string first)
    {
        Assert.AreEqual(CliCommandType.Queue, CommandLineParser.Parse([first]).Command);
    }

    [TestMethod]
    public void Parse_QueueFlags()
    {
        Assert.IsTrue(CommandLineParser.Parse(["queue", "--watch"]).WatchQueue);
        Assert.IsTrue(CommandLineParser.Parse(["queue", "-w"]).WatchQueue);

        Assert.AreEqual("HP", CommandLineParser.Parse(["queue", "-printer", "HP"]).TargetPrinterName);
        Assert.AreEqual("Canon", CommandLineParser.Parse(["queue", "--printer=Canon"]).TargetPrinterName);
        Assert.AreEqual("Epson", CommandLineParser.Parse(["queue", "-p", "Epson"]).TargetPrinterName);
        Assert.AreEqual("Positional", CommandLineParser.Parse(["queue", "Positional"]).TargetPrinterName);
        Assert.IsNull(CommandLineParser.Parse(["queue", "--printer"]).TargetPrinterName);
        Assert.AreEqual("First", CommandLineParser.Parse(["queue", "First", "Second"]).TargetPrinterName);
    }

    [TestMethod]
    public void Parse_Cancel_Forms()
    {
        var basic = CommandLineParser.Parse(["cancel", "7"]);
        Assert.AreEqual(CliCommandType.CancelJob, basic.Command);
        Assert.AreEqual("7", basic.JobId);

        Assert.AreEqual("8", CommandLineParser.Parse(["abort", "8"]).JobId);
        Assert.IsNull(CommandLineParser.Parse(["cancel"]).JobId);
        Assert.AreEqual("7", CommandLineParser.Parse(["cancel", "7", "8"]).JobId);
    }

    [TestMethod]
    public void Parse_Cancel_PrinterForms()
    {
        var longForm = CommandLineParser.Parse(["cancel", "-printer", "HP", "7"]);
        Assert.AreEqual("HP", longForm.TargetPrinterName);
        Assert.AreEqual("7", longForm.JobId);

        var equals = CommandLineParser.Parse(["cancel", "--printer=Canon", "8"]);
        Assert.AreEqual("Canon", equals.TargetPrinterName);
        Assert.AreEqual("8", equals.JobId);

        var shortForm = CommandLineParser.Parse(["cancel", "-p", "Epson", "9"]);
        Assert.AreEqual("Epson", shortForm.TargetPrinterName);
        Assert.AreEqual("9", shortForm.JobId);
    }

    [TestMethod]
    public void Parse_Purge_Forms()
    {
        var positional = CommandLineParser.Parse(["purge", "HP"]);
        Assert.AreEqual(CliCommandType.PurgeQueue, positional.Command);
        Assert.AreEqual("HP", positional.TargetPrinterName);

        Assert.AreEqual("Canon", CommandLineParser.Parse(["clear-queue", "Canon"]).TargetPrinterName);
        Assert.AreEqual("HP", CommandLineParser.Parse(["purge", "-printer", "HP"]).TargetPrinterName);
        Assert.AreEqual("Canon", CommandLineParser.Parse(["purge", "--printer=Canon"]).TargetPrinterName);
        Assert.AreEqual("Epson", CommandLineParser.Parse(["purge", "-p", "Epson"]).TargetPrinterName);
        Assert.AreEqual("First", CommandLineParser.Parse(["purge", "First", "Second"]).TargetPrinterName);
        Assert.IsNull(CommandLineParser.Parse(["purge"]).TargetPrinterName);
    }

    // ---------------------------------------------------------------------
    // Print command + flags
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Parse_Print_PositionalFile()
    {
        var args = CommandLineParser.Parse(["doc.pdf"]);

        Assert.AreEqual(CliCommandType.Print, args.Command);
        Assert.AreEqual("doc.pdf", args.FilePath);
    }

    [TestMethod]
    public void Parse_Print_TrimQuotesAndLastPositionalWins()
    {
        Assert.AreEqual("doc.pdf", CommandLineParser.Parse(["\"doc.pdf\""]).FilePath);
        Assert.AreEqual("second.pdf", CommandLineParser.Parse(["first.pdf", "second.pdf"]).FilePath);
    }

    [TestMethod]
    public void Parse_Print_PrinterForms()
    {
        Assert.AreEqual("HP", CommandLineParser.Parse(["doc.pdf", "-printer", "HP"]).TargetPrinterName);
        Assert.AreEqual("HP", CommandLineParser.Parse(["doc.pdf", "--printer=HP"]).TargetPrinterName);
        Assert.AreEqual("HP", CommandLineParser.Parse(["doc.pdf", "-p", "HP"]).TargetPrinterName);
    }

    [TestMethod]
    public void Parse_Print_PageRangeForms()
    {
        Assert.AreEqual("1,2,3", string.Join(',', CommandLineParser.Parse(["doc.pdf", "-pages", "1:3"]).PageRange.ResolvePages(10)));
        Assert.AreEqual("1,2,3", string.Join(',', CommandLineParser.Parse(["doc.pdf", "--pages=1-3"]).PageRange.ResolvePages(10)));
    }

    [TestMethod]
    public void Parse_Print_PageRangeInvalid_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["doc.pdf", "-pages", "nonsense"]));
    }

    [TestMethod]
    public void Parse_Print_PaperSizeForms()
    {
        Assert.AreEqual("A4", CommandLineParser.Parse(["doc.pdf", "-size", "A4"]).PaperSizeName);
        Assert.AreEqual("Letter", CommandLineParser.Parse(["doc.pdf", "--size=Letter"]).PaperSizeName);
        Assert.AreEqual("A5", CommandLineParser.Parse(["doc.pdf", "-s", "A5"]).PaperSizeName);
    }

    [TestMethod]
    public void Parse_Print_CopiesForms()
    {
        Assert.AreEqual(3, CommandLineParser.Parse(["doc.pdf", "-copies", "3"]).Copies);
        Assert.AreEqual(2, CommandLineParser.Parse(["doc.pdf", "--copies=2"]).Copies);
        Assert.AreEqual(4, CommandLineParser.Parse(["doc.pdf", "-c", "4"]).Copies);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("abc")]
    public void Parse_Print_CopiesInvalid_Throws(string value)
    {
        Assert.ThrowsExactly<FormatException>(() => CommandLineParser.Parse(["doc.pdf", $"--copies={value}"]));
    }

    [TestMethod]
    public void Parse_Print_MissingFlagValue_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["doc.pdf", "-printer"]));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["doc.pdf", "-copies", "-1"]));
    }

    [TestMethod]
    public void Parse_Print_OrientationValues()
    {
        Assert.AreEqual(PrintOrientation.Portrait, CommandLineParser.Parse(["doc.pdf", "-orientation", "portrait"]).Orientation);
        Assert.AreEqual(PrintOrientation.Portrait, CommandLineParser.Parse(["doc.pdf", "-o", "p"]).Orientation);
        Assert.AreEqual(PrintOrientation.Landscape, CommandLineParser.Parse(["doc.pdf", "--orientation=landscape"]).Orientation);
        Assert.AreEqual(PrintOrientation.Landscape, CommandLineParser.Parse(["doc.pdf", "-o", "l"]).Orientation);
        Assert.AreEqual(PrintOrientation.Auto, CommandLineParser.Parse(["doc.pdf", "-o", "sideways"]).Orientation);
    }

    [TestMethod]
    public void Parse_Print_DuplexValues()
    {
        Assert.AreEqual(PrintDuplex.Simplex, CommandLineParser.Parse(["doc.pdf", "-duplex", "simplex"]).Duplex);
        Assert.AreEqual(PrintDuplex.Simplex, CommandLineParser.Parse(["doc.pdf", "-duplex", "single"]).Duplex);
        Assert.AreEqual(PrintDuplex.Simplex, CommandLineParser.Parse(["doc.pdf", "-d", "1"]).Duplex);
        Assert.AreEqual(PrintDuplex.Vertical, CommandLineParser.Parse(["doc.pdf", "-d", "vertical"]).Duplex);
        Assert.AreEqual(PrintDuplex.Vertical, CommandLineParser.Parse(["doc.pdf", "--duplex=long"]).Duplex);
        Assert.AreEqual(PrintDuplex.Vertical, CommandLineParser.Parse(["doc.pdf", "-d", "2"]).Duplex);
        Assert.AreEqual(PrintDuplex.Horizontal, CommandLineParser.Parse(["doc.pdf", "-d", "horizontal"]).Duplex);
        Assert.AreEqual(PrintDuplex.Horizontal, CommandLineParser.Parse(["doc.pdf", "-d", "short"]).Duplex);
        Assert.AreEqual(PrintDuplex.Default, CommandLineParser.Parse(["doc.pdf", "-d", "diagonal"]).Duplex);
    }

    [TestMethod]
    public void Parse_Print_ColorValues()
    {
        Assert.AreEqual(PrintColorMode.Color, CommandLineParser.Parse(["doc.pdf", "-color", "color"]).ColorMode);
        Assert.AreEqual(PrintColorMode.Monochrome, CommandLineParser.Parse(["doc.pdf", "--color=mono"]).ColorMode);
        Assert.AreEqual(PrintColorMode.Monochrome, CommandLineParser.Parse(["doc.pdf", "-color", "monochrome"]).ColorMode);
        Assert.AreEqual(PrintColorMode.Monochrome, CommandLineParser.Parse(["doc.pdf", "-color", "bw"]).ColorMode);
        Assert.AreEqual(PrintColorMode.Monochrome, CommandLineParser.Parse(["doc.pdf", "-color", "grayscale"]).ColorMode);
        Assert.AreEqual(PrintColorMode.Default, CommandLineParser.Parse(["doc.pdf", "-color", "sepia"]).ColorMode);
    }

    [TestMethod]
    public void Parse_Print_Dpi()
    {
        Assert.AreEqual(600, CommandLineParser.Parse(["doc.pdf", "-dpi", "600"]).Dpi);
        Assert.AreEqual(1200, CommandLineParser.Parse(["doc.pdf", "--dpi=1200"]).Dpi);
        Assert.AreEqual(300, CommandLineParser.Parse(["doc.pdf", "-dpi", "abc"]).Dpi);
        Assert.AreEqual(300, CommandLineParser.Parse(["doc.pdf", "-dpi", "0"]).Dpi);
    }

    [TestMethod]
    public void Parse_Print_OutputForms()
    {
        Assert.AreEqual("out.xps", CommandLineParser.Parse(["doc.pdf", "-output", "out.xps"]).OutputFilePath);
        Assert.AreEqual("out.xps", CommandLineParser.Parse(["doc.pdf", "--output=out.xps"]).OutputFilePath);
        Assert.AreEqual("out.xps", CommandLineParser.Parse(["doc.pdf", "-out", "out.xps"]).OutputFilePath);
    }

    [TestMethod]
    public void Parse_Print_FitFlags()
    {
        Assert.IsTrue(CommandLineParser.Parse(["doc.pdf"]).FitToPage);
        Assert.IsTrue(CommandLineParser.Parse(["doc.pdf", "-fit"]).FitToPage);
        Assert.IsFalse(CommandLineParser.Parse(["doc.pdf", "-nofit"]).FitToPage);
        Assert.IsFalse(CommandLineParser.Parse(["doc.pdf", "--nofit"]).FitToPage);
    }

    [TestMethod]
    public void Parse_Print_NoFile_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["-fit"]));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["-printer", "HP"]));
    }

    [TestMethod]
    public void Parse_Print_UnknownFlagOrSlashPath_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["doc.pdf", "--bogus"]));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLineParser.Parse(["/tmp/doc.pdf"]));
    }

    // ---------------------------------------------------------------------
    // ParsedArguments mappings
    // ---------------------------------------------------------------------

    [TestMethod]
    public void ToPrintJobRequest_MapsEveryField()
    {
        var args = CommandLineParser.Parse(
        [
            "file.pdf", "-printer", "HP", "-pages", "1:3", "-size", "A4", "-copies", "2",
            "-orientation", "landscape", "-duplex", "horizontal", "-color", "mono",
            "-dpi", "600", "-nofit", "-output", "out.xps"
        ]);

        var request = args.ToPrintJobRequest();

        Assert.AreEqual("file.pdf", request.FilePath);
        Assert.AreEqual("HP", request.TargetPrinterName);
        Assert.AreSame(args.PageRange, request.PageRange);
        Assert.AreEqual("A4", request.PaperSizeName);
        Assert.AreEqual(2, request.Copies);
        Assert.AreEqual(PrintOrientation.Landscape, request.Orientation);
        Assert.AreEqual(PrintDuplex.Horizontal, request.Duplex);
        Assert.AreEqual(PrintColorMode.Monochrome, request.ColorMode);
        Assert.AreEqual(600, request.Dpi);
        Assert.IsFalse(request.FitToPage);
        Assert.AreEqual("out.xps", request.OutputFilePath);
    }

    [TestMethod]
    public void ToPrintJobRequest_WithoutFilePath_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new ParsedArguments { FilePath = null }.ToPrintJobRequest());
        Assert.ThrowsExactly<InvalidOperationException>(() => new ParsedArguments { FilePath = "   " }.ToPrintJobRequest());
    }

    [TestMethod]
    public void ToServerOptions_MapsEveryField()
    {
        var args = CommandLineParser.Parse(
        [
            "serve", "--port", "8080", "--ip", "10.0.0.1", "--pin", "1234",
            "--max-upload-mb", "10", "--cache-limit-mb", "20", "--output-dir", "out",
            "--share", "HP", "--ipp-port", "8631", "--no-mdns", "--ipp-allow-any-source"
        ]);

        var options = args.ToServerOptions();

        Assert.AreEqual(8080, options.Port);
        Assert.AreEqual("10.0.0.1", options.BindAddress);
        Assert.AreEqual("1234", options.Pin);
        Assert.IsTrue(options.RequireAuth);
        Assert.AreEqual(10, options.MaxUploadMb);
        Assert.AreEqual(20, options.CacheLimitMb);
        Assert.AreEqual("out", options.OutputDirectory);
        Assert.IsTrue(options.EnableWebUi);
        Assert.IsTrue(options.Share.Enabled);
        CollectionAssert.AreEqual(new[] { "HP" }, options.Share.Printers.ToArray());
        Assert.AreEqual(8631, options.Share.IppPort);
        Assert.IsFalse(options.Share.EnableMdns);
        Assert.IsTrue(options.Share.AllowAnySource);
    }

    [TestMethod]
    public void ToServerOptions_Defaults()
    {
        var options = CommandLineParser.Parse(["serve"]).ToServerOptions();

        Assert.AreEqual(5000, options.Port);
        Assert.AreEqual("0.0.0.0", options.BindAddress);
        Assert.IsNull(options.Pin);
        Assert.IsTrue(options.RequireAuth);
        Assert.AreEqual(50, options.MaxUploadMb);
        Assert.AreEqual(500, options.CacheLimitMb);
        Assert.IsNull(options.OutputDirectory);
        Assert.IsTrue(options.EnableWebUi);
        Assert.IsFalse(options.Share.Enabled);
        Assert.AreEqual(631, options.Share.IppPort);
        Assert.IsTrue(options.Share.EnableMdns);
        Assert.IsFalse(options.Share.AllowAnySource);
    }
}
