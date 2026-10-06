using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services.Cups;

/// <summary>
/// Prints by submitting the original document to a CUPS queue (IPP Print-Job). CUPS converts PDF, images,
/// text and PWG / Apple raster itself and applies copies, page ranges, media, sides, orientation and color.
/// Print-to-file (<see cref="PrintJobRequest.OutputFilePath"/>) runs the same conversion locally with
/// cupsfilter and writes a PDF instead of printing.
/// </summary>
public sealed class CupsPrintService(
    IPrinterDiscoveryService printerDiscovery,
    IDocumentRendererResolver rendererResolver,
    IPrintJobValidator validator,
    CupsClient cups) : IPrintService
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;
    private readonly IPrintJobValidator _validator = validator;
    private readonly CupsClient _cups = cups;

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

        // 3. Resolve pages
        var renderer = _rendererResolver.Resolve(request.FilePath);
        int totalPages = await renderer.GetPageCountAsync(request.FilePath);
        var pagesToPrint = request.PageRange.ResolvePages(totalPages);

        if (pagesToPrint.Count == 0)
        {
            return PrintJobResult.Failed(printer.Name, "No valid pages to print.");
        }

        var options = BuildOptions(request, printer, pagesToPrint, totalPages, progress, out var paperSizeName);

        try
        {
            if (!string.IsNullOrWhiteSpace(request.OutputFilePath))
            {
                return await PrintToFileAsync(request, printer, options, pagesToPrint.Count, paperSizeName, progress, cancellationToken);
            }

            progress?.Report($"Sending document to CUPS queue '{printer.Name}'...");
            var response = await _cups.SendAsync(
                IppOperation.PrintJob,
                $"/printers/{Uri.EscapeDataString(printer.Name)}",
                op =>
                {
                    op.AddUri("printer-uri", CupsClient.PrinterUri(printer.Name));
                    op.AddName("job-name", request.JobTitle ?? Path.GetFileName(request.FilePath));
                    op.Add("document-format", IppTag.MimeMediaType, DocumentFormat(request.FilePath));
                },
                job => AddJobAttributes(job, options),
                request.FilePath,
                cancellationToken);

            var jobId = response.Group(IppTag.JobAttributes)?.Get("job-id")?.First?.AsInt();
            progress?.Report($"Print job {(jobId is int id ? $"#{id} " : "")}queued on '{printer.Name}'.");
            return PrintJobResult.Succeeded(printer.Name, pagesToPrint.Count, request.Copies, paperSizeName);
        }
        catch (CupsException ex)
        {
            return PrintJobResult.Failed(printer.Name, $"CUPS error: {ex.Message}");
        }
    }

    /// <summary>Job template values shared by Print-Job and cupsfilter.</summary>
    private sealed record JobOptions(
        int Copies,
        IReadOnlyList<IppRange> PageRanges,
        string? Media,
        string? Sides,
        int? Orientation,
        string? ColorMode,
        string? Scaling);

    private static JobOptions BuildOptions(
        PrintJobRequest request,
        PrinterInfo printer,
        List<int> pagesToPrint,
        int totalPages,
        IProgress<string>? progress,
        out string? paperSizeName)
    {
        paperSizeName = null;
        string? media = null;
        if (!string.IsNullOrWhiteSpace(request.PaperSizeName))
        {
            var target = request.PaperSizeName.Trim();
            var match = printer.SupportedPaperSizes.FirstOrDefault(p =>
                            string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Keyword, target, StringComparison.OrdinalIgnoreCase))
                        ?? printer.SupportedPaperSizes.FirstOrDefault(p => p.Name.Contains(target, StringComparison.OrdinalIgnoreCase));

            if (match?.Keyword != null)
            {
                media = match.Keyword;
                paperSizeName = match.Name;
                progress?.Report($"Paper size set to: {paperSizeName}");
            }
            else
            {
                progress?.Report($"Warning: Paper size '{target}' not found on printer. Using default size.");
            }
        }

        string? sides = null;
        if (printer.CanDuplex)
        {
            sides = request.Duplex switch
            {
                PrintDuplex.Simplex => "one-sided",
                PrintDuplex.Vertical => "two-sided-long-edge",
                PrintDuplex.Horizontal => "two-sided-short-edge",
                _ => null
            };
        }

        string? colorMode = printer.SupportsColor
            ? request.ColorMode switch
            {
                PrintColorMode.Color => "color",
                PrintColorMode.Monochrome => "monochrome",
                _ => null
            }
            : null;

        // Pre-laid-out pages (IPP jobs) keep the client's layout; otherwise fit or keep the natural size
        string? scaling = request.FullPage ? null : request.FitToPage ? "fit" : "none";

        int? orientation = request.Orientation switch
        {
            PrintOrientation.Portrait => 3,
            PrintOrientation.Landscape => 4,
            _ => null
        };

        IReadOnlyList<IppRange> ranges = pagesToPrint.Count == totalPages ? [] : ToRanges(pagesToPrint);

        return new JobOptions(Math.Clamp(request.Copies, 1, 999), ranges, media, sides, orientation, colorMode, scaling);
    }

    private static void AddJobAttributes(IppAttributeGroup job, JobOptions options)
    {
        if (options.Copies > 1) job.AddInteger("copies", options.Copies);
        if (options.PageRanges.Count > 0) job.Add("page-ranges", IppTag.RangeOfInteger, options.PageRanges.Cast<object?>().ToArray());
        if (options.Media != null) job.AddKeyword("media", options.Media);
        if (options.Sides != null) job.AddKeyword("sides", options.Sides);
        if (options.Orientation is int o) job.AddEnum("orientation-requested", o);
        if (options.ColorMode != null) job.AddKeyword("print-color-mode", options.ColorMode);
        if (options.Scaling != null) job.AddKeyword("print-scaling", options.Scaling);
    }

    private async Task<PrintJobResult> PrintToFileAsync(
        PrintJobRequest request,
        PrinterInfo printer,
        JobOptions options,
        int pageCount,
        string? paperSizeName,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var cupsfilter = ExternalTool.Find("cupsfilter");
        if (cupsfilter == null)
        {
            return PrintJobResult.Failed(printer.Name, "Print-to-file needs 'cupsfilter' (part of CUPS); it was not found.");
        }

        var fullOutputPath = Path.GetFullPath(request.OutputFilePath!);
        var dir = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        progress?.Report($"Routing output to file: '{fullOutputPath}'");

        // application/vnd.cups-pdf (still a PDF) forces CUPS' pdftopdf filter, which applies page ranges,
        // media and orientation; asking for application/pdf would copy a PDF input unchanged
        var args = new List<string> { "-m", "application/vnd.cups-pdf" };
        var format = DocumentFormat(request.FilePath);
        if (format != "application/octet-stream")
        {
            args.Add("-i");
            args.Add(format);
        }
        void Option(string value) { args.Add("-o"); args.Add(value); }

        if (options.PageRanges.Count > 0) Option("page-ranges=" + string.Join(',', options.PageRanges.Select(r => r.Lower == r.Upper ? $"{r.Lower}" : $"{r.Lower}-{r.Upper}")));
        if (options.Media != null) Option("media=" + options.Media);
        if (options.Orientation is int o) Option($"orientation-requested={o}");
        if (options.Scaling != null) Option("print-scaling=" + options.Scaling);

        // The conversion needs the queue's PPD; cupsfilter's built-in default PPD is often not installed
        var ppd = Path.Combine(Path.GetTempPath(), $"printman-{Guid.NewGuid():N}.ppd");
        try
        {
            if (await _cups.DownloadPpdAsync(printer.Name, ppd, ct))
            {
                args.Add("-p");
                args.Add(ppd);
            }
            args.Add(request.FilePath);

            var result = await ExternalTool.RunAsync(cupsfilter, args, stdoutFile: fullOutputPath, ct: ct);
            if (result.ExitCode != 0 || !File.Exists(fullOutputPath) || new FileInfo(fullOutputPath).Length == 0)
            {
                var detail = result.StandardError
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault(line => line.StartsWith("ERROR:", StringComparison.Ordinal));
                try { File.Delete(fullOutputPath); } catch { }
                return PrintJobResult.Failed(printer.Name, $"cupsfilter could not convert the document{(detail != null ? $" ({detail})" : ".")}");
            }
        }
        finally
        {
            try { File.Delete(ppd); } catch { }
        }

        progress?.Report($"Document written to '{fullOutputPath}'.");
        return PrintJobResult.Succeeded(printer.Name, pageCount, request.Copies, paperSizeName);
    }

    /// <summary>MIME type CUPS should convert from; unknown extensions are auto-typed by cupsd.</summary>
    private static string DocumentFormat(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/x-bitmap",
        ".tif" or ".tiff" => "image/tiff",
        ".pwg" => "image/pwg-raster",
        ".urf" => "image/urf",
        ".txt" or ".log" or ".csv" or ".json" or ".md" or ".xml" or ".ini" or ".yaml" or ".yml" or ".sql" or ".cmd" or ".ps1" => "text/plain",
        _ => "application/octet-stream"
    };

    private static List<IppRange> ToRanges(List<int> sortedPages)
    {
        var ranges = new List<IppRange>();
        int start = sortedPages[0], end = start;
        foreach (var page in sortedPages.Skip(1))
        {
            if (page == end + 1)
            {
                end = page;
                continue;
            }
            ranges.Add(new IppRange(start, end));
            start = end = page;
        }
        ranges.Add(new IppRange(start, end));
        return ranges;
    }
}
