using System.Drawing.Printing;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services;

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
                    paperSizes.Add(new PaperSizeOption(ps.PaperName, (int)ps.RawKind, ps.Width, ps.Height));
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

        return PrinterMatcher.Find(GetPrinters(), query);
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
