using System.Text;
using Printman.Core.Models;
using Printman.Services.Ipp;

namespace Printman.Tests.Ipp;

[TestClass]
public sealed class IppMessageCodecEdgeTests
{
    // ---------------------------------------------------------------------
    // Out-of-band and raw value tags
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Reader_OutOfBandTags_DecodeToNull()
    {
        var body = Concat(
            [0x01],
            Attr(0x10, "unsupported", []),
            Attr(0x12, "unknown", []),
            Attr(0x13, "no-value", []));

        var parsed = IppMessageReader.Parse(BuildTerminated(body));
        var group = parsed.Group(IppTag.OperationAttributes);

        Assert.IsNotNull(group);
        Assert.IsNull(group.Get("unsupported")!.First!.Value);
        Assert.IsNull(group.Get("unknown")!.First!.Value);
        Assert.IsNull(group.Get("no-value")!.First!.Value);
        Assert.AreEqual(IppTag.NoValue, group.Get("no-value")!.First!.Tag);
    }

    [TestMethod]
    public void Reader_UnassignedValueTag_FallsBackToRawBytes()
    {
        // 0x2F is not a defined value tag and not a character/out-of-band tag.
        var payload = new byte[] { 0xDE, 0xAD };
        var parsed = IppMessageReader.Parse(BuildTerminated(Concat([0x01], Attr(0x2F, "raw", payload))));

        var value = parsed.Group(IppTag.OperationAttributes)!.Get("raw")!.First!;

        Assert.AreEqual((IppTag)0x2F, value.Tag);
        CollectionAssert.AreEqual(payload, (byte[])value.Value!);
    }

