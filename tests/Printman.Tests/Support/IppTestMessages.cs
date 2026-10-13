using System.Net;
using Printman.Core.Models;
using Printman.Services.Ipp;

namespace Printman.Tests.Support;

/// <summary>Builds IPP request messages and transport contexts for handler/codec tests.</summary>
public static class IppTestMessages
{
    public const string Host = "printman.local:631";

    /// <summary>Creates an operation message with the mandatory charset/language attributes.</summary>
    public static IppMessage New(short operation, int requestId = 1, string? slug = null)
    {
        var message = new IppMessage
        {
            VersionMajor = 2,
            VersionMinor = 0,
            Code = operation,
            RequestId = requestId
        };

        var op = message.AddGroup(IppTag.OperationAttributes);
        op.AddCharset("attributes-charset", "utf-8");
        op.AddLanguage("attributes-natural-language", "en");
        op.AddUri("printer-uri", $"ipp://{Host}/ipp/print{(slug is null ? "" : "/" + slug)}");
        return message;
    }

    /// <summary>Serializes the message and appends an optional document body.</summary>
    public static byte[] Body(IppMessage message, byte[]? document = null)
    {
        var head = IppMessageWriter.Write(message);
        if (document is null || document.Length == 0)
        {
            return head;
        }

        var buffer = new byte[head.Length + document.Length];
        head.CopyTo(buffer, 0);
        document.CopyTo(buffer, head.Length);
        return buffer;
    }

    public static IppRequestContext Context(byte[] body, string? slug = null, string host = Host, IPAddress? remote = null) =>
        new(new MemoryStream(body, writable: false), slug, host, remote ?? IPAddress.Parse("192.168.1.50"));

    public static IppRequestContext Context(IppMessage message, byte[]? document = null, string? slug = null, string host = Host, IPAddress? remote = null) =>
        Context(Body(message, document), slug, host, remote);

    public static byte[] PdfDoc(string marker = "%PDF-1.4 test") => System.Text.Encoding.ASCII.GetBytes(marker);

    /// <summary>Creates a shared printer with the given slug.</summary>
    public static SharedPrinter Printer(string slug = "fake-printer", string systemName = "Fake Printer") => new()
    {
        SystemName = systemName,
        Slug = slug,
        DisplayName = $"Printman - {systemName}",
        Uuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
    };
}
