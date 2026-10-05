using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services.Ipp;

/// <summary>
/// Builds IPP Everywhere / AirPrint printer description attributes for a shared printer.
/// </summary>
public class IppPrinterAttributeBuilder(
    ISharedPrinterRegistry registry,
    IIppJobStore jobStore,
    IppDocumentFormats formats,
    IppServerSettings settings)
{
    public const int RasterDpi = 300;
    private const int MarginHmm = 423; // 4.23 mm: conservative hard margin (no borderless support)

    private static readonly int[] SupportedOperations =
    [
        IppOperation.PrintJob, IppOperation.ValidateJob, IppOperation.CreateJob, IppOperation.SendDocument,
        IppOperation.CancelJob, IppOperation.GetJobAttributes, IppOperation.GetJobs, IppOperation.GetPrinterAttributes,
        IppOperation.CancelMyJobs, IppOperation.CloseJob, IppOperation.IdentifyPrinter
    ];

    private readonly ISharedPrinterRegistry _registry = registry;
    private readonly IIppJobStore _jobStore = jobStore;
    private readonly IppDocumentFormats _formats = formats;
    private readonly IppServerSettings _settings = settings;

    /// <summary>Apple URF capability tokens, shared with the DNS-SD TXT record.</summary>
    public static IReadOnlyList<string> GetUrfSupported(PrinterInfo? info)
    {
        var tokens = new List<string> { "V1.4", "CP1", "PQ4", $"RS{RasterDpi}", "SRGB24", "W8", "IS1", "MT1" };
        if (info?.CanDuplex == true) tokens.Add("DM1");
        return tokens;
    }

    /// <summary>
    /// Builds the printer attribute group. <paramref name="requested"/> follows "requested-attributes":
    /// null or "all" returns everything except media-col-database, which must be named explicitly.
    /// </summary>
    public IppAttributeGroup Build(SharedPrinter printer, string printerUri, string webUri, IReadOnlyCollection<string>? requested)
    {
        var info = _registry.GetCapabilities(printer);
        var status = _registry.GetStatus(printer);
        var media = PwgMediaMapper.Map(info?.SupportedPaperSizes ?? []);
        var defaultMedia = PwgMediaMapper.PickDefault(media);
        bool color = info?.SupportsColor ?? false;
        bool duplex = info?.CanDuplex ?? false;

        var g = new IppAttributeGroup(IppTag.PrinterAttributes);

        // Identity & protocol
        g.AddUri("printer-uri-supported", printerUri);
        g.AddKeyword("uri-security-supported", "none");
        g.AddKeyword("uri-authentication-supported", "none");
        g.AddName("printer-name", printer.DisplayName);
        g.AddName("printer-dns-sd-name", printer.DisplayName);
        g.AddText("printer-info", printer.DisplayName);
        g.AddText("printer-location", Environment.MachineName);
        g.AddText("printer-make-and-model", $"Printman {printer.WindowsName}");
        g.AddUri("printer-more-info", webUri);
        g.AddUri("printer-uuid", $"urn:uuid:{printer.Uuid}");
        g.AddText("printer-device-id", BuildDeviceId(printer));
        g.AddKeyword("printer-kind", "document", "photo");
        g.AddKeyword("ipp-versions-supported", "1.1", "2.0");
        g.AddKeywords("ipp-features-supported", _formats.SupportsUrf ? ["ipp-everywhere", "airprint-1.4"] : ["ipp-everywhere"]);
        g.AddEnum("operations-supported", SupportedOperations);
        g.AddCharset("charset-configured", "utf-8");
        g.AddCharset("charset-supported", "utf-8");
        g.AddLanguage("natural-language-configured", "en");
        g.AddLanguage("generated-natural-language-supported", "en");
        g.AddKeyword("compression-supported", "none");
        g.AddKeyword("pdl-override-supported", "attempted");
        g.AddBoolean("multiple-document-jobs-supported", false);
        g.AddInteger("multiple-operation-time-out", 60);
        g.AddKeyword("multiple-operation-time-out-action", "abort-job");
        g.AddBoolean("job-ids-supported", true);
        g.AddKeyword("which-jobs-supported", "completed", "not-completed", "all");
        g.AddKeyword("identify-actions-default", "display");
        g.AddKeyword("identify-actions-supported", "display");
        g.AddKeyword("printer-get-attributes-supported", "document-format");
        g.AddKeyword("job-creation-attributes-supported",
            "copies", "sides", "print-color-mode", "media", "media-col", "orientation-requested",
            "page-ranges", "print-scaling", "print-quality", "printer-resolution", "output-bin",
            "finishings", "number-up", "print-content-optimize", "print-rendering-intent");

        // State
        AddState(g, printer, status);

        // Document formats
        g.AddMimeTypes("document-format-supported", _formats.SupportedMimeTypes.Append(IppDocumentFormats.OctetStream));
        g.Add("document-format-default", IppTag.MimeMediaType, IppDocumentFormats.OctetStream);
        g.AddKeyword("pdf-versions-supported", "adobe-1.3", "adobe-1.4", "adobe-1.5", "adobe-1.6", "adobe-1.7", "iso-32000-1_2008");
        if (_formats.SupportsPwgRaster)
        {
            g.AddResolution("pwg-raster-document-resolution-supported", new IppResolution(RasterDpi, RasterDpi));
            g.AddKeyword("pwg-raster-document-type-supported", "sgray_8", "srgb_8");
            g.AddKeyword("pwg-raster-document-sheet-back", "normal");
        }
        if (_formats.SupportsUrf)
        {
            g.AddKeywords("urf-supported", GetUrfSupported(info));
        }

        // Job template
        g.AddInteger("copies-default", 1);
        g.AddRange("copies-supported", 1, 99);
        g.AddKeyword("sides-default", "one-sided");
        g.AddKeywords("sides-supported", duplex
            ? ["one-sided", "two-sided-long-edge", "two-sided-short-edge"]
            : ["one-sided"]);
        g.AddKeyword("print-color-mode-default", color ? "auto" : "monochrome");
        g.AddKeywords("print-color-mode-supported", color ? ["auto", "monochrome", "color"] : ["auto", "monochrome"]);
        g.AddBoolean("color-supported", color);
        g.AddKeyword("output-bin-default", "face-down");
        g.AddKeyword("output-bin-supported", "face-down");
        g.AddEnum("print-quality-default", 4);
        g.AddEnum("print-quality-supported", 3, 4, 5);
        g.AddResolution("printer-resolution-default", new IppResolution(RasterDpi, RasterDpi));
        g.AddResolution("printer-resolution-supported", new IppResolution(RasterDpi, RasterDpi));
        g.AddEnum("orientation-requested-default", 3);
        g.AddEnum("orientation-requested-supported", 3, 4, 5, 6);
        g.AddBoolean("page-ranges-supported", true);
        g.AddKeyword("print-scaling-default", "auto");
        g.AddKeyword("print-scaling-supported", "auto", "auto-fit", "fit", "fill", "none");
        g.AddEnum("finishings-default", 3);
        g.AddEnum("finishings-supported", 3);
        g.AddInteger("number-up-default", 1);
        g.AddInteger("number-up-supported", 1);
        g.AddKeyword("job-sheets-default", "none");
        g.AddKeyword("job-sheets-supported", "none");
        g.AddKeyword("print-content-optimize-default", "auto");
        g.AddKeyword("print-content-optimize-supported", "auto");
        g.AddKeyword("print-rendering-intent-default", "auto");
        g.AddKeyword("print-rendering-intent-supported", "auto");

        // Media
        g.AddKeyword("media-default", defaultMedia.Name);
        g.AddKeywords("media-supported", media.Select(m => m.Media.Name));
        g.AddKeywords("media-ready", [defaultMedia.Name]);
        g.AddCollection("media-size-supported", media.Select(m => SizeCollection(m.Media)).ToArray());
        g.AddCollection("media-col-default", MediaCol(defaultMedia));
        g.AddCollection("media-col-ready", MediaCol(defaultMedia));
        g.AddKeyword("media-col-supported",
            "media-size", "media-size-name", "media-top-margin", "media-bottom-margin",
            "media-left-margin", "media-right-margin", "media-source", "media-type");
        g.AddCollection("media-col-database", media.Select(m => MediaCol(m.Media)).ToArray());
        g.AddKeyword("media-source-supported", "auto");
        g.AddKeyword("media-type-supported", "stationery");
        g.AddInteger("media-top-margin-supported", MarginHmm);
        g.AddInteger("media-bottom-margin-supported", MarginHmm);
        g.AddInteger("media-left-margin-supported", MarginHmm);
        g.AddInteger("media-right-margin-supported", MarginHmm);

        return Filter(g, requested);
    }

    private void AddState(IppAttributeGroup g, SharedPrinter printer, PrinterStatusInfo? status)
    {
        var reasons = new List<string>();
        bool stopped = false;

        if (status != null)
        {
            if (status.IsPaperJam) { reasons.Add("media-jam-error"); stopped = true; }
            if (status.IsOutOfPaper) { reasons.Add("media-empty-error"); stopped = true; }
            if (status.IsDoorOpen) { reasons.Add("door-open-error"); stopped = true; }
            if (status.IsPaused) { reasons.Add("paused"); stopped = true; }
            // Windows' offline detection is unreliable (sleeping WSD printers), so it is informational only
            if (!status.IsOnline) reasons.Add("offline-report");
            if (status.HasError && reasons.Count == 0) { reasons.Add("other-error"); stopped = true; }
        }

        int active = _jobStore.ActiveCount(printer.Slug);
        int state = stopped ? IppPrinterState.Stopped
            : active > 0 ? IppPrinterState.Processing
            : IppPrinterState.Idle;

        g.AddEnum("printer-state", state);
        g.AddKeywords("printer-state-reasons", reasons.Count > 0 ? reasons : ["none"]);
        g.AddText("printer-state-message", status?.StatusText ?? "Ready");
        g.AddBoolean("printer-is-accepting-jobs", active < _settings.MaxPendingJobs);
        g.AddInteger("queued-job-count", active);
        g.AddInteger("printer-up-time", _settings.UpTimeSeconds);
        g.AddInteger("printer-state-change-time", 1);
        g.AddInteger("printer-config-change-time", 1);
        g.AddDateTime("printer-current-time", DateTimeOffset.Now);
    }

    private static IppCollection SizeCollection(PwgMedia media)
    {
        var c = new IppCollection();
        c.AddInteger("x-dimension", media.WidthHmm);
        c.AddInteger("y-dimension", media.HeightHmm);
        return c;
    }

    private static IppCollection MediaCol(PwgMedia media)
    {
        var c = new IppCollection();
        c.AddCollection("media-size", SizeCollection(media));
        c.AddKeyword("media-size-name", media.Name);
        c.AddInteger("media-top-margin", MarginHmm);
        c.AddInteger("media-bottom-margin", MarginHmm);
        c.AddInteger("media-left-margin", MarginHmm);
        c.AddInteger("media-right-margin", MarginHmm);
        c.AddKeyword("media-source", "auto");
        c.AddKeyword("media-type", "stationery");
        return c;
    }

    private string BuildDeviceId(SharedPrinter printer)
    {
        var commands = new List<string>();
        if (_formats.Supports(IppDocumentFormats.Pdf)) commands.Add("PDF");
        if (_formats.SupportsPwgRaster) commands.Add("PWGRaster");
        if (_formats.SupportsUrf) commands.Add("URF");
        if (_formats.Supports(IppDocumentFormats.Jpeg)) commands.Add("JPEG");
        var model = printer.WindowsName.Replace(';', ' ').Replace(':', ' ');
        return $"MFG:Printman;MDL:{model};CMD:{string.Join(',', commands)};";
    }

    private static IppAttributeGroup Filter(IppAttributeGroup all, IReadOnlyCollection<string>? requested)
    {
        bool everything = requested == null || requested.Count == 0 ||
                          requested.Contains("all") || requested.Contains("printer-description") || requested.Contains("job-template");

        var filtered = new IppAttributeGroup(IppTag.PrinterAttributes);
        foreach (var attr in all.Attributes)
        {
            bool include = attr.Name == "media-col-database"
                ? requested?.Contains("media-col-database") == true
                : everything || requested!.Contains(attr.Name);

            if (include)
            {
                filtered.Attributes.Add(attr);
            }
        }
        return filtered;
    }
}
