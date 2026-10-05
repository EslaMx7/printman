using System.Buffers.Binary;
using System.Text;
using Printman.Core.Models;

namespace Printman.Services.Ipp;

/// <summary>
/// Parses the attribute section of an application/ipp message (RFC 8010).
/// Reading stops right after the end-of-attributes tag, so any document data
/// remains unread in the source stream.
/// </summary>
public static class IppMessageReader
{
    public const int DefaultMaxAttributeBytes = 64 * 1024;
    private const int MaxCollectionDepth = 8;

    public static async Task<IppMessage> ReadAsync(Stream stream, int maxBytes = DefaultMaxAttributeBytes, CancellationToken ct = default)
    {
        var cursor = new Cursor(stream, maxBytes, ct);
        var header = await cursor.ReadAsync(8);

        var message = new IppMessage
        {
            VersionMajor = header[0],
            VersionMinor = header[1],
            Code = BinaryPrimitives.ReadInt16BigEndian(header.AsSpan(2)),
            RequestId = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4))
        };

        IppAttributeGroup? group = null;
        IppAttribute? current = null;

        while (true)
        {
            byte tag = await cursor.ReadByteAsync();

            if (tag == (byte)IppTag.EndOfAttributes)
            {
                break;
            }

            if (tag < 0x10)
            {
                group = message.AddGroup((IppTag)tag);
                current = null;
                continue;
            }

            if (group == null)
            {
                throw new IppParseException("Attribute found before the first attribute group.");
            }

            var (name, value) = await ReadAttributeAsync(cursor, tag, depth: 0);

            if (name.Length == 0)
            {
                if (current == null)
                {
                    throw new IppParseException("Additional value without a preceding attribute.");
                }
                current.Values.Add(value);
            }
            else
            {
                current = new IppAttribute(name);
                current.Values.Add(value);
                group.Attributes.Add(current);
            }
        }

        return message;
    }

    public static IppMessage Parse(byte[] data) =>
        ReadAsync(new MemoryStream(data, writable: false), int.MaxValue).GetAwaiter().GetResult();

    private static async Task<(string Name, IppValue Value)> ReadAttributeAsync(Cursor cursor, byte tag, int depth)
    {
        if (tag == 0x7F)
        {
            throw new IppParseException("Extended value tags are not supported.");
        }

        int nameLength = await cursor.ReadUInt16Async();
        var name = nameLength > 0 ? Encoding.UTF8.GetString(await cursor.ReadAsync(nameLength)) : "";
        int valueLength = await cursor.ReadUInt16Async();
        var data = valueLength > 0 ? await cursor.ReadAsync(valueLength) : [];

        if (tag == (byte)IppTag.BegCollection)
        {
            if (depth >= MaxCollectionDepth)
            {
                throw new IppParseException("Collections are nested too deeply.");
            }
            var collection = await ReadCollectionAsync(cursor, depth + 1);
            return (name, new IppValue(IppTag.BegCollection, collection));
        }

        return (name, Decode((IppTag)tag, data));
    }

    private static async Task<IppCollection> ReadCollectionAsync(Cursor cursor, int depth)
    {
        var collection = new IppCollection();
        IppAttribute? member = null;

        while (true)
        {
            byte tag = await cursor.ReadByteAsync();
            if (tag < 0x10)
            {
                throw new IppParseException("Unterminated collection.");
            }

            var (_, value) = await ReadAttributeAsync(cursor, tag, depth);

            if (tag == (byte)IppTag.EndCollection)
            {
                return collection;
            }

            if (tag == (byte)IppTag.MemberAttrName)
            {
                member = new IppAttribute(value.AsString() ?? "");
                collection.Attributes.Add(member);
                continue;
            }

            if (member == null)
            {
                throw new IppParseException("Collection value without a member name.");
            }
            member.Values.Add(value);
        }
    }

    private static IppValue Decode(IppTag tag, byte[] data)
    {
        switch (tag)
        {
            case IppTag.Integer or IppTag.Enum:
                if (data.Length != 4) throw new IppParseException($"Invalid integer length {data.Length}.");
                return new IppValue(tag, BinaryPrimitives.ReadInt32BigEndian(data));

            case IppTag.Boolean:
                if (data.Length != 1) throw new IppParseException("Invalid boolean length.");
                return new IppValue(tag, data[0] != 0);

            case IppTag.Resolution:
                if (data.Length != 9) throw new IppParseException("Invalid resolution length.");
                return new IppValue(tag, new IppResolution(
                    BinaryPrimitives.ReadInt32BigEndian(data),
                    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4)),
                    data[8]));

            case IppTag.RangeOfInteger:
                if (data.Length != 8) throw new IppParseException("Invalid rangeOfInteger length.");
                return new IppValue(tag, new IppRange(
                    BinaryPrimitives.ReadInt32BigEndian(data),
                    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4))));

            case IppTag.TextWithLanguage or IppTag.NameWithLanguage:
                // language-length, language, text-length, text
                if (data.Length < 4) throw new IppParseException("Invalid string-with-language value.");
                int langLength = BinaryPrimitives.ReadUInt16BigEndian(data);
                if (2 + langLength + 2 > data.Length) throw new IppParseException("Invalid string-with-language value.");
                int textLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2 + langLength));
                if (4 + langLength + textLength > data.Length) throw new IppParseException("Invalid string-with-language value.");
                return new IppValue(tag, Encoding.UTF8.GetString(data, 4 + langLength, textLength));

            case IppTag.DateTime or IppTag.OctetString:
                return new IppValue(tag, data);

            default:
                if ((byte)tag is >= 0x10 and <= 0x1F)
                {
                    return new IppValue(tag, null); // out-of-band
                }
                if ((byte)tag >= 0x40)
                {
                    return new IppValue(tag, Encoding.UTF8.GetString(data));
                }
                return new IppValue(tag, data);
        }
    }

    private sealed class Cursor(Stream stream, int maxBytes, CancellationToken ct)
    {
        private readonly byte[] _single = new byte[1];
        private int _consumed;

        public async Task<byte[]> ReadAsync(int count)
        {
            Account(count);
            var buffer = new byte[count];
            try
            {
                await stream.ReadExactlyAsync(buffer, ct);
            }
            catch (EndOfStreamException)
            {
                throw new IppParseException("Unexpected end of IPP message.");
            }
            return buffer;
        }

        public async Task<byte> ReadByteAsync()
        {
            Account(1);
            try
            {
                await stream.ReadExactlyAsync(_single, ct);
            }
            catch (EndOfStreamException)
            {
                throw new IppParseException("Unexpected end of IPP message.");
            }
            return _single[0];
        }

        public async Task<int> ReadUInt16Async()
        {
            var b = await ReadAsync(2);
            return BinaryPrimitives.ReadUInt16BigEndian(b);
        }

        private void Account(int count)
        {
            _consumed += count;
            if (_consumed > maxBytes)
            {
                throw new IppParseException("IPP attribute section is too large.");
            }
        }
    }
}
