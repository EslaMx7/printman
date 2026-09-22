namespace Printman.Core.Models;

public class PrintJobRequest
{
    public required string FilePath { get; init; }
    public string? TargetPrinterName { get; init; }
    public PageRange PageRange { get; init; } = PageRange.All;
    public string? PaperSizeName { get; init; }
    public int Copies { get; init; } = 1;
    public PrintOrientation Orientation { get; init; } = PrintOrientation.Auto;
    public PrintDuplex Duplex { get; init; } = PrintDuplex.Default;
    public PrintColorMode ColorMode { get; init; } = PrintColorMode.Default;
    public int Dpi { get; init; } = 300;
    public bool FitToPage { get; init; } = true;
    public string? JobTitle { get; init; }
    public string? OutputFilePath { get; init; } // Optional: for virtual printers (PDF, XPS) to print directly to file
}
