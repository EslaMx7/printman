using System.Net;
using System.Text;

namespace Printman.Core.Models;

/// <summary>
/// IPP delimiter and value tags (RFC 8010 §3.5).
/// </summary>
public enum IppTag : byte
{
    // Delimiter tags
    OperationAttributes = 0x01,
    JobAttributes = 0x02,
    EndOfAttributes = 0x03,
    PrinterAttributes = 0x04,
    UnsupportedAttributes = 0x05,

    // Out-of-band values
    Unsupported = 0x10,
    Unknown = 0x12,
    NoValue = 0x13,

    // Integer values
    Integer = 0x21,
    Boolean = 0x22,
    Enum = 0x23,

    // Octet-string values
    OctetString = 0x30,
    DateTime = 0x31,
    Resolution = 0x32,
    RangeOfInteger = 0x33,
    BegCollection = 0x34,
    TextWithLanguage = 0x35,
    NameWithLanguage = 0x36,
    EndCollection = 0x37,

    // Character-string values
    TextWithoutLanguage = 0x41,
    NameWithoutLanguage = 0x42,
    Keyword = 0x44,
    Uri = 0x45,
    UriScheme = 0x46,
    Charset = 0x47,
    NaturalLanguage = 0x48,
    MimeMediaType = 0x49,
    MemberAttrName = 0x4A
}

public static class IppOperation
{
    public const short PrintJob = 0x0002;
    public const short ValidateJob = 0x0004;
    public const short CreateJob = 0x0005;
    public const short SendDocument = 0x0006;
    public const short CancelJob = 0x0008;
    public const short GetJobAttributes = 0x0009;
    public const short GetJobs = 0x000A;
    public const short GetPrinterAttributes = 0x000B;
    public const short CancelMyJobs = 0x0039;
    public const short CloseJob = 0x003B;
    public const short IdentifyPrinter = 0x003C;
}

public static class IppStatus
{
    public const short Ok = 0x0000;
    public const short OkIgnoredOrSubstituted = 0x0001;
    public const short BadRequest = 0x0400;
    public const short Forbidden = 0x0401;
    public const short NotPossible = 0x0404;
    public const short NotFound = 0x0406;
    public const short RequestEntityTooLarge = 0x0408;
    public const short DocumentFormatNotSupported = 0x040A;
    public const short AttributesOrValuesNotSupported = 0x040B;
    public const short InternalError = 0x0500;
    public const short OperationNotSupported = 0x0501;
    public const short VersionNotSupported = 0x0503;
    public const short Busy = 0x0507;
    public const short MultipleDocumentJobsNotSupported = 0x0509;
}

public static class IppJobState
{
    public const int Pending = 3;
    public const int PendingHeld = 4;
    public const int Processing = 5;
    public const int ProcessingStopped = 6;
    public const int Canceled = 7;
    public const int Aborted = 8;
    public const int Completed = 9;

    public static bool IsTerminal(int state) => state >= Canceled;
}

public static class IppPrinterState
{
    public const int Idle = 3;
    public const int Processing = 4;
    public const int Stopped = 5;
}

public readonly record struct IppResolution(int CrossFeed, int Feed, byte Units = 3); // Units: 3 = dots per inch
public readonly record struct IppRange(int Lower, int Upper);

/// <summary>
/// A single attribute value. <see cref="Value"/> is an int, bool, string, byte[],
/// <see cref="IppResolution"/>, <see cref="IppRange"/>, <see cref="IppCollection"/> or null (out-of-band).
/// </summary>
public sealed record IppValue(IppTag Tag, object? Value)
{
    public int? AsInt() => Value as int?;
    public bool? AsBool() => Value as bool?;
    public string? AsString() => Value as string;
    public IppCollection? AsCollection() => Value as IppCollection;
    public IppRange? AsRange() => Value as IppRange?;
}

public sealed class IppAttribute(string name)
{
    public string Name { get; } = name;
    public List<IppValue> Values { get; } = [];

    public IppValue? First => Values.Count > 0 ? Values[0] : null;
}

/// <summary>
/// Ordered list of attributes, used both for attribute groups and collection values.
/// </summary>
public class IppAttributeList
{
    public List<IppAttribute> Attributes { get; } = [];

    public IppAttribute? Get(string name) =>
        Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    public IppAttributeList Add(string name, IppTag tag, params object?[] values)
    {
        var attr = new IppAttribute(name);
        foreach (var v in values)
        {
            attr.Values.Add(new IppValue(tag, v));
        }
        if (values.Length == 0)
        {
            attr.Values.Add(new IppValue(IppTag.NoValue, null));
        }
        Attributes.Add(attr);
        return this;
    }

