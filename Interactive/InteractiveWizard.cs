using OhMyPrinter.Core.Abstractions;
using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Interactive;

public class InteractiveWizard(
    IPrinterDiscoveryService printerDiscovery,
    IPrintService printService,
    IDocumentRendererResolver rendererResolver)
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IPrintService _printService = printService;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;

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
            Console.WriteLine("  [4] Exit");
            Console.Write("\nSelect an option [1-4] (default 1): ");

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
                case "4" or "q" or "exit":
                    Console.WriteLine("Goodbye!");
                    return;
                default:
                    ConsoleUi.PrintWarning("Invalid option. Please enter 1, 2, 3, or 4.");
                    break;
            }
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
}
