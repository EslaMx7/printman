using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services.Discovery;

/// <summary>
/// Minimal multicast DNS responder (RFC 6762) publishing DNS-SD services (RFC 6763) over IPv4.
/// Shares UDP port 5353 with other responders on the machine (Windows DNS Client, Bonjour, browsers).
/// </summary>
public sealed class MdnsResponder : IServiceAdvertiser, IDisposable
{
    private const int MdnsPort = 5353;
    private const uint SharedTtl = 4500;
    private const uint UniqueTtl = 120;
    private const uint LegacyUnicastMaxTtl = 10;
    private const string MetaQuery = "_services._dns-sd._udp.local";
    private static readonly IPAddress MulticastGroup = IPAddress.Parse("224.0.0.251");
    private static readonly IPEndPoint MulticastEndpoint = new(MulticastGroup, MdnsPort);

    private readonly object _sync = new();
    private readonly List<string> _warnings = [];
    private readonly SemaphoreSlim _rebuildLock = new(1, 1);
    private List<Endpoint> _endpoints = [];
    private List<DnsSdService> _services = [];
    private CancellationTokenSource? _cts;
    private string _hostLabel;
    private bool _active;      // answering queries (after probing)
    private bool _probing;
    private readonly HashSet<string> _conflicts = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _rebuildTimer;

    public MdnsResponder()
    {
        _hostLabel = MakeHostLabel(Environment.MachineName);
    }

    public string HostName => $"{_hostLabel}.local";
    public IReadOnlyList<string> Warnings => _warnings;

    private sealed record Endpoint(Socket Socket, IPAddress Address, string InterfaceName, int InterfaceIndex);

    // ---------------------------------------------------------------- Lifecycle

    public Task StartAsync(IReadOnlyList<DnsSdService> services, CancellationToken ct)
    {
        lock (_sync)
        {
            _services = services.ToList();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        OpenEndpoints();
        if (_endpoints.Count == 0)
        {
            throw new InvalidOperationException("no usable network interface could join the mDNS multicast group");
        }

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        _ = Task.Run(() => ProbeAndAnnounceAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        _rebuildTimer?.Dispose();

        if (_active)
        {
            // Goodbye: same records with TTL 0 so caches drop them immediately
            foreach (var ep in _endpoints)
            {
                var goodbye = DnsMessage.Response();
                goodbye.Answers.AddRange(BuildRecords(ep.Address).Select(r => r.WithTtl(0)));
                await SendAsync(ep, goodbye, MulticastEndpoint);
            }
        }

        _active = false;
        _cts?.Cancel();
        CloseEndpoints();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        CloseEndpoints();
        _rebuildTimer?.Dispose();
    }

    private void OpenEndpoints()
    {
        var endpoints = new List<Endpoint>();
        foreach (var (address, name, index) in GetMulticastInterfaces())
        {
            Socket? socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.ExclusiveAddressUse = false;
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                if (OperatingSystem.IsWindows())
                {
                    DisableConnectionResetReporting(socket);

                    // Binding to the interface address keeps traffic per-interface (same approach as Bonjour on Windows)
                    socket.Bind(new IPEndPoint(address, MdnsPort));
                }
                else
                {
                    // Linux / macOS only deliver multicast to sockets bound to the wildcard address; packets are
                    // attributed to an interface by IP_PKTINFO in the receive loop instead.
                    // macOS' mDNSResponder holds the port with SO_REUSEPORT, so ours must set it too.
                    if (OperatingSystem.IsMacOS())
                    {
                        const int SolSocket = 0xffff, SoReusePort = 0x0200;
                        socket.SetRawSocketOption(SolSocket, SoReusePort, BitConverter.GetBytes(1));
                    }
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
                    socket.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
                }
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MulticastGroup, address));
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

                var ep = new Endpoint(socket, address, name, index);
                endpoints.Add(ep);
                _ = Task.Run(() => ReceiveLoopAsync(ep, _cts!.Token));
            }
            catch (SocketException ex)
            {
                socket?.Dispose();
                lock (_sync)
                {
                    _warnings.Add($"mDNS unavailable on '{name}' ({address}): {ex.Message}");
                }
            }
        }