    public IppAttributeList AddKeyword(string name, params string[] values) => Add(name, IppTag.Keyword, values);
    public IppAttributeList AddKeywords(string name, IEnumerable<string> values) => Add(name, IppTag.Keyword, values.Cast<object?>().ToArray());
    public IppAttributeList AddInteger(string name, params int[] values) => Add(name, IppTag.Integer, values.Cast<object?>().ToArray());
    public IppAttributeList AddEnum(string name, params int[] values) => Add(name, IppTag.Enum, values.Cast<object?>().ToArray());
    public IppAttributeList AddBoolean(string name, bool value) => Add(name, IppTag.Boolean, value);
    public IppAttributeList AddText(string name, string value) => Add(name, IppTag.TextWithoutLanguage, value);
    public IppAttributeList AddName(string name, string value) => Add(name, IppTag.NameWithoutLanguage, value);
    public IppAttributeList AddUri(string name, params string[] values) => Add(name, IppTag.Uri, values);
    public IppAttributeList AddCharset(string name, string value) => Add(name, IppTag.Charset, value);
    public IppAttributeList AddLanguage(string name, string value) => Add(name, IppTag.NaturalLanguage, value);
    public IppAttributeList AddMimeTypes(string name, IEnumerable<string> values) => Add(name, IppTag.MimeMediaType, values.Cast<object?>().ToArray());
    public IppAttributeList AddResolution(string name, params IppResolution[] values) => Add(name, IppTag.Resolution, values.Cast<object?>().ToArray());
    public IppAttributeList AddRange(string name, int lower, int upper) => Add(name, IppTag.RangeOfInteger, new IppRange(lower, upper));
    public IppAttributeList AddCollection(string name, params IppCollection[] values) => Add(name, IppTag.BegCollection, values.Cast<object?>().ToArray());
    public IppAttributeList AddNoValue(string name) => Add(name, IppTag.NoValue, (object?)null);
    public IppAttributeList AddDateTime(string name, DateTimeOffset value) => Add(name, IppTag.DateTime, EncodeDateTime(value));

    /// <summary>RFC 2579 DateAndTime (11 octets).</summary>
    public static byte[] EncodeDateTime(DateTimeOffset value)
    {
        var offset = value.Offset;
        return
        [
            (byte)(value.Year >> 8), (byte)value.Year,
            (byte)value.Month, (byte)value.Day,
            (byte)value.Hour, (byte)value.Minute, (byte)value.Second,
            (byte)(value.Millisecond / 100),
            (byte)(offset < TimeSpan.Zero ? '-' : '+'),
            (byte)Math.Abs(offset.Hours), (byte)Math.Abs(offset.Minutes)
        ];
    }
}

public sealed class IppCollection : IppAttributeList;

public sealed class IppAttributeGroup(IppTag tag) : IppAttributeList
{
    public IppTag Tag { get; } = tag;
}

public sealed class IppMessage
{
    public byte VersionMajor { get; set; } = 2;
    public byte VersionMinor { get; set; } = 0;

    /// <summary>Operation id (requests) or status code (responses).</summary>
    public short Code { get; set; }
    public int RequestId { get; set; } = 1;
    public List<IppAttributeGroup> Groups { get; } = [];

    public IppAttributeGroup? Group(IppTag tag) => Groups.FirstOrDefault(g => g.Tag == tag);

    public IppAttributeGroup AddGroup(IppTag tag)
    {
        var group = new IppAttributeGroup(tag);
        Groups.Add(group);
        return group;
    }

    /// <summary>Looks up an attribute in the operation group, then the job group.</summary>
    public IppAttribute? FindRequestAttribute(string name) =>
        Group(IppTag.OperationAttributes)?.Get(name) ?? Group(IppTag.JobAttributes)?.Get(name);

    /// <summary>Looks up a job template attribute in the job group, then the operation group.</summary>
    public IppAttribute? FindJobAttribute(string name) =>
        Group(IppTag.JobAttributes)?.Get(name) ?? Group(IppTag.OperationAttributes)?.Get(name);
}

public sealed class IppParseException(string message) : Exception(message);

/// <summary>
/// Transport-independent view of an incoming IPP HTTP request.
/// </summary>
/// <param name="Body">Request body positioned at the start of the IPP message; the document follows the attributes.</param>
/// <param name="PrinterSlug">Printer path segment from /ipp/print/{slug}, or null for the default shared printer.</param>
/// <param name="Host">Host header as sent by the client (host[:port]); used to build printer and job URIs.</param>
/// <param name="RemoteAddress">Client address, for logging.</param>
public sealed record IppRequestContext(Stream Body, string? PrinterSlug, string Host, IPAddress? RemoteAddress);

