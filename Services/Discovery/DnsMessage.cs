using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Printman.Services.Discovery;

public static class DnsType
{
    public const ushort A = 1;
    public const ushort Ptr = 12;
    public const ushort Txt = 16;
    public const ushort Aaaa = 28;
    public const ushort Srv = 33;
    public const ushort Any = 255;
}

public sealed record DnsQuestion(string Name, ushort Type, bool UnicastResponse);

public sealed record SrvData(ushort Priority, ushort Weight, ushort Port, string Target);

/// <summary>
/// A resource record. <see cref="Data"/> is a string (PTR target), <see cref="SrvData"/>,
/// <see cref="IPAddress"/> (A/AAAA) or byte[] (TXT and anything else, raw rdata).
/// Names are dotted strings; a literal dot inside a label is escaped as "\.".
/// </summary>
public sealed record DnsRecord(string Name, ushort Type, bool CacheFlush, uint Ttl, object Data)
{
    public DnsRecord WithTtl(uint ttl) => this with { Ttl = ttl };

    public bool SameData(DnsRecord other) => (Data, other.Data) switch
    {
        (string a, string b) => DnsName.Equal(a, b),
        (SrvData a, SrvData b) => a.Port == b.Port && DnsName.Equal(a.Target, b.Target),
        (IPAddress a, IPAddress b) => a.Equals(b),
        (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
        _ => false
    };
}

public sealed class DnsMessage
{
    public ushort Id { get; set; }
    public ushort Flags { get; set; }
    public List<DnsQuestion> Questions { get; } = [];
    public List<DnsRecord> Answers { get; } = [];
    public List<DnsRecord> Authorities { get; } = [];
    public List<DnsRecord> Additionals { get; } = [];

    public bool IsResponse => (Flags & 0x8000) != 0;

    /// <summary>Authoritative mDNS response header (QR + AA).</summary>
    public static DnsMessage Response() => new() { Flags = 0x8400 };

    public static DnsTxtBuilder Txt() => new();

    // ---------------------------------------------------------------- Encoding

    public byte[] Encode()
    {
        var w = new Writer();
        w.U16(Id);
        w.U16(Flags);
        w.U16((ushort)Questions.Count);
        w.U16((ushort)Answers.Count);
        w.U16((ushort)Authorities.Count);
        w.U16((ushort)Additionals.Count);

        foreach (var q in Questions)
        {
            w.Name(q.Name);
            w.U16(q.Type);
            w.U16((ushort)(1 | (q.UnicastResponse ? 0x8000 : 0)));
        }

        foreach (var r in Answers.Concat(Authorities).Concat(Additionals))
        {
            w.Record(r);
        }

        return w.ToArray();
    }

    // ---------------------------------------------------------------- Decoding

