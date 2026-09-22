using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Interactive;

namespace Printman.CLI;

public class CliHandler(
    IPrinterDiscoveryService printerDiscovery,
    IPrintService printService,
    Server.PrintingWebServerHost webServer)
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IPrintService _printService = printService;
    private readonly Server.PrintingWebServerHost _webServer = webServer;

    public async Task<int> ExecuteAsync(ParsedArguments parsedArgs)
    {
        switch (parsedArgs.Command)
        {
            case CliCommandType.Help:
                ConsoleUi.PrintHelp();
                return 0;

            case CliCommandType.ListPrinters:
                return HandleListPrinters();

            case CliCommandType.PrinterInfo:
                return HandlePrinterInfo(parsedArgs.QueryTarget);

            case CliCommandType.Server:
                return await _webServer.RunAsync(
                    parsedArgs.ServerPort,
                    parsedArgs.BindAddress,
                    parsedArgs.ServerPin,
                    parsedArgs.RequireAuth,
                    parsedArgs.MaxUploadMb,
                    parsedArgs.CacheLimitMb);

            case CliCommandType.Print:
                return await HandlePrintAsync(parsedArgs.ToPrintJobRequest());

            default:
                ConsoleUi.PrintError($"Unknown command type: {parsedArgs.Command}");
                return 1;
        }
    }

    private int HandleListPrinters()
    {
        ConsoleUi.PrintInfo("Querying installed printers on this machine...");
        var printers = _printerDiscovery.GetPrinters();

        if (printers.Count == 0)
        {
            ConsoleUi.PrintWarning("No printers were found installed on this system.");
            return 0;
        }

        ConsoleUi.PrintPrintersTable(printers);
        return 0;
    }

    private int HandlePrinterInfo(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            var defaultPrinter = _printerDiscovery.GetDefaultPrinter();
            if (defaultPrinter == null)
            {
                ConsoleUi.PrintError("No printer specified and no default printer found.");
                return 1;
            }
            ConsoleUi.PrintPrinterDetails(defaultPrinter);
            return 0;
        }

        var printer = _printerDiscovery.FindPrinter(query);
        if (printer == null)
        {
            ConsoleUi.PrintError($"Printer matching '{query}' was not found.");
            return 1;
        }

        ConsoleUi.PrintPrinterDetails(printer);
        return 0;
    }

    private async Task<int> HandlePrintAsync(PrintJobRequest request)
    {
        ConsoleUi.PrintInfo($"Preparing print job for: '{request.FilePath}'");
        if (request.PageRange.IsAllPages)
        {
            ConsoleUi.PrintInfo("Page selection: All pages");
        }
        else
        {
            ConsoleUi.PrintInfo($"Page selection: {request.PageRange.RawExpression}");
        }

        var progress = new Progress<string>(msg =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" -> {msg}");
            Console.ResetColor();
        });

        var result = await _printService.PrintAsync(request, progress);

        if (result.Success)
        {
            ConsoleUi.PrintSuccess(
                $"Print job complete! Printed {result.PagesPrinted} page(s) ({result.CopiesPrinted} copy/copies) to '{result.PrinterUsed}'.");
            return 0;
        }
        else
        {
            ConsoleUi.PrintError($"Print failed: {result.ErrorMessage}");
            return 1;
        }
    }
}