        _endpoints = endpoints;
    }

    private void CloseEndpoints()
    {
        foreach (var ep in _endpoints)
        {
            try { ep.Socket.Dispose(); } catch { }
        }
        _endpoints = [];
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        // Debounce: adapters often report several changes in a row
        _rebuildTimer?.Dispose();
        _rebuildTimer = new System.Threading.Timer(_ => _ = RebuildAsync(), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
    }

    private async Task RebuildAsync()
    {
        var token = _cts?.Token ?? CancellationToken.None;
        if (token.IsCancellationRequested) return;

        await _rebuildLock.WaitAsync(token);
        try
        {
            var previous = _endpoints;
            _endpoints = [];
            foreach (var ep in previous)
            {
                try { ep.Socket.Dispose(); } catch { }
            }

            OpenEndpoints();
            if (_active)
            {
                await AnnounceAsync(token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _rebuildLock.Release();
        }
    }

    // ---------------------------------------------------------------- Probing & announcing

    private async Task ProbeAndAnnounceAsync(CancellationToken ct)
    {
        try
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                lock (_sync) { _conflicts.Clear(); }
                _probing = true;

                // Three probes, 250 ms apart (RFC 6762 §8.1)
                await Task.Delay(Random.Shared.Next(0, 250), ct);
                for (int i = 0; i < 3; i++)
                {
                    foreach (var ep in _endpoints)
                    {
                        await SendAsync(ep, BuildProbe(ep.Address), MulticastEndpoint);
                    }
                    await Task.Delay(250, ct);
                }

                _probing = false;
                if (!ResolveConflicts())
                {
                    break;
                }
            }

            _active = true;
            await AnnounceAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [mDNS] Advertising failed: {ex.Message}");
            Console.ResetColor();
        }
    }

    private async Task AnnounceAsync(CancellationToken ct)
    {
        // At least two unsolicited responses, one second apart (RFC 6762 §8.3)
        for (int i = 0; i < 3; i++)
        {
            foreach (var ep in _endpoints)
            {
                var announcement = DnsMessage.Response();
                announcement.Answers.AddRange(BuildRecords(ep.Address));
                await SendAsync(ep, announcement, MulticastEndpoint);
            }
            await Task.Delay(TimeSpan.FromSeconds(1 << i), ct);
        }
    }

    private DnsMessage BuildProbe(IPAddress address)
    {
        var probe = new DnsMessage();
        var unique = BuildRecords(address).Where(r => r.CacheFlush).ToList();
        foreach (var name in unique.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            probe.Questions.Add(new DnsQuestion(name, DnsType.Any, UnicastResponse: true));
        }
        // Proposed records go in the authority section, without the cache-flush bit
        probe.Authorities.AddRange(unique.Select(r => r with { CacheFlush = false }));
        return probe;
    }

    /// <summary>Renames conflicting names. Returns true when another probe round is needed.</summary>
    private bool ResolveConflicts()
    {
        lock (_sync)
        {
            if (_conflicts.Count == 0) return false;

            if (_conflicts.Contains(HostName))
            {
                _hostLabel = NextName(_hostLabel, '-');
                Console.WriteLine($"  [mDNS] Host name in use on the network, using '{HostName}'.");
            }

            for (int i = 0; i < _services.Count; i++)
            {
                var service = _services[i];
                if (_conflicts.Contains(InstanceFqdn(service)))
                {
                    var renamed = SharedPrinter.TruncateUtf8(NextName(service.InstanceName, ' '), 63);
                    Console.WriteLine($"  [mDNS] '{service.InstanceName}' is already used on the network, advertising as '{renamed}'.");
                    _services[i] = service with { InstanceName = renamed };
                }
            }
            return true;
        }
    }

    private static string NextName(string name, char separator)
    {
        // "Name" -> "Name (2)" / "host" -> "host-2", then increment
        if (separator == ' ')
        {
            var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.*) \((\d+)\)$");
            return m.Success ? $"{m.Groups[1].Value} ({int.Parse(m.Groups[2].Value) + 1})" : $"{name} (2)";
        }
        var h = System.Text.RegularExpressions.Regex.Match(name, @"^(.*)-(\d+)$");
        return h.Success ? $"{h.Groups[1].Value}-{int.Parse(h.Groups[2].Value) + 1}" : $"{name}-2";
    }

    // ---------------------------------------------------------------- Receiving & answering

    private async Task ReceiveLoopAsync(Endpoint ep, CancellationToken ct)
    {
        var buffer = new byte[9000];
        bool wildcardBound = !OperatingSystem.IsWindows();
        while (!ct.IsCancellationRequested)
        {
            int receivedBytes;
            EndPoint remoteEndPoint;
            Endpoint target = ep;
            try
            {
                if (wildcardBound)
                {
                    var received = await ep.Socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct);
                    var packet = received.PacketInformation;
                    if (IsMulticast(packet.Address))
                    {
                        // Every wildcard socket gets a copy: only the socket of the arrival interface handles it
                        if (packet.Interface != ep.InterfaceIndex) continue;
                    }
                    else
                    {
                        // Unicast reaches just one of the sockets: answer through the arrival interface's endpoint
                        target = _endpoints.FirstOrDefault(e => e.InterfaceIndex == packet.Interface) ?? ep;
                    }
                    receivedBytes = received.ReceivedBytes;
                    remoteEndPoint = received.RemoteEndPoint;
                }
                else
                {
                    var result = await ep.Socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct);
                    receivedBytes = result.ReceivedBytes;
                    remoteEndPoint = result.RemoteEndPoint;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                continue;
            }
            catch (SocketException)
            {
                return;
            }

            DnsMessage message;
            try
            {
                message = DnsMessage.Parse(buffer.AsSpan(0, receivedBytes));
            }
            catch (FormatException)
            {
                continue;
            }

            var source = (IPEndPoint)remoteEndPoint;
            try
            {
                if (message.IsResponse)
                {
                    ObserveResponse(message, source);
                }
                else if (_active)
                {
                    await AnswerAsync(target, message, source, ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { /* never let one bad packet stop the responder */ }
        }
    }

    private void ObserveResponse(DnsMessage message, IPEndPoint source)
    {
        if (!_probing || IsOwnAddress(source.Address)) return;

        var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HostName };
        lock (_sync)
        {
            foreach (var s in _services) ours.Add(InstanceFqdn(s));
            foreach (var record in message.Answers.Concat(message.Additionals))
            {
                if (ours.Contains(record.Name.TrimEnd('.')))
                {
                    _conflicts.Add(record.Name.TrimEnd('.'));
                }
            }
        }
    }

    private async Task AnswerAsync(Endpoint ep, DnsMessage query, IPEndPoint source, CancellationToken ct)
    {
        if (query.Questions.Count == 0) return;

        var records = BuildRecords(ep.Address);
        var answers = new List<DnsRecord>();

        foreach (var q in query.Questions)
        {
            foreach (var r in records)
            {
                if ((q.Type == r.Type || q.Type == DnsType.Any) && DnsName.Equal(q.Name, r.Name) && !IsKnownAnswer(query, r) && !answers.Contains(r))
                {
                    answers.Add(r);
                }
            }
        }

        if (answers.Count == 0) return;

        var additionals = new List<DnsRecord>();
        foreach (var answer in answers)
        {
            if (answer.Type == DnsType.Ptr && answer.Data is string instance)
            {
                additionals.AddRange(records.Where(r => (r.Type is DnsType.Srv or DnsType.Txt) && DnsName.Equal(r.Name, instance)));
            }
            if (answer.Type is DnsType.Ptr or DnsType.Srv)
            {
                additionals.AddRange(records.Where(r => r.Type == DnsType.A));
            }
        }

        // Meta-query PTRs point at service types; they need no additional records
        additionals = additionals.Distinct().Where(r => !answers.Contains(r)).ToList();

        var response = DnsMessage.Response();
        bool legacyUnicast = source.Port != MdnsPort;

        if (legacyUnicast)
        {
            // One-shot resolvers (RFC 6762 §6.7): echo the id and questions, short TTLs, no cache-flush bit
            response.Id = query.Id;
            response.Questions.AddRange(query.Questions);
            response.Answers.AddRange(answers.Select(r => r with { CacheFlush = false, Ttl = Math.Min(r.Ttl, LegacyUnicastMaxTtl) }));
            response.Additionals.AddRange(additionals.Select(r => r with { CacheFlush = false, Ttl = Math.Min(r.Ttl, LegacyUnicastMaxTtl) }));
            await SendAsync(ep, response, source);
            return;
        }

        response.Answers.AddRange(answers);
        response.Additionals.AddRange(additionals);

        if (query.Questions.All(q => q.UnicastResponse))
        {
            await SendAsync(ep, response, source);
            return;
        }

        // Shared records are answered after a random delay to avoid collisions (RFC 6762 §6)
        if (answers.Any(r => !r.CacheFlush))
        {
            await Task.Delay(Random.Shared.Next(20, 120), ct);
        }
        await SendAsync(ep, response, MulticastEndpoint);
    }

    private static bool IsKnownAnswer(DnsMessage query, DnsRecord record) =>
        query.Answers.Any(k => k.Type == record.Type && DnsName.Equal(k.Name, record.Name) && k.SameData(record) && k.Ttl >= record.Ttl / 2);

    // ---------------------------------------------------------------- Records

    private List<DnsRecord> BuildRecords(IPAddress address)
    {
        List<DnsSdService> services;
        string host;
        lock (_sync)
        {
            services = _services.ToList();
            host = HostName;
        }

        var records = new List<DnsRecord>();
        foreach (var serviceType in services.Select(s => s.ServiceType).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            records.Add(new DnsRecord(MetaQuery, DnsType.Ptr, false, SharedTtl, $"{serviceType}.local"));
        }

        foreach (var s in services)
        {
            var instance = InstanceFqdn(s);
            records.Add(new DnsRecord($"{s.ServiceType}.local", DnsType.Ptr, false, SharedTtl, instance));
            foreach (var subtype in s.Subtypes)
            {
                records.Add(new DnsRecord($"{subtype}._sub.{s.ServiceType}.local", DnsType.Ptr, false, SharedTtl, instance));
            }
            records.Add(new DnsRecord(instance, DnsType.Srv, true, UniqueTtl, new SrvData(0, 0, (ushort)s.Port, host)));

            var txt = DnsMessage.Txt();
            foreach (var (key, value) in s.Txt)
            {
                txt.Add(key, value);
            }
            records.Add(new DnsRecord(instance, DnsType.Txt, true, SharedTtl, txt.Build()));
        }

        records.Add(new DnsRecord(host, DnsType.A, true, UniqueTtl, address));
        return records;
    }

    private static string InstanceFqdn(DnsSdService service) =>
        $"{DnsName.EscapeLabel(service.InstanceName)}.{service.ServiceType}.local";

    private async Task SendAsync(Endpoint ep, DnsMessage message, IPEndPoint destination)
    {
        try
        {
            await ep.Socket.SendToAsync(message.Encode(), SocketFlags.None, destination);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private bool IsOwnAddress(IPAddress address) => _endpoints.Any(e => e.Address.Equals(address));

    // ---------------------------------------------------------------- Interfaces

    private static bool IsMulticast(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork && (address.GetAddressBytes()[0] & 0xF0) == 0xE0;

    private static List<(IPAddress Address, string Name, int Index)> GetMulticastInterfaces()
    {
        var result = new List<(IPAddress, string, int)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                    !ni.SupportsMulticast)
                {
                    continue;
                }

                var properties = ni.GetIPProperties();
                int index = -1;
                try { index = properties.GetIPv4Properties()?.Index ?? -1; } catch (PlatformNotSupportedException) { }
                foreach (var ua in properties.UnicastAddresses)
                {
                    var ip = ua.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                    var b = ip.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue; // link-local without DHCP: not reachable by phones
                    result.Add((ip, ni.Name, index));
                }
            }
        }
        catch (NetworkInformationException) { }
        return result;
    }

    private static void DisableConnectionResetReporting(Socket socket)
    {
        // SIO_UDP_CONNRESET: stop ICMP "port unreachable" from surfacing as receive errors on Windows
        const int SioUdpConnReset = unchecked((int)0x9800000C);
        try { socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null); } catch { }
    }

    private static string MakeHostLabel(string machineName)
    {
        var sb = new StringBuilder();
        foreach (var c in machineName.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var label = sb.ToString().Trim('-');
        if (label.Length == 0) label = "host";
        if (label.Length > 40) label = label[..40].TrimEnd('-');
        return $"{label}-printman";
    }
}
