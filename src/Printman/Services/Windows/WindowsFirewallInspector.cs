using Printman.Core.Abstractions;

namespace Printman.Services.Discovery;

/// <summary>
/// Reads Windows Defender Firewall rules through the HNetCfg.FwPolicy2 COM object (no elevation needed)
/// and suggests the commands to run when inbound printing traffic looks blocked. Never modifies rules.
/// </summary>
public class WindowsFirewallInspector : IFirewallInspector
{
    private const int ProtocolTcp = 6;
    private const int ProtocolUdp = 17;
    private const int ProtocolAny = 256;
    private const int DirectionIn = 1;
    private const int ActionBlock = 0;
    private const int ActionAllow = 1;
    private const int ProfileAll = 0x7FFFFFFF;

    public IReadOnlyList<string> CheckInboundAccess(int tcpPort, bool needsMdns)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return [];
        }

        Verdict tcp, udp;
        int profiles;
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType == null) return [];
            dynamic policy = Activator.CreateInstance(policyType)!;
            profiles = (int)policy.CurrentProfileTypes;

            var rules = new List<Rule>();
            foreach (dynamic r in policy.Rules)
            {
                try
                {
                    rules.Add(new Rule(
                        (bool)r.Enabled,
                        (int)r.Direction,
                        (int)r.Action,
                        (int)r.Protocol,
                        (int)r.Profiles,
                        (string?)r.ApplicationName,
                        (string?)r.LocalPorts));
                }
                catch { /* skip rules with unexpected shapes */ }
            }

            tcp = Evaluate(rules, exePath, ProtocolTcp, tcpPort, profiles);
            udp = needsMdns ? Evaluate(rules, exePath, ProtocolUdp, 5353, profiles) : Verdict.Allowed;
        }
        catch
        {
            return []; // Cannot inspect: stay quiet rather than guess
        }

        if (tcp == Verdict.Allowed && udp == Verdict.Allowed)
        {
            return [];
        }

        var hints = new List<string>();
        if ((profiles & 4) != 0)
        {
            hints.Add("[FIREWALL] This network is set to Public; Windows blocks most inbound traffic. Consider switching it to Private.");
        }

        hints.Add(tcp == Verdict.Blocked || udp == Verdict.Blocked
            ? "[FIREWALL] A firewall rule is blocking printman, so other devices may not see or reach the printer."
            : "[FIREWALL] No firewall rule allows printman yet; accept the Windows prompt or allow it from an elevated terminal:");

        if (tcp != Verdict.Allowed)
        {
            hints.Add($"  netsh advfirewall firewall add rule name=\"Printman IPP\" dir=in action=allow protocol=TCP localport={tcpPort} program=\"{exePath}\" profile=private");
        }
        if (udp != Verdict.Allowed)
        {
            hints.Add($"  netsh advfirewall firewall add rule name=\"Printman mDNS\" dir=in action=allow protocol=UDP localport=5353 program=\"{exePath}\" profile=private");
        }
        return hints;
    }

    private enum Verdict { Allowed, Blocked, NoRule }

    private sealed record Rule(bool Enabled, int Direction, int Action, int Protocol, int Profiles, string? ApplicationName, string? LocalPorts);

    private static Verdict Evaluate(List<Rule> rules, string exePath, int protocol, int port, int currentProfiles)
    {
        bool allowed = false;
        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.Direction != DirectionIn) continue;
            if (rule.Protocol != protocol && rule.Protocol != ProtocolAny) continue;
            if ((rule.Profiles & currentProfiles) == 0 && rule.Profiles != ProfileAll) continue;
            if (!PortMatches(rule.LocalPorts, port)) continue;

            bool forUs = string.IsNullOrEmpty(rule.ApplicationName) ||
                         string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(rule.ApplicationName)), exePath, StringComparison.OrdinalIgnoreCase);
            if (!forUs) continue;

            // Block rules win over allow rules in Windows Firewall
            if (rule.Action == ActionBlock && !string.IsNullOrEmpty(rule.ApplicationName)) return Verdict.Blocked;
            if (rule.Action == ActionAllow) allowed = true;
        }
        return allowed ? Verdict.Allowed : Verdict.NoRule;
    }

    private static bool PortMatches(string? localPorts, int port)
    {
        if (string.IsNullOrWhiteSpace(localPorts) || localPorts.Trim() == "*") return true;

        foreach (var part in localPorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-');
            if (range.Length == 2 && int.TryParse(range[0], out int lo) && int.TryParse(range[1], out int hi) && port >= lo && port <= hi) return true;
            if (int.TryParse(part, out int single) && single == port) return true;
        }
        return false;
    }
}
