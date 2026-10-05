using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Server;

/// <summary>
/// Maps the IPP endpoint (/ipp/print[/{slug}]) onto Kestrel. Only transport concerns live here:
/// source filtering, body limits and content types. Protocol handling is in <see cref="IIppRequestHandler"/>.
/// </summary>
public static class IppEndpoints
{
    public static void Map(IEndpointRouteBuilder app, IIppRequestHandler handler, IppServerSettings settings, bool allowAnySource)
    {
        app.MapPost("/ipp/print", ctx => HandleAsync(ctx, null, handler, settings, allowAnySource));
        app.MapPost("/ipp/print/{**rest}", ctx =>
            HandleAsync(ctx, ctx.Request.RouteValues["rest"] as string, handler, settings, allowAnySource));

        // Browsers following the printer URL get pointed to the web UI (when it is running)
        app.MapGet("/ipp/print/{**rest}", (HttpContext ctx) => Results.Content(
            settings.WebUiEnabled
                ? $"This is a Printman network printer (IPP). Add it from your device's printer settings, or use the web UI on port {settings.WebPort}."
                : "This is a Printman network printer (IPP). Add it from your device's printer settings.",
            "text/plain"));
    }

    private static async Task HandleAsync(
        HttpContext ctx,
        string? rest,
        IIppRequestHandler handler,
        IppServerSettings settings,
        bool allowAnySource)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (!allowAnySource && !IsLocalNetworkAddress(remote))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Requiring application/ipp also stops browsers from posting cross-site forms here
        if (ctx.Request.ContentType?.StartsWith("application/ipp", StringComparison.OrdinalIgnoreCase) != true)
        {
            ctx.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        var sizeFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = settings.MaxJobBytes + 1024 * 1024;
        }

        // Phones render pages while uploading; Kestrel's minimum data rate would drop them
        var rateFeature = ctx.Features.Get<IHttpMinRequestBodyDataRateFeature>();
        if (rateFeature != null)
        {
            rateFeature.MinDataRate = null;
        }

        var slug = rest?.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var host = ctx.Request.Host.HasValue ? ctx.Request.Host.Value! : $"{ctx.Connection.LocalIpAddress}:{ctx.Connection.LocalPort}";

        byte[] response;
        try
        {
            await using var body = new BufferedStream(ctx.Request.Body, 16 * 1024);
            response = await handler.HandleAsync(new IppRequestContext(body, slug, host, remote), ctx.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (BadHttpRequestException)
        {
            // Body exceeded the limit or the client disconnected mid-upload
            ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "application/ipp";
        ctx.Response.ContentLength = response.Length;
        await ctx.Response.Body.WriteAsync(response, ctx.RequestAborted);
    }

    /// <summary>Loopback, RFC 1918, link-local, CGNAT and IPv6 unique-local addresses.</summary>
    public static bool IsLocalNetworkAddress(IPAddress? address)
    {
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10 ||
                   (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                   (b[0] == 192 && b[1] == 168) ||
                   (b[0] == 169 && b[1] == 254) ||
                   (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.IsIPv6SiteLocal;
    }
}
