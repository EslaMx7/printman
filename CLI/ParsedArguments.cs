using Printman.Core.Models;

namespace Printman.CLI;

public class ParsedArguments
{
    public CliCommandType Command { get; set; } = CliCommandType.Interactive;
    public string? FilePath { get; set; }
    public string? TargetPrinterName { get; set; }
    public PageRange PageRange { get; set; } = PageRange.All;
    public string? PaperSizeName { get; set; }
    public int Copies { get; set; } = 1;
    public PrintOrientation Orientation { get; set; } = PrintOrientation.Auto;
    public PrintDuplex Duplex { get; set; } = PrintDuplex.Default;
    public PrintColorMode ColorMode { get; set; } = PrintColorMode.Default;
    public int Dpi { get; set; } = 300;
    public bool FitToPage { get; set; } = true;
    public string? OutputFilePath { get; set; }
    public string? QueryTarget { get; set; } // For "info <printer>" command
    public int ServerPort { get; set; } = 5000;
    public string BindAddress { get; set; } = "0.0.0.0";
    public string? ServerPin { get; set; }
    public bool RequireAuth { get; set; } = true;
    public int MaxUploadMb { get; set; } = 50;
    public int CacheLimitMb { get; set; } = 500;

    public PrintJobRequest ToPrintJobRequest()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            throw new InvalidOperationException("No input file path was specified for the print job.");
        }

        return new PrintJobRequest
        {
            FilePath = FilePath,
            TargetPrinterName = TargetPrinterName,
            PageRange = PageRange,
            PaperSizeName = PaperSizeName,
            Copies = Copies,
            Orientation = Orientation,
            Duplex = Duplex,
            ColorMode = ColorMode,
            Dpi = Dpi,
            FitToPage = FitToPage,
            OutputFilePath = OutputFilePath
        };
    }
}
