using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Interactive;

namespace Printman.CLI;

public class CliHandler(
    IPrinterDiscoveryService printerDiscovery,
    IPrintService printService,
    Server.PrintingWebServerHost webServer,
    IPrintQueueService queueService)
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IPrintService _printService = printService;
    private readonly Server.PrintingWebServerHost _webServer = webServer;
    private readonly IPrintQueueService _queueService = queueService;

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
                return await _webServer.RunAsync(parsedArgs.ToServerOptions());

            case CliCommandType.Queue:
                return await HandleQueueAsync(parsedArgs.TargetPrinterName, parsedArgs.WatchQueue);

            case CliCommandType.CancelJob:
                return HandleCancelJob(parsedArgs.TargetPrinterName, parsedArgs.JobId);

            case CliCommandType.PurgeQueue:
                return HandlePurgeQueue(parsedArgs.TargetPrinterName);

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

    private async Task<int> HandleQueueAsync(string? printerName, bool watch)
    {
        string? resolvedPrinter = null;
        if (!string.IsNullOrWhiteSpace(printerName))
        {
            var p = _printerDiscovery.FindPrinter(printerName);
            if (p == null)
            {
                ConsoleUi.PrintError($"Printer matching '{printerName}' was not found.");
                return 1;
            }
            resolvedPrinter = p.Name;
        }

        if (!watch)
        {
            var jobs = _queueService.GetJobs(resolvedPrinter);
            var status = !string.IsNullOrWhiteSpace(resolvedPrinter) ? _queueService.GetPrinterStatus(resolvedPrinter) : null;
            ConsoleUi.PrintQueueTable(jobs, status);
            return 0;
        }

        Console.WriteLine($"Starting live queue watcher for '{resolvedPrinter ?? "All Printers"}'... Press Ctrl+C or Q to exit.\n");
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var jobs = _queueService.GetJobs(resolvedPrinter);
                var status = !string.IsNullOrWhiteSpace(resolvedPrinter) ? _queueService.GetPrinterStatus(resolvedPrinter) : null;

                try { Console.Clear(); } catch { }
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("================================================================================");
                Console.WriteLine($" PRINTMAN LIVE SPOOLER WATCHER — {DateTime.Now:HH:mm:ss} (Target: {resolvedPrinter ?? "All Printers"})");
                Console.WriteLine("================================================================================");
                Console.ResetColor();

                ConsoleUi.PrintQueueTable(jobs, status);

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Auto-refreshing every 1.5s. Press Q or Ctrl+C to stop.");
                Console.ResetColor();

                for (int i = 0; i < 15; i++)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                        {
                            return 0;
                        }
                    }
                    await Task.Delay(100, cts.Token);
                }
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private int HandleCancelJob(string? printerName, string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            ConsoleUi.PrintError("Job ID must be specified. Usage: printman cancel <job-id> [-p <printer>]");
            return 1;
        }

        string? resolvedPrinter = null;
        if (!string.IsNullOrWhiteSpace(printerName))
        {
            var p = _printerDiscovery.FindPrinter(printerName);
            if (p != null) resolvedPrinter = p.Name;
        }

        bool success = _queueService.CancelJob(resolvedPrinter, jobId);
        if (success)
        {
            ConsoleUi.PrintSuccess($"Job '{jobId}' cancellation requested.");
            return 0;
        }
        else
        {
            ConsoleUi.PrintError($"Failed to cancel job '{jobId}'. Verify the ID and printer name.");
            return 1;
        }
    }

    private int HandlePurgeQueue(string? printerName)
    {
        var printer = !string.IsNullOrWhiteSpace(printerName)
            ? _printerDiscovery.FindPrinter(printerName)
            : _printerDiscovery.GetDefaultPrinter();

        if (printer == null)
        {
            ConsoleUi.PrintError("Target printer could not be resolved.");
            return 1;
        }

        ConsoleUi.PrintWarning($"Purging all jobs from queue for '{printer.Name}'...");
        int count = _queueService.PurgeSpoolerQueue(printer.Name);
        ConsoleUi.PrintSuccess($"Purged {count} job(s) from '{printer.Name}'.");
        return 0;
    }
}
