using System.Buffers.Binary;
using System.Text;
using Printman.Core.Models;

namespace Printman.Services.Ipp;

/// <summary>
/// Encodes an <see cref="IppMessage"/> into application/ipp wire format (RFC 8010).
/// </summary>
public static class IppMessageWriter
{
    public static byte[] Write(IppMessage message)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(message.VersionMajor);
        ms.WriteByte(message.VersionMinor);
        WriteInt16(ms, message.Code);
        WriteInt32(ms, message.RequestId);

        foreach (var group in message.Groups)
        {
            ms.WriteByte((byte)group.Tag);
            foreach (var attr in group.Attributes)
            {
                WriteAttribute(ms, attr);
            }
        }

        ms.WriteByte((byte)IppTag.EndOfAttributes);
        return ms.ToArray();
    }

    private static void WriteAttribute(Stream s, IppAttribute attr)
    {
        bool first = true;
        foreach (var value in attr.Values)
        {
            WriteValue(s, first ? attr.Name : "", value);
            first = false;
        }
    }

    private static void WriteValue(Stream s, string name, IppValue value)
    {
        if (value.Tag == IppTag.BegCollection && value.Value is IppCollection collection)
        {
            WriteHeader(s, IppTag.BegCollection, name);
            WriteInt16(s, 0);

            foreach (var member in collection.Attributes)
            {
                WriteHeader(s, IppTag.MemberAttrName, "");
                WriteBytes(s, Encoding.UTF8.GetBytes(member.Name));
                foreach (var memberValue in member.Values)
                {
                    WriteValue(s, "", memberValue);
                }
            }

            WriteHeader(s, IppTag.EndCollection, "");
            WriteInt16(s, 0);
            return;
        }

        WriteHeader(s, value.Tag, name);
        WriteBytes(s, Encode(value));
    }

    private static byte[] Encode(IppValue value)
    {
        switch (value.Value)
        {
            case null:
                return [];
            case int i:
                var ib = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(ib, i);
                return ib;
            case bool b:
                return [(byte)(b ? 1 : 0)];
            case IppResolution r:
                var rb = new byte[9];
                BinaryPrimitives.WriteInt32BigEndian(rb, r.CrossFeed);
                BinaryPrimitives.WriteInt32BigEndian(rb.AsSpan(4), r.Feed);
                rb[8] = r.Units;
                return rb;
            case IppRange range:
                var gb = new byte[8];
                BinaryPrimitives.WriteInt32BigEndian(gb, range.Lower);
                BinaryPrimitives.WriteInt32BigEndian(gb.AsSpan(4), range.Upper);
                return gb;
            case byte[] raw:
                return raw;
            case string str when value.Tag is IppTag.TextWithLanguage or IppTag.NameWithLanguage:
                var lang = Encoding.UTF8.GetBytes("en");
                var text = Encoding.UTF8.GetBytes(str);
                var lb = new byte[4 + lang.Length + text.Length];
                BinaryPrimitives.WriteUInt16BigEndian(lb, (ushort)lang.Length);
                lang.CopyTo(lb, 2);
                BinaryPrimitives.WriteUInt16BigEndian(lb.AsSpan(2 + lang.Length), (ushort)text.Length);
                text.CopyTo(lb, 4 + lang.Length);
                return lb;
            case string str:
                return Encoding.UTF8.GetBytes(str);
            default:
                throw new InvalidOperationException($"Unsupported IPP value type '{value.Value.GetType().Name}'.");
        }
    }

    private static void WriteHeader(Stream s, IppTag tag, string name)
    {
        s.WriteByte((byte)tag);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        WriteInt16(s, (short)nameBytes.Length);
        s.Write(nameBytes);
    }

    private static void WriteBytes(Stream s, byte[] data)
    {
        if (data.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("IPP value exceeds 65535 bytes.");
        }
        WriteInt16(s, unchecked((short)data.Length));
        s.Write(data);
    }

    private static void WriteInt16(Stream s, short value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, value);
        s.Write(b);
    }

    private static void WriteInt32(Stream s, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, value);
        s.Write(b);
    }
}