/// <summary>
/// Job template values parsed from an IPP request.
/// </summary>
public sealed record IppJobOptions
{
    public int Copies { get; init; } = 1;
    public PrintDuplex Duplex { get; init; } = PrintDuplex.Default;
    public PrintColorMode ColorMode { get; init; } = PrintColorMode.Default;
    public PrintOrientation Orientation { get; init; } = PrintOrientation.Auto;
    public string? PaperSizeName { get; init; }
    public PageRange PageRange { get; init; } = PageRange.All;
    public bool FitToPage { get; init; } = true;
}

/// <summary>
/// An IPP job tracked for status queries. State derives from the pipeline ticket once a document arrives.
/// </summary>
public sealed class IppJob
{
    private readonly object _sync = new();
    private int? _overrideState;
    private string? _overrideReason;
    private DateTime? _overrideCompletedAt;

    public required int Id { get; init; }
    public required string PrinterSlug { get; init; }
    public required string Name { get; init; }
    public required string UserName { get; init; }
    public IppJobOptions Options { get; init; } = new();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public PipelineTicket? Ticket { get; private set; }
    public string? DocumentFormat { get; private set; }

    /// <summary>True between Create-Job and the first Send-Document.</summary>
    public bool AwaitingDocument => Ticket == null && _overrideState == null;

    public void Attach(PipelineTicket ticket, string documentFormat)
    {
        lock (_sync)
        {
            Ticket = ticket;
            DocumentFormat = documentFormat;
        }
    }

    /// <summary>Ends a job that never reached the pipeline (canceled or aborted).</summary>
    public void Terminate(int state, string reason)
    {
        lock (_sync)
        {
            if (Ticket != null || _overrideState != null) return;
            _overrideState = state;
            _overrideReason = reason;
            _overrideCompletedAt = DateTime.UtcNow;
        }
    }

    public (int State, string Reason, string Message) GetState()
    {
        lock (_sync)
        {
            if (_overrideState is int s)
            {
                return (s, _overrideReason ?? "none", _overrideReason ?? "");
            }

            if (Ticket == null)
            {
                return (IppJobState.Pending, "job-incoming", "Waiting for document data.");
            }

            return Ticket.State switch
            {
                PipelineJobState.Pending => (IppJobState.Pending, "none", "Queued."),
                PipelineJobState.Processing => (IppJobState.Processing, "job-printing", "Printing."),
                PipelineJobState.Completed => (IppJobState.Completed, "job-completed-successfully", "Sent to printer."),
                PipelineJobState.Canceled => (IppJobState.Canceled, "job-canceled-by-user", "Canceled."),
                _ => (IppJobState.Aborted, "aborted-by-system", Ticket.Message ?? "Printing failed.")
            };
        }
    }

    public DateTime? StartedAt => Ticket?.StartedAt;
    public DateTime? CompletedAt => _overrideCompletedAt ?? Ticket?.CompletedAt;
    public int PagesPrinted => Ticket?.PagesPrinted ?? 0;
}

/// <summary>
/// Runtime settings shared by the IPP front-end and the DNS-SD advertiser.
/// </summary>
public sealed class IppServerSettings
{
    public int WebPort { get; set; } = 5000;
    public bool WebUiEnabled { get; set; } = true;
    public int IppPort { get; set; } = ShareOptions.DefaultIppPort;
    public long MaxJobBytes { get; set; } = 256L * 1024 * 1024;
    public int MaxPendingJobs { get; set; } = 50;

    /// <summary>mDNS host name (e.g. "desktop-printman.local"), null when advertising is off.</summary>
    public string? MdnsHostName { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public int UpTimeSeconds => (int)Math.Max(1, (DateTime.UtcNow - StartedAt).TotalSeconds + 1);
    public int ToUpTime(DateTime utc) => (int)Math.Max(1, (utc - StartedAt).TotalSeconds + 1);
}

/// <summary>
/// A local printer (Windows or CUPS queue) exposed on the network.
/// </summary>
public sealed class SharedPrinter
{
    public required string SystemName { get; init; }

    /// <summary>URL-safe identifier used in /ipp/print/{slug}.</summary>
    public required string Slug { get; init; }

    /// <summary>Name shown to clients: "Printman - {SystemName}".</summary>
    public required string DisplayName { get; init; }
    public required Guid Uuid { get; init; }

    public string ResourcePath => $"ipp/print/{Slug}";

    public static string TruncateUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
        var sb = new StringBuilder();
        int bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            int len = rune.Utf8SequenceLength;
            if (bytes + len > maxBytes) break;
            sb.Append(rune.ToString());
            bytes += len;
        }
        return sb.ToString().TrimEnd();
    }
}
