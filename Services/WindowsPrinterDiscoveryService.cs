using System.Drawing.Printing;
using OhMyPrinter.Core.Abstractions;
using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Services;

public class WindowsPrinterDiscoveryService : IPrinterDiscoveryService
{
    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        var result = new List<PrinterInfo>();
        var defaultPrinterName = GetDefaultPrinterName();

        foreach (string printerName in PrinterSettings.InstalledPrinters)
        {
            try
            {
                var settings = new PrinterSettings { PrinterName = printerName };
                if (!settings.IsValid)
                {
                    continue;
                }

                var paperSizes = new List<PaperSizeOption>();
                foreach (PaperSize ps in settings.PaperSizes)
                {
                    paperSizes.Add(PaperSizeOption.FromDrawing(ps));
                }

                var resolutions = new List<string>();
                foreach (PrinterResolution res in settings.PrinterResolutions)
                {
                    resolutions.Add(res.Kind == PrinterResolutionKind.Custom
                        ? $"{res.X}x{res.Y} DPI"
                        : res.Kind.ToString());
                }

                bool isDefault = !string.IsNullOrEmpty(defaultPrinterName) &&
                                 string.Equals(printerName, defaultPrinterName, StringComparison.OrdinalIgnoreCase);

                result.Add(new PrinterInfo
                {
                    Name = printerName,
                    IsDefault = isDefault,
                    Status = "Ready",
                    SupportsColor = settings.SupportsColor,
                    CanDuplex = settings.CanDuplex,
                    SupportedPaperSizes = paperSizes,
                    SupportedResolutions = resolutions
                });
            }
            catch
            {
                // If query on a specific network printer fails or hangs, skip or add basic entry
                result.Add(new PrinterInfo
                {
                    Name = printerName,
                    IsDefault = string.Equals(printerName, defaultPrinterName, StringComparison.OrdinalIgnoreCase),
                    Status = "Unknown"
                });
            }
        }

        return result;
    }

    public PrinterInfo? GetDefaultPrinter()
    {
        var printers = GetPrinters();
        return printers.FirstOrDefault(p => p.IsDefault) ?? printers.FirstOrDefault();
    }

    public PrinterInfo? FindPrinter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetDefaultPrinter();
        }

        var printers = GetPrinters();
        var trimmedQuery = query.Trim();

        // 1. Exact match
        var exact = printers.FirstOrDefault(p =>
            string.Equals(p.Name, trimmedQuery, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 2. Starts with
        var starts = printers.FirstOrDefault(p =>
            p.Name.StartsWith(trimmedQuery, StringComparison.OrdinalIgnoreCase));
        if (starts != null) return starts;

        // 3. Substring match
        var contains = printers.FirstOrDefault(p =>
            p.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase));
        if (contains != null) return contains;

        // 4. Token-based match (e.g. "HP Laser" matches "HP LaserJet Professional P1102")
        var tokens = trimmedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > 0)
        {
            var tokenMatch = printers.FirstOrDefault(p =>
                tokens.All(t => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase)));
            if (tokenMatch != null) return tokenMatch;
        }

        return null;
    }

    private static string? GetDefaultPrinterName()
    {
        try
        {
            var defaultSettings = new PrinterSettings();
            return defaultSettings.IsDefaultPrinter ? defaultSettings.PrinterName : null;
        }
        catch
        {
            return null;
        }
    }
}
