namespace OhMyPrinter.Core.Models;

public class PrintJobResult
{
    public bool Success { get; init; }
    public required string PrinterUsed { get; init; }
    public int PagesPrinted { get; init; }
    public int CopiesPrinted { get; init; }
    public string? PaperSizeUsed { get; init; }
    public string? ErrorMessage { get; init; }

    public static PrintJobResult Failed(string printerUsed, string errorMessage) =>
        new()
        {
            Success = false,
            PrinterUsed = printerUsed,
            ErrorMessage = errorMessage
        };

    public static PrintJobResult Succeeded(string printerUsed, int pagesPrinted, int copiesPrinted, string? paperSizeUsed = null) =>
        new()
        {
            Success = true,
            PrinterUsed = printerUsed,
            PagesPrinted = pagesPrinted,
            CopiesPrinted = copiesPrinted,
            PaperSizeUsed = paperSizeUsed
        };
}
