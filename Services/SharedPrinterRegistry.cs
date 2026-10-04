using System.Security.Cryptography;
using System.Text;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services;

public class SharedPrinterRegistry(
    IPrinterDiscoveryService printerDiscovery,
    IPrintQueueService queueService) : ISharedPrinterRegistry
{
    private static readonly TimeSpan CapabilitiesTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StatusTtl = TimeSpan.FromSeconds(3);

    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IPrintQueueService _queueService = queueService;
    private readonly object _sync = new();
    private readonly Dictionary<string, (PrinterInfo? Info, DateTime At)> _capabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (PrinterStatusInfo? Status, DateTime At)> _status = new(StringComparer.OrdinalIgnoreCase);
    private List<SharedPrinter> _printers = [];

    public IReadOnlyList<SharedPrinter> Printers => _printers;

    public IReadOnlyList<string> Configure(IReadOnlyList<string> printerNames)
    {
        var unresolved = new List<string>();
        var resolved = new List<PrinterInfo>();

        if (printerNames.Count == 0)
        {
            var defaultPrinter = _printerDiscovery.GetDefaultPrinter();
            if (defaultPrinter != null)
            {
                resolved.Add(defaultPrinter);
            }
        }
        else
        {
            foreach (var query in printerNames)
            {
                var printer = _printerDiscovery.FindPrinter(query);
                if (printer == null)
                {
                    unresolved.Add(query);
                }
                else if (!resolved.Any(p => string.Equals(p.Name, printer.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    resolved.Add(printer);
                }
            }
        }

        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shared = new List<SharedPrinter>();
        foreach (var info in resolved)
        {
            var slug = MakeSlug(info.Name);
            var candidate = slug;
            for (int n = 2; !slugs.Add(candidate); n++)
            {
                candidate = $"{slug}-{n}";
            }

            shared.Add(new SharedPrinter
            {
                WindowsName = info.Name,
                Slug = candidate,
                // DNS-SD instance names are limited to 63 UTF-8 bytes
                DisplayName = SharedPrinter.TruncateUtf8($"Printman - {info.Name}", 63),
                Uuid = MakeUuid(info.Name)
            });

            lock (_sync)
            {
                _capabilities[info.Name] = (info, DateTime.UtcNow);
            }
        }

        _printers = shared;
        return unresolved;
    }

    public SharedPrinter? Find(string? slug)
    {
        var printers = _printers;
        if (string.IsNullOrWhiteSpace(slug))
        {
            return printers.FirstOrDefault();
        }
        return printers.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public PrinterInfo? GetCapabilities(SharedPrinter printer)
    {
        lock (_sync)
        {
            if (_capabilities.TryGetValue(printer.WindowsName, out var cached) && DateTime.UtcNow - cached.At < CapabilitiesTtl)
            {
                return cached.Info;
            }
        }

        PrinterInfo? info = null;
        try
        {
            info = _printerDiscovery.GetPrinters()
                .FirstOrDefault(p => string.Equals(p.Name, printer.WindowsName, StringComparison.OrdinalIgnoreCase));
        }
        catch { }

        lock (_sync)
        {
            // Keep the last known capabilities if the printer is temporarily unavailable
            if (info == null && _capabilities.TryGetValue(printer.WindowsName, out var previous))
            {
                info = previous.Info;
            }
            _capabilities[printer.WindowsName] = (info, DateTime.UtcNow);
        }
        return info;
    }

    public PrinterStatusInfo? GetStatus(SharedPrinter printer)
    {
        lock (_sync)
        {
            if (_status.TryGetValue(printer.WindowsName, out var cached) && DateTime.UtcNow - cached.At < StatusTtl)
            {
                return cached.Status;
            }
        }

        PrinterStatusInfo? status = null;
        try
        {
            status = _queueService.GetPrinterStatus(printer.WindowsName);
        }
        catch { }

        lock (_sync)
        {
            _status[printer.WindowsName] = (status, DateTime.UtcNow);
        }
        return status;
    }

    private static string MakeSlug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug.Length == 0 ? "printer" : slug;
    }

    private static Guid MakeUuid(string printerName)
    {
        // Stable per machine + printer so clients keep recognising the same printer across restarts
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"printman|{Environment.MachineName}|{printerName}"));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5 (name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }
}
