using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services.Ipp;

/// <summary>
/// Builds the _ipp._tcp DNS-SD records (Bonjour Printing / AirPrint / Mopria TXT keys) for shared printers.
/// </summary>
public class IppDnsSdServiceFactory(
    ISharedPrinterRegistry registry,
    IppDocumentFormats formats,
    IppServerSettings settings) : IDnsSdServiceFactory
{
    private readonly ISharedPrinterRegistry _registry = registry;
    private readonly IppDocumentFormats _formats = formats;
    private readonly IppServerSettings _settings = settings;

    public IReadOnlyList<DnsSdService> Create(IReadOnlyList<SharedPrinter> printers)
    {
        var services = new List<DnsSdService>();
        foreach (var printer in printers)
        {
            var info = _registry.GetCapabilities(printer);
            var media = PwgMediaMapper.Map(info?.SupportedPaperSizes ?? []);
            bool largeFormat = media.Any(m => m.Media.WidthHmm >= 27940 && m.Media.HeightHmm >= 42000);

            var txt = new List<KeyValuePair<string, string>>
            {
                new("txtvers", "1"),
                new("qtotal", "1"),
                new("rp", printer.ResourcePath),
                new("ty", Limit($"Printman {printer.WindowsName}")),
                new("product", Limit($"(Printman {printer.WindowsName})")),
                new("note", Limit(Environment.MachineName)),
                new("UUID", printer.Uuid.ToString()),
                new("pdl", string.Join(',', _formats.SupportedMimeTypes.Append(IppDocumentFormats.OctetStream))),
                new("Color", info?.SupportsColor == true ? "T" : "F"),
                new("Duplex", info?.CanDuplex == true ? "T" : "F"),
                new("Copies", "T"),
                new("Collate", "F"),
                new("Staple", "F"),
                new("Punch", "F"),
                new("Bind", "F"),
                new("Sort", "F"),
                new("Scan", "F"),
                new("Fax", "F"),
                new("PaperMax", largeFormat ? "tabloid-A3" : "legal-A4"),
                new("kind", "document,photo"),
                new("priority", "50"),
                new("air", "none")
            };

            if (!string.IsNullOrEmpty(_settings.MdnsHostName))
            {
                txt.Add(new("adminurl", $"http://{_settings.MdnsHostName}:{_settings.WebPort}/"));
            }

            var subtypes = new List<string> { "_print" };
            if (_formats.SupportsUrf)
            {
                // AirPrint only lists printers advertising the _universal subtype with URF capabilities
                txt.Add(new("URF", string.Join(',', IppPrinterAttributeBuilder.GetUrfSupported(info))));
                subtypes.Insert(0, "_universal");
            }

            services.Add(new DnsSdService
            {
                InstanceName = printer.DisplayName,
                ServiceType = "_ipp._tcp",
                Subtypes = subtypes,
                Port = _settings.IppPort,
                Txt = txt
            });
        }
        return services;
    }

    // Each TXT string (key=value) must fit in 255 bytes
    private static string Limit(string value) => SharedPrinter.TruncateUtf8(value, 200);
}
