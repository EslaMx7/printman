using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Services.Ipp;

namespace Printman.Services.Cups;

/// <summary>
/// Lists CUPS print queues (CUPS-Get-Printers) with their media, duplex and color capabilities.
/// </summary>
public sealed class CupsPrinterDiscoveryService(
    CupsClient cups,
    Func<string, string?> getEnvironmentVariable,
    string? lpoptionsPath,
    TextWriter diagnostics) : IPrinterDiscoveryService
{
    public CupsPrinterDiscoveryService(CupsClient cups) : this(
        cups,
        Environment.GetEnvironmentVariable,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cups", "lpoptions"),
        Console.Error)
    {
    }

    private static readonly string[] RequestedAttributes =
    [
        "printer-name", "printer-info", "printer-make-and-model", "device-uri",
        "printer-state", "printer-is-accepting-jobs",
        "color-supported", "sides-supported", "media-supported", "printer-resolution-supported"
    ];

    private readonly CupsClient _cups = cups;
    private readonly Func<string, string?> _getEnvironmentVariable = getEnvironmentVariable;
    private readonly string? _lpoptionsPath = lpoptionsPath;
    private readonly TextWriter _diagnostics = diagnostics;
    private int _warned;

    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        IppMessage response;
        try
        {
            response = _cups.Send(CupsClient.CupsGetPrinters, "/", op => op.AddKeywords("requested-attributes", RequestedAttributes));
        }
        catch (CupsException ex) when (ex.IppStatus == IppStatus.NotFound)
        {
            return []; // no queues configured
        }
        catch (CupsException ex)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                _diagnostics.WriteLine($"Warning: {ex.Message}");
            }
            return [];
        }

        var defaultName = GetDefaultPrinterName();
        var result = new List<PrinterInfo>();

        foreach (var group in response.Groups.Where(g => g.Tag == IppTag.PrinterAttributes))
        {
            var name = group.Get("printer-name")?.First?.AsString();
            if (string.IsNullOrEmpty(name)) continue;

            var paperSizes = new List<PaperSizeOption>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var keyword in Strings(group, "media-supported"))
            {
                if (PwgMediaMapper.FromPwgName(keyword) is not PaperSizeOption paper) continue;
                // Keep names unique so a size can be picked by name (--paper, web UI)
                paperSizes.Add(seenNames.Add(paper.Name) ? paper : paper with { Name = keyword });
            }

            var resolutions = (group.Get("printer-resolution-supported")?.Values ?? [])
                .Select(v => v.Value)
                .OfType<IppResolution>()
                .Select(r => $"{r.CrossFeed}x{r.Feed} DPI")
                .Distinct()
                .ToList();

            int state = group.Get("printer-state")?.First?.AsInt() ?? IppPrinterState.Idle;
            bool accepting = group.Get("printer-is-accepting-jobs")?.First?.AsBool() ?? true;

            result.Add(new PrinterInfo
            {
                Name = name,
                IsDefault = string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase),
                Status = !accepting ? "Not accepting jobs"
                    : state == IppPrinterState.Stopped ? "Paused"
                    : state == IppPrinterState.Processing ? "Printing"
                    : "Ready",
                PortName = group.Get("device-uri")?.First?.AsString(),
                DriverName = group.Get("printer-make-and-model")?.First?.AsString(),
                SupportsColor = group.Get("color-supported")?.First?.AsBool() ?? false,
                CanDuplex = Strings(group, "sides-supported").Any(s => s.StartsWith("two-sided", StringComparison.Ordinal)),
                SupportedPaperSizes = paperSizes,
                SupportedResolutions = resolutions
            });
        }

        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
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

    /// <summary>Same precedence as lp: LPDEST, PRINTER, ~/.cups/lpoptions, then the server default.</summary>
    private string? GetDefaultPrinterName()
    {
        foreach (var variable in new[] { "LPDEST", "PRINTER" })
        {
            var value = _getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && value != "lp") return StripInstance(value);
        }

        if (_lpoptionsPath != null && File.Exists(_lpoptionsPath))
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_lpoptionsPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lines = [];
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("Default ", StringComparison.OrdinalIgnoreCase))
                {
                    var dest = line[8..].Trim().Split(' ', 2)[0];
                    if (dest.Length > 0) return StripInstance(dest);
                }
            }
        }

        try
        {
            var response = _cups.Send(CupsClient.CupsGetDefault, "/", op => op.AddKeyword("requested-attributes", "printer-name"));
            return response.Group(IppTag.PrinterAttributes)?.Get("printer-name")?.First?.AsString();
        }
        catch (CupsException)
        {
            return null; // no default configured
        }
    }

    private static string StripInstance(string destination)
    {
        int slash = destination.IndexOf('/');
        return slash > 0 ? destination[..slash] : destination;
    }

    private static IEnumerable<string> Strings(IppAttributeList group, string name) =>
        (group.Get(name)?.Values ?? []).Select(v => v.AsString()).OfType<string>();
}