    [TestMethod]
    public void Reader_HeaderOnly_EndsBeforeTag_Throws()
    {
        // The attribute loop reads the group/value tags one byte at a time; a stream that ends
        // right after the header must surface as a parse error rather than an EndOfStreamException.
        byte[] headerOnly = [0x02, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01];

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(headerOnly));
    }

    // ---------------------------------------------------------------------
    // Structured value round-trips
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Reader_TextWithLanguage_RoundTrips()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        var op = message.AddGroup(IppTag.OperationAttributes);
        var attr = new IppAttribute("text-lang");
        attr.Values.Add(new IppValue(IppTag.TextWithLanguage, "hello"));
        op.Attributes.Add(attr);

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));
        var value = parsed.FindRequestAttribute("text-lang")!.First!;

        Assert.AreEqual(IppTag.TextWithLanguage, value.Tag);
        Assert.AreEqual("hello", value.AsString());
    }

    [TestMethod]
    public void Reader_NameWithLanguage_RoundTrips()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        var op = message.AddGroup(IppTag.OperationAttributes);
        var attr = new IppAttribute("name-lang");
        attr.Values.Add(new IppValue(IppTag.NameWithLanguage, "printer-name"));
        op.Attributes.Add(attr);

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));
        var value = parsed.FindRequestAttribute("name-lang")!.First!;

        Assert.AreEqual(IppTag.NameWithLanguage, value.Tag);
        Assert.AreEqual("printer-name", value.AsString());
    }

    [TestMethod]
    public void Reader_DateTime_RoundTripsAsBytes()
    {
        var when = new DateTimeOffset(2024, 1, 2, 3, 4, 5, 600, TimeSpan.FromHours(-5));
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes).AddDateTime("date-time-at-creation", when);

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));
        var value = parsed.FindRequestAttribute("date-time-at-creation")!.First!;

        Assert.AreEqual(IppTag.DateTime, value.Tag);
        CollectionAssert.AreEqual(IppAttributeList.EncodeDateTime(when), (byte[])value.Value!);
    }

    [TestMethod]
    public void Reader_ResolutionRangeAndOctetString_RoundTrip()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        var op = message.AddGroup(IppTag.OperationAttributes);
        op.AddResolution("printer-resolution", new IppResolution(600, 1200, 3));
        op.AddRange("copies-range", 1, 5);
        var octet = new IppAttribute("blob");
        octet.Values.Add(new IppValue(IppTag.OctetString, new byte[] { 0x01, 0x02, 0x03 }));
        op.Attributes.Add(octet);

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));
        var group = parsed.Group(IppTag.OperationAttributes)!;

        var resolution = (IppResolution)group.Get("printer-resolution")!.First!.Value!;
        Assert.AreEqual(600, resolution.CrossFeed);
        Assert.AreEqual(1200, resolution.Feed);
        Assert.AreEqual(3, resolution.Units);

        var range = (IppRange)group.Get("copies-range")!.First!.Value!;
        Assert.AreEqual(1, range.Lower);
        Assert.AreEqual(5, range.Upper);

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03 }, (byte[])group.Get("blob")!.First!.Value!);
    }

    [TestMethod]
    public void Reader_MultipleValuesOnOneAttribute_KeepOrder()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes).AddInteger("values", 1, 2, 3);

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));
        var attribute = parsed.FindRequestAttribute("values")!;

        CollectionAssert.AreEqual(new int?[] { 1, 2, 3 }, attribute.Values.Select(v => v.AsInt()).ToArray());
    }

    [TestMethod]
    public void Reader_EnumerationTags_DecodeAsIntegers()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes).AddEnum("an-enum", 4);

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));
        var value = parsed.FindRequestAttribute("an-enum")!.First!;

        Assert.AreEqual(IppTag.Enum, value.Tag);
        Assert.AreEqual(4, value.AsInt());
    }

    // ---------------------------------------------------------------------
    // Malformed input
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Reader_MaxCollectionDepth_Throws()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        var op = message.AddGroup(IppTag.OperationAttributes);
        op.AddCollection("root", BuildNested(9));

        var bytes = IppMessageWriter.Write(message);

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(bytes));
    }

    [TestMethod]
    public void Reader_UnterminatedCollection_Throws()
    {
        // Begin a collection, close it with a group tag instead of end-collection.
        var body = Concat([0x01], Attr(0x34, "root", []), [0x01]);

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_ValueWithoutAttribute_Throws()
    {
        var body = Concat([0x01], Attr(0x41, "", [(byte)'y']));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_ExtendedValueTag_Throws()
    {
        var body = Concat([0x01], Attr(0x7F, "extended", []));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_MemberValueWithoutName_Throws()
    {
        // Begin collection, then an integer value tag without a preceding member-attr-name.
        var body = Concat(
            [0x01],
            [0x34, 0x00, 0x01, (byte)'c', 0x00, 0x00],
            [0x21, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x01]);

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_InvalidIntegerLength_Throws()
    {
        var body = Concat([0x01], Attr(0x21, "i", [0x00, 0x01]));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_InvalidBooleanLength_Throws()
    {
        var body = Concat([0x01], Attr(0x22, "b", [0x01, 0x02]));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_InvalidResolutionLength_Throws()
    {
        var body = Concat([0x01], Attr(0x32, "r", new byte[8]));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_InvalidRangeLength_Throws()
    {
        var body = Concat([0x01], Attr(0x33, "g", new byte[7]));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_StringWithLanguage_ShortFrame_Throws()
    {
        var body = Concat([0x01], Attr(0x35, "t", [0x00]));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_StringWithLanguage_OverlongLanguage_Throws()
    {
        var data = new byte[] { 0x00, 0x05, (byte)'e', (byte)'n' };
        var body = Concat([0x01], Attr(0x35, "t", data));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    [TestMethod]
    public void Reader_StringWithLanguage_OverlongText_Throws()
    {
        var data = new byte[] { 0x00, 0x02, (byte)'e', (byte)'n', 0x00, 0x05, (byte)'a' };
        var body = Concat([0x01], Attr(0x35, "t", data));

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(Build(body)));
    }

    // ---------------------------------------------------------------------
    // Writer limits and unsupported values
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Writer_ValueAtUInt16Limit_DoesNotThrow()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes).AddName("blob", new string('a', ushort.MaxValue));

        var bytes = IppMessageWriter.Write(message);

        Assert.IsTrue(bytes.Length > ushort.MaxValue);
    }

    [TestMethod]
    public void Writer_UnsupportedValueType_Throws()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        var attribute = new IppAttribute("bad");
        attribute.Values.Add(new IppValue(IppTag.OctetString, 3.14));
        message.AddGroup(IppTag.OperationAttributes).Attributes.Add(attribute);

        Assert.ThrowsExactly<InvalidOperationException>(() => IppMessageWriter.Write(message));
    }

    [TestMethod]
    public void Writer_OutOfBandValue_RoundTripsAsNull()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes).AddNoValue("out-of-band");

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));
        var value = parsed.FindRequestAttribute("out-of-band")!.First!;

        Assert.AreEqual(IppTag.NoValue, value.Tag);
        Assert.IsNull(value.Value);
    }

    [TestMethod]
    public void Writer_EmptyValueList_WritesNoValueFrame()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes).Attributes.Add(new IppAttribute("empty"));

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));

        // No value frames were written, so the attribute is absent after the round-trip.
        Assert.IsNull(parsed.FindRequestAttribute("empty"));
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static IppCollection BuildNested(int depth)
    {
        var collection = new IppCollection();
        if (depth <= 1)
        {
            collection.AddInteger("leaf", 1);
        }
        else
        {
            collection.AddCollection("child", BuildNested(depth - 1));
        }

        return collection;
    }

    private static byte[] Build(byte[] body)
    {
        byte[] header = [0x02, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01];
        return Concat(header, body);
    }

    /// <summary>Builds a well-formed message (header + body + end-of-attributes tag).</summary>
    private static byte[] BuildTerminated(byte[] body) =>
        Concat(Build(body), [0x03]);

    private static byte[] Attr(byte tag, string name, byte[] value)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        using var ms = new MemoryStream();
        ms.WriteByte(tag);
        WriteU16(ms, nameBytes.Length);
        ms.Write(nameBytes);
        WriteU16(ms, value.Length);
        ms.Write(value);
        return ms.ToArray();
    }

    private static void WriteU16(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(p => p.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
