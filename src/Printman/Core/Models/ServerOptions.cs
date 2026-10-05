namespace Printman.Core.Models;

/// <summary>
/// Configuration for the embedded LAN server (`printman serve` / `printman share`).
/// </summary>
public sealed record ServerOptions
{
    public int Port { get; init; } = 5000;
    public string BindAddress { get; init; } = "0.0.0.0";
    public string? Pin { get; init; }
    public bool RequireAuth { get; init; } = true;
    public int MaxUploadMb { get; init; } = 50;
    public int CacheLimitMb { get; init; } = 500;

    /// <summary>
    /// Optional directory where every server print job is written as a file (Print-to-File).
    /// Intended for headless testing against virtual printers (Microsoft Print to PDF / XPS).
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>
    /// When false (`printman share`), only shared network printers are served: no web UI, API or PIN.
    /// </summary>
    public bool EnableWebUi { get; init; } = true;

    public ShareOptions Share { get; init; } = new();
}

/// <summary>
/// Network printer sharing (IPP Everywhere / AirPrint / Mopria) options.
/// </summary>
public sealed record ShareOptions
{
    /// <summary>Opt-in (`share`, `--share`): when false the server is web-only.</summary>
    public bool Enabled { get; init; }

    /// <summary>Printer names (fuzzy matched). Empty means the Windows default printer.</summary>
    public IReadOnlyList<string> Printers { get; init; } = [];

    /// <summary>TCP port for the IPP endpoint (default 631, the standard IPP port).</summary>
    public int IppPort { get; init; } = 631;

    /// <summary>Advertise shared printers via mDNS / DNS-SD (Bonjour).</summary>
    public bool EnableMdns { get; init; } = true;

    /// <summary>Accept IPP requests from non-private source addresses (disabled by default).</summary>
    public bool AllowAnySource { get; init; }

    /// <summary>Maximum size of a single IPP document in MB.</summary>
    public int MaxJobMb { get; init; } = 256;
}
