using Printman.Core.Models;

namespace Printman.Core.Abstractions;

/// <summary>
/// Advertises services on the local network (mDNS / DNS-SD) so devices can discover them.
/// </summary>
public interface IServiceAdvertiser
{
    /// <summary>Host name the services resolve to, e.g. "desktop-printman.local".</summary>
    string HostName { get; }

    /// <summary>Non-fatal problems found while starting (e.g. interfaces that could not be used).</summary>
    IReadOnlyList<string> Warnings { get; }

    /// <summary>Opens the network sockets and starts announcing in the background.</summary>
    Task StartAsync(IReadOnlyList<DnsSdService> services, CancellationToken ct);

    /// <summary>Withdraws the services (goodbye packets) and closes the sockets.</summary>
    Task StopAsync();
}
