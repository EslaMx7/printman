using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Interactive;

public class InteractiveWizard(
    IPrinterDiscoveryService printerDiscovery,
    IPrintService printService,
    IDocumentRendererResolver rendererResolver,
    Server.PrintingWebServerHost webServer,
    IPrintQueueService queueService)
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IPrintService _printService = printService;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;
    private readonly Server.PrintingWebServerHost _webServer = webServer;
    private readonly IPrintQueueService _queueService = queueService;

    public async Task RunAsync()
    {
        ConsoleUi.ShowBanner();

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("MAIN MENU:");
            Console.ResetColor();
            Console.WriteLine("  [1] Print a Document (PDF, Image, Text)");
            Console.WriteLine("  [2] List Installed Printers");
            Console.WriteLine("  [3] Inspect Printer Details");
            Console.WriteLine("  [4] View & Manage Print Spooler Queue");
            Console.WriteLine("  [5] Start Mobile LAN Web Server");
            Console.WriteLine("  [6] Exit");
            Console.Write("\nSelect an option [1-6] (default 1): ");

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) input = "1";

            switch (input)
            {
                case "1":
                    await RunPrintWizardAsync();
                    break;
                case "2":
                    ShowPrinters();
                    break;
                case "3":
                    ShowPrinterDetails();
                    break;
                case "4":
                    await ManageQueueAsync();
                    break;
                case "5":
                    await StartWebServerAsync();
                    break;
                case "6" or "q" or "exit":
                    Console.WriteLine("Goodbye!");
                    return;
                default:
                    ConsoleUi.PrintWarning("Invalid option. Please enter 1, 2, 3, 4, 5, or 6.");
                    break;
            }
        }
    }

    private async Task StartWebServerAsync()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("--- Mobile LAN Web Server ---");
        Console.ResetColor();

        int port = 5000;
        Console.Write("Enter server port (Enter for default 5000): ");
        var portInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(portInput))
        {
            if (int.TryParse(portInput, out int p) && p > 0 && p <= 65535)
            {
                port = p;
            }
            else
            {
                ConsoleUi.PrintWarning("Invalid port number. Falling back to port 5000.");
            }
        }

        Console.Write("Require PIN protection? [Y/n] (default Yes): ");
        var authInput = Console.ReadLine()?.Trim().ToLowerInvariant();
        bool requireAuth = authInput != "n" && authInput != "no";
        string? pin = null;

        if (requireAuth)
        {
            Console.Write("Enter custom PIN (Enter to auto-generate): ");
            var customPin = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(customPin))
            {
                pin = customPin;
            }
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            await _webServer.RunAsync(
                port: port,
                bindAddress: "0.0.0.0",
                pin: pin,
                requireAuth: requireAuth,
                maxUploadMb: 50,
                cacheLimitMb: 500,
                ct: cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private void ShowPrinters()
    {
        var printers = _printerDiscovery.GetPrinters();
        ConsoleUi.PrintPrintersTable(printers);
    }

    private void ShowPrinterDetails()
    {
        var printers = _printerDiscovery.GetPrinters();
        if (printers.Count == 0)
        {
            ConsoleUi.PrintWarning("No printers found.");
            return;
        }

        ConsoleUi.PrintPrintersTable(printers);
        Console.Write("Enter printer number to inspect: ");
        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out int idx) && idx >= 1 && idx <= printers.Count)
        {
            ConsoleUi.PrintPrinterDetails(printers[idx - 1]);
        }
        else
        {
            ConsoleUi.PrintError("Invalid printer selection.");
        }
    }

    private async Task RunPrintWizardAsync()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("--- Print Wizard ---");
        Console.ResetColor();

        // 1. File path
        string? filePath = null;
        while (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Console.Write("\nDocument path to print (drag & drop file here or 'cancel'): ");
            var raw = Console.ReadLine()?.Trim().Trim('"', '\'');
            if (string.Equals(raw, "cancel", StringComparison.OrdinalIgnoreCase)) return;

            if (!string.IsNullOrWhiteSpace(raw) && File.Exists(raw))
            {
                filePath = Path.GetFullPath(raw);
            }
            else
            {
                ConsoleUi.PrintError("File not found. Please verify the path and try again.");
            }
        }

        // Check if renderer supports this file
        IDocumentRenderer renderer;
        try
        {
            renderer = _rendererResolver.Resolve(filePath);
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError(ex.Message);
            return;
        }

        int totalPages;
        try
        {
            totalPages = await renderer.GetPageCountAsync(filePath);
            ConsoleUi.PrintInfo($"Detected format '{Path.GetExtension(filePath)}' with {totalPages} page(s).");
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"Unable to read document: {ex.Message}");
            return;
        }

        // 2. Select Printer
        var printers = _printerDiscovery.GetPrinters();
        if (printers.Count == 0)
        {
            ConsoleUi.PrintError("No printers installed on this machine.");
            return;
        }

        ConsoleUi.PrintPrintersTable(printers);
        var defaultPrinter = _printerDiscovery.GetDefaultPrinter();
        int defaultIndex = defaultPrinter != null ? printers.ToList().FindIndex(p => p.Name == defaultPrinter.Name) + 1 : 1;

        Console.Write($"Select printer [1-{printers.Count}] (Enter for default '{defaultPrinter?.Name}'): ");
        var printerChoice = Console.ReadLine()?.Trim();
        PrinterInfo selectedPrinter;
        if (string.IsNullOrEmpty(printerChoice))
        {
            selectedPrinter = defaultPrinter ?? printers[0];
        }
        else if (int.TryParse(printerChoice, out int pIdx) && pIdx >= 1 && pIdx <= printers.Count)
        {
            selectedPrinter = printers[pIdx - 1];
        }
        else
        {
            var match = _printerDiscovery.FindPrinter(printerChoice);
            if (match == null)
            {
                ConsoleUi.PrintError("Printer selection not recognized.");
                return;
            }
            selectedPrinter = match;
        }

        ConsoleUi.PrintInfo($"Target printer: {selectedPrinter.Name}");

        // 3. Pages selection
        PageRange pageRange = PageRange.All;
        if (totalPages > 1)
        {
            Console.Write($"\nPages to print (e.g. 1:3, 1-3, 1,3,5 or Enter for All {totalPages} pages): ");
            var pagesInput = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(pagesInput))
            {
                try
                {
                    pageRange = PageRange.Parse(pagesInput);
                }
                catch (Exception ex)
                {
                    ConsoleUi.PrintError(ex.Message);
                    return;
                }
            }
        }

        var resolvedPages = pageRange.ResolvePages(totalPages);
        ConsoleUi.PrintInfo($"Selected {resolvedPages.Count} page(s): {string.Join(", ", resolvedPages)}");

        // 4. Paper Size selection
        string? paperSizeName = null;
        if (selectedPrinter.SupportedPaperSizes.Count > 0)
        {
            Console.Write("\nSpecify paper size (e.g. A4, Letter or Enter for printer default): ");
            var sizeInput = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(sizeInput))
            {
                paperSizeName = sizeInput;
            }
        }

        // 5. Copies
        int copies = 1;
        Console.Write("\nNumber of copies [1-99] (Enter for 1): ");
        var copiesInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(copiesInput) && int.TryParse(copiesInput, out int c) && c > 0)
        {
            copies = c;
        }

        // 6. Orientation
        var orientation = PrintOrientation.Auto;
        Console.Write("\nOrientation [auto/portrait/landscape] (Enter for Auto): ");
        var orientInput = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (orientInput is "portrait" or "p") orientation = PrintOrientation.Portrait;
        else if (orientInput is "landscape" or "l") orientation = PrintOrientation.Landscape;

        // 7. Duplex (if supported)
        var duplex = PrintDuplex.Default;
        if (selectedPrinter.CanDuplex)
        {
            Console.Write("\nDuplex mode [simplex/vertical/horizontal] (Enter for default): ");
            var duplexInput = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (duplexInput is "simplex" or "1") duplex = PrintDuplex.Simplex;
            else if (duplexInput is "vertical" or "long" or "2") duplex = PrintDuplex.Vertical;
            else if (duplexInput is "horizontal" or "short") duplex = PrintDuplex.Horizontal;
        }

        // 8. Confirm
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== Print Job Summary ===");
        Console.ResetColor();
        Console.WriteLine($"  Document:    {Path.GetFileName(filePath)}");
        Console.WriteLine($"  Printer:     {selectedPrinter.Name}");
        Console.WriteLine($"  Pages:       {resolvedPages.Count} ({string.Join(", ", resolvedPages)})");
        Console.WriteLine($"  Copies:      {copies}");
        Console.WriteLine($"  Paper Size:  {paperSizeName ?? "Printer Default"}");
        Console.WriteLine($"  Orientation: {orientation}");
        if (selectedPrinter.CanDuplex) Console.WriteLine($"  Duplex:      {duplex}");

        Console.Write("\nProceed with printing? [Y/n]: ");
        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(confirm) && confirm != "y" && confirm != "yes")
        {
            ConsoleUi.PrintWarning("Print job cancelled by user.");
            return;
        }

        var request = new PrintJobRequest
        {
            FilePath = filePath,
            TargetPrinterName = selectedPrinter.Name,
            PageRange = pageRange,
            PaperSizeName = paperSizeName,
            Copies = copies,
            Orientation = orientation,
            Duplex = duplex,
            FitToPage = true
        };

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
                $"Print job successfully spooled! Printed {result.PagesPrinted} page(s) to '{result.PrinterUsed}'.");
        }
        else
        {
            ConsoleUi.PrintError($"Printing failed: {result.ErrorMessage}");
        }
    }

    private async Task ManageQueueAsync()
    {
        var defaultPrinter = _printerDiscovery.GetDefaultPrinter();
        string targetPrinter = defaultPrinter?.Name ?? "Default";

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("--- Print Spooler Queue Management ---");
            Console.ResetColor();
            Console.WriteLine($"Selected Printer: {targetPrinter}");
            Console.WriteLine("  [1] Live Watcher Dashboard (Real-time auto-refresh, cancel hotkeys)");
            Console.WriteLine("  [2] View Current Spooler Queue Snapshot");
            Console.WriteLine("  [3] Cancel a Print Job by ID");
            Console.WriteLine("  [4] Purge All Jobs on Printer");
            Console.WriteLine("  [5] Select Different Printer to Inspect");
            Console.WriteLine("  [6] Return to Main Menu");
            Console.Write("\nSelect an option [1-6] (default 1): ");

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) input = "1";

            switch (input)
            {
                case "1":
                    await RunLiveQueueWatcherAsync(targetPrinter);
                    break;
                case "2":
                    ShowQueueSnapshot(targetPrinter);
                    break;
                case "3":
                    CancelJobPrompt(targetPrinter);
                    break;
                case "4":
                    PurgeQueuePrompt(targetPrinter);
                    break;
                case "5":
                    var selected = PromptSelectPrinter();
                    if (selected != null) targetPrinter = selected.Name;
                    break;
                case "6" or "q" or "exit":
                    return;
                default:
                    ConsoleUi.PrintWarning("Invalid option. Please enter 1 to 6.");
                    break;
            }
        }
    }

    private PrinterInfo? PromptSelectPrinter()
    {
        var printers = _printerDiscovery.GetPrinters();
        if (printers.Count == 0)
        {
            ConsoleUi.PrintError("No printers installed on this machine.");
            return null;
        }

        ConsoleUi.PrintPrintersTable(printers);
        var defaultPrinter = _printerDiscovery.GetDefaultPrinter();
        Console.Write($"Select printer [1-{printers.Count}] (Enter for '{defaultPrinter?.Name}'): ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) return defaultPrinter ?? printers[0];

        if (int.TryParse(input, out int idx) && idx >= 1 && idx <= printers.Count)
        {
            return printers[idx - 1];
        }

        var found = _printerDiscovery.FindPrinter(input);
        if (found != null) return found;

        ConsoleUi.PrintWarning("Invalid printer selection. Keeping current printer.");
        return null;
    }

    private async Task RunLiveQueueWatcherAsync(string printerName)
    {
        try { Console.Clear(); } catch { }
        using var cts = new CancellationTokenSource();

        Console.WriteLine($"Starting Live Queue Watcher for '{printerName}'...");
        Console.WriteLine("Press [C] to Cancel Job • [A] to Purge All • [Q] or [Esc] to Return\n");

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var jobs = _queueService.GetJobs(printerName);
                var status = _queueService.GetPrinterStatus(printerName);

                try { Console.Clear(); } catch { }
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("================================================================================");
                Console.WriteLine($" PRINTMAN LIVE SPOOLER WATCHER — {DateTime.Now:HH:mm:ss}");
                Console.WriteLine("================================================================================");
                Console.ResetColor();

                ConsoleUi.PrintQueueTable(jobs, status);

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Controls: [C] Cancel a Job  |  [A] Purge All Queue  |  [Q] Exit Watcher");
                Console.ResetColor();

                for (int i = 0; i < 10; i++)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                        {
                            return;
                        }
                        if (key.Key is ConsoleKey.C)
                        {
                            Console.WriteLine();
                            CancelJobPrompt(printerName);
                            break;
                        }
                        if (key.Key is ConsoleKey.A)
                        {
                            Console.WriteLine();
                            PurgeQueuePrompt(printerName);
                            break;
                        }
                    }
                    await Task.Delay(100, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Watcher error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private void ShowQueueSnapshot(string printerName)
    {
        var jobs = _queueService.GetJobs(printerName);
        var status = _queueService.GetPrinterStatus(printerName);
        ConsoleUi.PrintQueueTable(jobs, status);
    }

    private void CancelJobPrompt(string printerName)
    {
        Console.Write("Enter Job ID to cancel: ");
        var jobId = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(jobId))
        {
            ConsoleUi.PrintWarning("No Job ID entered.");
            return;
        }

        bool success = _queueService.CancelJob(printerName, jobId);
        if (success)
        {
            ConsoleUi.PrintSuccess($"Job '{jobId}' cancellation requested.");
        }
        else
        {
            ConsoleUi.PrintError($"Failed to cancel job '{jobId}'. Verify the ID and try again.");
        }
    }

    private void PurgeQueuePrompt(string printerName)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write($"Are you sure you want to PURGE all print jobs for '{printerName}'? [y/N]: ");
        Console.ResetColor();
        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (confirm is "y" or "yes")
        {
            int purged = _queueService.PurgeSpoolerQueue(printerName);
            ConsoleUi.PrintSuccess($"Purged {purged} job(s) from '{printerName}'.");
        }
        else
        {
            ConsoleUi.PrintInfo("Purge cancelled.");
        }
    }
}
