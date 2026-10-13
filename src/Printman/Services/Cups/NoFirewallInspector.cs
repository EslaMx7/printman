using Printman.Core.Abstractions;

namespace Printman.Services.Cups;

/// <summary>
/// Linux / macOS: firewall front-ends vary (ufw, firewalld, nftables, the macOS application firewall)
/// and usually need root to inspect, so no hints are produced.
/// </summary>
public sealed class NoFirewallInspector : IFirewallInspector
{
    public IReadOnlyList<string> CheckInboundAccess(int tcpPort, bool needsMdns) => [];
}
