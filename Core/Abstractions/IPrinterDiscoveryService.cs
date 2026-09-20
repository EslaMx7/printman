using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Core.Abstractions;

public interface IPrinterDiscoveryService
{
    /// <summary>
    /// Gets all installed printers on the system with capabilities and status.
    /// </summary>
    IReadOnlyList<PrinterInfo> GetPrinters();

    /// <summary>
    /// Gets the default printer if one exists.
    /// </summary>
    PrinterInfo? GetDefaultPrinter();

    /// <summary>
    /// Finds a printer by query (exact match first, then case-insensitive substring match).
    /// </summary>
    PrinterInfo? FindPrinter(string query);
}
