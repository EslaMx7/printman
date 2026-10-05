using Printman.Core.Models;

namespace Printman.Core.Abstractions;

/// <summary>
/// Resolves which Windows printers are shared on the network and caches their capabilities and status.
/// </summary>
public interface ISharedPrinterRegistry
{
    IReadOnlyList<SharedPrinter> Printers { get; }

    /// <summary>
    /// Resolves printer names (fuzzy matched); an empty list shares the default printer.
    /// Returns the names that could not be resolved.
    /// </summary>
    IReadOnlyList<string> Configure(IReadOnlyList<string> printerNames);

    /// <summary>Finds a shared printer by slug; a null or empty slug returns the first shared printer.</summary>
    SharedPrinter? Find(string? slug);

    /// <summary>Cached printer capabilities (refreshed periodically).</summary>
    PrinterInfo? GetCapabilities(SharedPrinter printer);

    /// <summary>Cached hardware status (refreshed every few seconds).</summary>
    PrinterStatusInfo? GetStatus(SharedPrinter printer);
}
