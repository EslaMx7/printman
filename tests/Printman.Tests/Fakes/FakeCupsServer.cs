using System.Net;
using System.Net.Http.Headers;
using Printman.Core.Models;
using Printman.Services.Cups;
using Printman.Services.Ipp;

namespace Printman.Tests.Fakes;

/// <summary>
/// In-memory cupsd: an <see cref="HttpMessageHandler"/> that decodes application/ipp requests with the
/// real codec and answers from a scripted handler. Records every request for assertions.
/// </summary>
public sealed class FakeCupsServer : HttpMessageHandler
{
    public sealed record Received(string Resource, IppMessage Message, byte[] Document, string? Authorization);

    public List<Received> Requests { get; } = [];

    /// <summary>Scripted IPP answer. The default replies successful-ok with no attributes.</summary>
    public Func<Received, IppMessage> Respond { get; set; } = _ => Ok();

    /// <summary>When set, every request fails with this exception (connection refused, timeout, ...).</summary>
    public Func<Exception>? Fail { get; set; }

    /// <summary>When set, the first N requests get HTTP 401 (cupsd asking for PeerCred).</summary>
    public int UnauthorizedFirst { get; set; }

    /// <summary>HTTP status for every reply (e.g. 500 to simulate a broken cupsd).</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>Raw bytes to send instead of an encoded IPP reply (for malformed-response tests).</summary>
    public byte[]? RawBody { get; set; }

    /// <summary>PPD text served at /printers/{name}.ppd, or null for 404.</summary>
    public string? Ppd { get; set; } = "*PPD-Adobe: \"4.3\"\n";

    public CupsClient CreateClient(bool viaSocket = true) =>
        new(viaSocket ? new CupsEndpoint("/run/cups/cups.sock", "localhost", 631) : new CupsEndpoint(null, "localhost", 631), this);

    public static IppMessage Ok(Action<IppMessage>? fill = null)
    {
        var reply = new IppMessage { Code = IppStatus.Ok };
        var op = reply.AddGroup(IppTag.OperationAttributes);
        op.AddCharset("attributes-charset", "utf-8");
        op.AddLanguage("attributes-natural-language", "en");
        fill?.Invoke(reply);
        return reply;
    }

    public static IppMessage Error(short status, string? message = null)
    {
        var reply = Ok();
        reply.Code = status;
        if (message != null)
        {
            reply.Group(IppTag.OperationAttributes)!.AddText("status-message", message);
        }
        return reply;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Fail != null)
        {
            throw Fail();
        }

        var resource = "/" + request.RequestUri!.AbsolutePath.TrimStart('/');

        if (request.Method == HttpMethod.Get)
        {
            return Ppd != null && resource.EndsWith(".ppd", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ppd) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        var stream = new MemoryStream(bytes, writable: false);
        var message = await IppMessageReader.ReadAsync(stream, ct: cancellationToken);
        var document = bytes[(int)stream.Position..];
        var received = new Received(resource, message, document, request.Headers.Authorization?.ToString());
        Requests.Add(received);

        if (UnauthorizedFirst > 0)
        {
            UnauthorizedFirst--;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        var body = RawBody ?? IppMessageWriter.Write(Respond(received));
        return new HttpResponseMessage(Status)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/ipp") } }
        };
    }
}
