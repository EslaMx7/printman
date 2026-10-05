using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Tests.Fakes;

/// <summary>Configurable <see cref="ISharedPrinterRegistry"/> for IPP tests.</summary>
public sealed class FakeSharedPrinterRegistry : ISharedPrinterRegistry
{
    public List<SharedPrinter> SharedPrinters { get; } = [];
    public Dictionary<string, PrinterInfo?> Capabilities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PrinterStatusInfo?> Statuses { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ConfiguredNames { get; } = [];

    public IReadOnlyList<SharedPrinter> Printers => SharedPrinters;

    public IReadOnlyList<string> Configure(IReadOnlyList<string> printerNames)
    {
        ConfiguredNames.AddRange(printerNames);
        return [];
    }

    public SharedPrinter? Find(string? slug)
    {
        if (SharedPrinters.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return SharedPrinters[0];
        }

        return SharedPrinters.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public PrinterInfo? GetCapabilities(SharedPrinter printer) =>
        Capabilities.TryGetValue(printer.WindowsName, out var info) ? info : null;

    public PrinterStatusInfo? GetStatus(SharedPrinter printer) =>
        Statuses.TryGetValue(printer.WindowsName, out var status) ? status : null;
}
