namespace Printman.Core.Models;

/// <summary>
/// A DNS-SD service instance to advertise over multicast DNS (RFC 6763).
/// </summary>
public sealed record DnsSdService
{
    /// <summary>Human readable instance name, e.g. "Printman - HP LaserJet" (max 63 UTF-8 bytes).</summary>
    public required string InstanceName { get; init; }

    /// <summary>Service type without domain, e.g. "_ipp._tcp".</summary>
    public required string ServiceType { get; init; }

    /// <summary>Subtypes without the "._sub" suffix, e.g. "_universal", "_print".</summary>
    public IReadOnlyList<string> Subtypes { get; init; } = [];

    public required int Port { get; init; }

    /// <summary>TXT record key/value pairs, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Txt { get; init; } = [];
}
