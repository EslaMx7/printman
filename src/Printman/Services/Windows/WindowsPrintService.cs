using System.Drawing;
using System.Drawing.Printing;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services;

public class WindowsPrintService(
    IPrinterDiscoveryService printerDiscovery,
    IDocumentRendererResolver rendererResolver,
    IPrintJobValidator validator) : IPrintService
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;
    private readonly IPrintJobValidator _validator = validator;

    public async Task<PrintJobResult> PrintAsync(
        PrintJobRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate request
        var validation = await _validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return PrintJobResult.Failed(request.TargetPrinterName ?? "Unknown", validation.ErrorMessage!);
        }

        // 2. Resolve target printer
        var printer = !string.IsNullOrWhiteSpace(request.TargetPrinterName)
            ? _printerDiscovery.FindPrinter(request.TargetPrinterName)
            : _printerDiscovery.GetDefaultPrinter();

        if (printer == null)
        {
            return PrintJobResult.Failed(request.TargetPrinterName ?? "Default", "Target printer could not be resolved.");
        }

        // 3. Resolve renderer and pages
        if (_rendererResolver.Resolve(request.FilePath) is not IGdiDocumentRenderer renderer)
        {
            return PrintJobResult.Failed(printer.Name, $"No page renderer is available for '{Path.GetExtension(request.FilePath)}' files.");
        }

        int totalPages = await renderer.GetPageCountAsync(request.FilePath);
        var pagesToPrint = request.PageRange.ResolvePages(totalPages);

        if (pagesToPrint.Count == 0)
        {
            return PrintJobResult.Failed(printer.Name, "No valid pages to print.");
        }

        progress?.Report($"Connecting to printer '{printer.Name}'...");

        return await Task.Run(async () =>
        {
            using var printDoc = new PrintDocument();
            printDoc.PrinterSettings.PrinterName = printer.Name;
            printDoc.DocumentName = request.JobTitle ?? Path.GetFileName(request.FilePath);

            // Suppress the GUI modal dialog so printing runs purely in background / console
            printDoc.PrintController = new StandardPrintController();

            // Set Print to File if requested (for virtual printers like XPS / PDF or silent file output)
            if (!string.IsNullOrWhiteSpace(request.OutputFilePath))
            {
                var fullOutputPath = Path.GetFullPath(request.OutputFilePath);
                var dir = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                printDoc.PrinterSettings.PrintToFile = true;
                printDoc.PrinterSettings.PrintFileName = fullOutputPath;
                progress?.Report($"Routing output to file: '{fullOutputPath}'");
            }

            // Set Copies
            printDoc.PrinterSettings.Copies = (short)Math.Clamp(request.Copies, 1, 999);

            // Set Orientation
            if (request.Orientation != PrintOrientation.Auto)
            {
                printDoc.DefaultPageSettings.Landscape = request.Orientation == PrintOrientation.Landscape;
            }

            // Set Duplex
            if (printer.CanDuplex)
            {
                printDoc.PrinterSettings.Duplex = request.Duplex switch
                {
                    PrintDuplex.Simplex => Duplex.Simplex,
                    PrintDuplex.Vertical => Duplex.Vertical,
                    PrintDuplex.Horizontal => Duplex.Horizontal,
                    _ => printDoc.PrinterSettings.Duplex
                };
            }

            // Set Color Mode
            if (printer.SupportsColor && request.ColorMode != PrintColorMode.Default)
            {
                printDoc.DefaultPageSettings.Color = request.ColorMode == PrintColorMode.Color;
            }

            // Set Paper Size
            string? matchedPaperSizeName = null;
            if (!string.IsNullOrWhiteSpace(request.PaperSizeName))
            {
                var targetSize = request.PaperSizeName.Trim();
                PaperSize? matchedSize = null;

                foreach (PaperSize ps in printDoc.PrinterSettings.PaperSizes)
                {
                    if (string.Equals(ps.PaperName, targetSize, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedSize = ps;
                        break;
                    }
                }

                if (matchedSize == null)
                {
                    foreach (PaperSize ps in printDoc.PrinterSettings.PaperSizes)
                    {
                        if (ps.PaperName.Contains(targetSize, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedSize = ps;
                            break;
                        }
                    }
                }

                if (matchedSize != null)
                {
                    printDoc.DefaultPageSettings.PaperSize = matchedSize;
                    matchedPaperSizeName = matchedSize.PaperName;
                    progress?.Report($"Paper size set to: {matchedPaperSizeName}");
                }
                else
                {
                    progress?.Report($"Warning: Paper size '{targetSize}' not found on printer. Using default size.");
                }
            }

            int pageIndex = 0;
            Exception? renderException = null;

            printDoc.PrintPage += (sender, e) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    e.Cancel = true;
                    return;
                }

                if (pageIndex >= pagesToPrint.Count)
                {
                    e.HasMorePages = false;
                    return;
                }

                int currentPageNumber = pagesToPrint[pageIndex];
                progress?.Report($"Spooling page {currentPageNumber} of {totalPages} (Job page {pageIndex + 1}/{pagesToPrint.Count})...");

                try
                {
                    // Use MarginBounds for safe printable boundary (or PageBounds if fitToPage)
                    Rectangle printableArea = e.MarginBounds;
                    if (request.FullPage)
                    {
                        // Graphics origin sits at the hard margin; shift back so the page maps onto the physical sheet
                        printableArea = e.PageBounds;
                        printableArea.Offset(
                            -(int)Math.Round(e.PageSettings.HardMarginX),
                            -(int)Math.Round(e.PageSettings.HardMarginY));
                    }
                    else if (printableArea.Width <= 0 || printableArea.Height <= 0)
                    {
                        printableArea = e.PageBounds;
                    }

                    // Synchronously wait for the renderer on this page
                    renderer.RenderPageAsync(
                        request.FilePath,
                        currentPageNumber,
                        e.Graphics!,
                        printableArea,
                        request.Dpi,
                        request.FitToPage).GetAwaiter().GetResult();

                    pageIndex++;
                    e.HasMorePages = pageIndex < pagesToPrint.Count;
                }
                catch (Exception ex)
                {
                    renderException = ex;
                    e.Cancel = true;
                    e.HasMorePages = false;
                }
            };

            try
            {
                printDoc.Print();

                if (renderException != null)
                {
                    return PrintJobResult.Failed(printer.Name, $"Printing aborted due to render error: {renderException.Message}");
                }

                progress?.Report($"Print job successfully sent to spooler for '{printer.Name}'.");
                return PrintJobResult.Succeeded(printer.Name, pagesToPrint.Count, request.Copies, matchedPaperSizeName);
            }
            catch (Exception ex)
            {
                return PrintJobResult.Failed(printer.Name, $"Printer spooler error: {ex.Message}");
            }
        }, cancellationToken);
    }
}
