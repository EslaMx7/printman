using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Tests.Fakes;

/// <summary>Configurable <see cref="IPrinterDiscoveryService"/> for unit tests.</summary>
public sealed class FakePrinterDiscoveryService : IPrinterDiscoveryService
{
    public List<PrinterInfo> Printers { get; } = [];
    public PrinterInfo? DefaultPrinter { get; set; }

    /// <summary>When set, <see cref="FindPrinter"/> delegates to this instead of substring matching.</summary>
    public Func<string, PrinterInfo?>? FindOverride { get; set; }

    public IReadOnlyList<PrinterInfo> GetPrinters() => Printers;

    public PrinterInfo? GetDefaultPrinter() => DefaultPrinter ?? Printers.FirstOrDefault(p => p.IsDefault);

    public PrinterInfo? FindPrinter(string query)
    {
        if (FindOverride is not null)
        {
            return FindOverride(query);
        }

        return Printers.FirstOrDefault(p => string.Equals(p.Name, query, StringComparison.OrdinalIgnoreCase))
            ?? Printers.FirstOrDefault(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
