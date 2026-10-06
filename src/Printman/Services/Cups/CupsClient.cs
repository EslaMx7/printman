using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Printman.Core.Models;
using Printman.Services.Ipp;

namespace Printman.Services.Cups;

public sealed class CupsException(string message, short? ippStatus = null) : Exception(message)
{
    public short? IppStatus { get; } = ippStatus;
}

/// <summary>
/// Minimal IPP client for the local CUPS scheduler (Linux, macOS, Raspberry Pi OS), built on Printman's own
/// IPP codec. Connects through the cupsd domain socket when present (authenticating as the calling user via
/// PeerCred, like lp / cancel do), otherwise over TCP to localhost:631. Honours CUPS_SERVER.
/// </summary>
public sealed class CupsClient : IDisposable
{
    public const short CupsGetDefault = 0x4001;
    public const short CupsGetPrinters = 0x4002;

    private const int MaxResponseBytes = 16 * 1024 * 1024;
    private static readonly string[] SocketCandidates = ["/run/cups/cups.sock", "/var/run/cups/cups.sock", "/private/var/run/cupsd"];

    private readonly HttpClient _http;
    private readonly bool _viaSocket;
    private readonly string _endpointDescription;
    private int _requestId;

    public CupsClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };

        var (socketPath, host, port) = ResolveServer();
        if (socketPath != null)
        {
            _viaSocket = true;
            _endpointDescription = socketPath;
            handler.ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
            _http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        }
        else
        {
            _endpointDescription = $"{host}:{port}";
            _http = new HttpClient(handler) { BaseAddress = new Uri($"http://{host}:{port}/") };
        }

        _http.Timeout = Timeout.InfiniteTimeSpan; // per-call cancellation instead
    }

    public static string PrinterUri(string printerName) => $"ipp://localhost/printers/{Uri.EscapeDataString(printerName)}";
    public static string JobUri(int jobId) => $"ipp://localhost/jobs/{jobId}";

    public static bool IsSuccess(short status) => status < 0x0100;

    /// <summary>
    /// Sends one IPP request. <paramref name="operation"/> adds operation attributes after the charset,
    /// language and requesting-user-name; <paramref name="job"/> fills an optional job attribute group.
    /// Throws <see cref="CupsException"/> when CUPS is unreachable or answers with an error status.
    /// </summary>
    public async Task<IppMessage> SendAsync(
        short operationId,
        string resource,
        Action<IppAttributeGroup> operation,
        Action<IppAttributeGroup>? job = null,
        string? documentPath = null,
        CancellationToken ct = default)
    {
        var request = new IppMessage { Code = operationId, RequestId = Interlocked.Increment(ref _requestId) };
        var op = request.AddGroup(IppTag.OperationAttributes);
        op.AddCharset("attributes-charset", "utf-8");
        op.AddLanguage("attributes-natural-language", "en");
        operation(op);
        if (op.Get("requesting-user-name") == null)
        {
            op.AddName("requesting-user-name", Environment.UserName);
        }
        if (job != null)
        {
            job(request.AddGroup(IppTag.JobAttributes));
        }

        var header = IppMessageWriter.Write(request);

        HttpResponseMessage response;
        try
        {
            response = await PostAsync(resource, header, documentPath, authenticate: false, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && _viaSocket)
            {
                response.Dispose();
                response = await PostAsync(resource, header, documentPath, authenticate: true, ct);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new CupsException($"Cannot reach the CUPS print service at {_endpointDescription} ({ex.Message}). Is cups installed and running?");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new CupsException("CUPS refused the request (not authorized). Your user may need to be in the lpadmin group for this action.", IppStatus.Forbidden);
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new CupsException($"CUPS answered HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            IppMessage reply;
            try
            {
                reply = await IppMessageReader.ReadAsync(body, MaxResponseBytes, ct);
            }
            catch (Exception ex) when (ex is IppParseException or EndOfStreamException)
            {
                throw new CupsException($"Invalid IPP response from CUPS: {ex.Message}");
            }

            if (!IsSuccess(reply.Code))
            {
                var message = reply.Group(IppTag.OperationAttributes)?.Get("status-message")?.First?.AsString();
                throw new CupsException(message ?? $"CUPS returned IPP status 0x{reply.Code:x4}.", reply.Code);
            }

            return reply;
        }
    }

    /// <summary>
    /// Saves the queue's PPD (GET /printers/{name}.ppd, readable by any user, unlike /etc/cups/ppd).
    /// Returns false when the queue has none.
    /// </summary>
    public async Task<bool> DownloadPpdAsync(string printerName, string destination, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync($"printers/{Uri.EscapeDataString(printerName)}.ppd", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return false;

            await using var file = File.Create(destination);
            await response.Content.CopyToAsync(file, ct);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Synchronous wrapper for the synchronous discovery / queue interfaces.</summary>
    public IppMessage Send(short operationId, string resource, Action<IppAttributeGroup> operation, Action<IppAttributeGroup>? job = null) =>
        SendAsync(operationId, resource, operation, job).GetAwaiter().GetResult();

    private async Task<HttpResponseMessage> PostAsync(string resource, byte[] header, string? documentPath, bool authenticate, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, resource.TrimStart('/'))
        {
            Content = new IppContent(header, documentPath)
        };
        if (authenticate)
        {
            // cupsd verifies the name against the peer credentials of the domain socket
            message.Headers.Authorization = new AuthenticationHeaderValue("PeerCred", Environment.UserName);
        }
        return await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static (string? SocketPath, string Host, int Port) ResolveServer()
    {
        var server = Environment.GetEnvironmentVariable("CUPS_SERVER");
        if (!string.IsNullOrWhiteSpace(server))
        {
            if (server.StartsWith('/')) return (server, "localhost", 631);

            var hostPart = server;
            int port = 631;
            int colon = server.LastIndexOf(':');
            if (colon > 0 && !server.EndsWith(']') && int.TryParse(server[(colon + 1)..], out int p))
            {
                hostPart = server[..colon];
                port = p;
            }
            return (null, hostPart, port);
        }

        foreach (var candidate in SocketCandidates)
        {
            if (File.Exists(candidate)) return (candidate, "localhost", 631);
        }
        return (null, "localhost", 631);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>IPP attributes followed by the (optional) document file, streamed without buffering.</summary>
    private sealed class IppContent : HttpContent
    {
        private readonly byte[] _header;
        private readonly string? _documentPath;

        public IppContent(byte[] header, string? documentPath)
        {
            _header = header;
            _documentPath = documentPath;
            Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(_header, cancellationToken);
            if (_documentPath != null)
            {
                await using var file = new FileStream(_documentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await file.CopyToAsync(stream, cancellationToken);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _header.Length + (_documentPath != null ? new FileInfo(_documentPath).Length : 0);
            return true;
        }
    }
}