    public static DnsMessage Parse(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        int pos = 0;

        if (bytes.Length < 12) throw new FormatException("DNS message too short.");
        var msg = new DnsMessage
        {
            Id = BinaryPrimitives.ReadUInt16BigEndian(bytes),
            Flags = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2))
        };
        int qd = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        int an = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6));
        int ns = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8));
        int ar = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10));
        pos = 12;

        for (int i = 0; i < qd; i++)
        {
            var name = ReadName(bytes, ref pos);
            Need(bytes, pos, 4);
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos));
            ushort cls = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos + 2));
            pos += 4;
            msg.Questions.Add(new DnsQuestion(name, type, (cls & 0x8000) != 0));
        }

        ReadRecords(bytes, ref pos, an, msg.Answers);
        ReadRecords(bytes, ref pos, ns, msg.Authorities);
        ReadRecords(bytes, ref pos, ar, msg.Additionals);
        return msg;
    }

    private static void ReadRecords(byte[] bytes, ref int pos, int count, List<DnsRecord> target)
    {
        for (int i = 0; i < count; i++)
        {
            var name = ReadName(bytes, ref pos);
            Need(bytes, pos, 10);
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos));
            ushort cls = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos + 2));
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 4));
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos + 8));
            pos += 10;
            Need(bytes, pos, length);
            int end = pos + length;

            object data;
            switch (type)
            {
                case DnsType.Ptr:
                    int p = pos;
                    data = ReadName(bytes, ref p);
                    break;
                case DnsType.Srv when length >= 7:
                    int s = pos + 6;
                    data = new SrvData(
                        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos)),
                        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos + 2)),
                        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos + 4)),
                        ReadName(bytes, ref s));
                    break;
                case DnsType.A when length == 4:
                case DnsType.Aaaa when length == 16:
                    data = new IPAddress(bytes.AsSpan(pos, length));
                    break;
                default:
                    data = bytes.AsSpan(pos, length).ToArray();
                    break;
            }

            target.Add(new DnsRecord(name, type, (cls & 0x8000) != 0, ttl, data));
            pos = end;
        }
    }

    private static string ReadName(byte[] bytes, ref int pos)
    {
        var labels = new List<string>();
        int cursor = pos;
        bool jumped = false;
        int hops = 0;

        while (true)
        {
            Need(bytes, cursor, 1);
            int len = bytes[cursor];

            if ((len & 0xC0) == 0xC0)
            {
                Need(bytes, cursor, 2);
                int pointer = ((len & 0x3F) << 8) | bytes[cursor + 1];
                if (!jumped) pos = cursor + 2;
                jumped = true;
                if (++hops > 32 || pointer >= bytes.Length) throw new FormatException("Invalid DNS name compression.");
                cursor = pointer;
                continue;
            }

            if (len == 0)
            {
                if (!jumped) pos = cursor + 1;
                break;
            }

            if (len > 63) throw new FormatException("Invalid DNS label length.");
            Need(bytes, cursor + 1, len);
            labels.Add(DnsName.EscapeLabel(Encoding.UTF8.GetString(bytes, cursor + 1, len)));
            cursor += 1 + len;
        }

        return string.Join('.', labels);
    }

    private static void Need(byte[] bytes, int pos, int count)
    {
        if (pos < 0 || count < 0 || pos + count > bytes.Length)
        {
            throw new FormatException("Truncated DNS message.");
        }
    }

    private sealed class Writer
    {
        private readonly MemoryStream _ms = new();
        private readonly Dictionary<string, int> _names = new(StringComparer.OrdinalIgnoreCase);

        public void U16(ushort value)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, value);
            _ms.Write(b);
        }

        public void U32(uint value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, value);
            _ms.Write(b);
        }

        public void Name(string name)
        {
            var labels = DnsName.Split(name);
            for (int i = 0; i < labels.Count; i++)
            {
                var suffix = string.Join('.', labels.Skip(i).Select(DnsName.EscapeLabel));
                if (_names.TryGetValue(suffix, out int offset))
                {
                    U16((ushort)(0xC000 | offset));
                    return;
                }

                if (_ms.Position < 0x3FFF)
                {
                    _names[suffix] = (int)_ms.Position;
                }

                var bytes = Encoding.UTF8.GetBytes(labels[i]);
                if (bytes.Length > 63) bytes = bytes[..63];
                _ms.WriteByte((byte)bytes.Length);
                _ms.Write(bytes);
            }
            _ms.WriteByte(0);
        }

        public void Record(DnsRecord r)
        {
            Name(r.Name);
            U16(r.Type);
            U16((ushort)(1 | (r.CacheFlush ? 0x8000 : 0)));
            U32(r.Ttl);

            // Reserve the rdata length, then patch it after writing (names inside rdata may be compressed)
            long lengthPos = _ms.Position;
            U16(0);
            long start = _ms.Position;

            switch (r.Data)
            {
                case string target:
                    Name(target);
                    break;
                case SrvData srv:
                    U16(srv.Priority);
                    U16(srv.Weight);
                    U16(srv.Port);
                    Name(srv.Target);
                    break;
                case IPAddress ip:
                    _ms.Write(ip.GetAddressBytes());
                    break;
                case byte[] raw:
                    _ms.Write(raw);
                    break;
            }

            long end = _ms.Position;
            _ms.Position = lengthPos;
            U16((ushort)(end - start));
            _ms.Position = end;
        }

        public byte[] ToArray() => _ms.ToArray();
    }
}

/// <summary>Builds TXT record rdata (length-prefixed "key=value" strings).</summary>
public sealed class DnsTxtBuilder
{
    private readonly MemoryStream _ms = new();

    public DnsTxtBuilder Add(string key, string value)
    {
        var bytes = Encoding.UTF8.GetBytes($"{key}={value}");
        if (bytes.Length > 255) bytes = bytes[..255];
        _ms.WriteByte((byte)bytes.Length);
        _ms.Write(bytes);
        return this;
    }

    public byte[] Build()
    {
        // An empty TXT record must still contain a single empty string
        return _ms.Length == 0 ? [0] : _ms.ToArray();
    }
}

/// <summary>Helpers for dotted DNS names whose labels may contain escaped dots ("\.").</summary>
public static class DnsName
{
    public static string EscapeLabel(string label) => label.Replace("\\", "\\\\").Replace(".", "\\.");

    public static List<string> Split(string name)
    {
        var labels = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '\\' && i + 1 < name.Length)
            {
                current.Append(name[++i]);
            }
            else if (c == '.')
            {
                if (current.Length > 0) labels.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0) labels.Add(current.ToString());
        return labels;
    }

    public static bool Equal(string a, string b) =>
        string.Equals(a.TrimEnd('.'), b.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
}
