namespace Printman.Core.Abstractions;

/// <summary>
/// Read-only check of whether inbound network traffic can reach this process.
/// Never changes firewall configuration.
/// </summary>
public interface IFirewallInspector
{
    /// <summary>
    /// Returns human readable hints (including the commands to run) when inbound access looks blocked;
    /// empty when everything looks allowed or the state cannot be determined confidently.
    /// </summary>
    IReadOnlyList<string> CheckInboundAccess(int tcpPort, bool needsMdns);
}
