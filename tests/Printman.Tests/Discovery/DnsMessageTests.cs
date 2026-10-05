using System.Buffers.Binary;
using System.Net;
using System.Text;
using Printman.Services.Discovery;

namespace Printman.Tests.Discovery;

[TestClass]
public sealed class DnsMessageTests
{
    // ------------------------------------------------------------------ DnsType

    [TestMethod]
    public void DnsType_Constants_MatchIanaAssignments()
    {
        ushort[] actual = [DnsType.A, DnsType.Ptr, DnsType.Txt, DnsType.Aaaa, DnsType.Srv, DnsType.Any];
        ushort[] expected = [1, 12, 16, 28, 33, 255];
        CollectionAssert.AreEqual(expected, actual);
    }

    // ------------------------------------------------------------------ DnsQuestion / SrvData

    [TestMethod]
    public void DnsQuestion_ExposesValues()
    {
        var question = new DnsQuestion("_ipp._tcp.local", DnsType.Ptr, UnicastResponse: true);

        Assert.AreEqual("_ipp._tcp.local", question.Name);
        Assert.AreEqual(DnsType.Ptr, question.Type);
        Assert.IsTrue(question.UnicastResponse);
    }

    [TestMethod]
    public void SrvData_ExposesValues()
    {
        var srv = new SrvData(10, 20, 631, "printer.local");

        Assert.AreEqual((ushort)10, srv.Priority);
        Assert.AreEqual((ushort)20, srv.Weight);
        Assert.AreEqual((ushort)631, srv.Port);
        Assert.AreEqual("printer.local", srv.Target);
    }

    // ------------------------------------------------------------------ DnsRecord.WithTtl / SameData

    [TestMethod]
    public void WithTtl_ReturnsCopyAndLeavesOriginalUnchanged()
    {
        var original = new DnsRecord("a.local", DnsType.Txt, false, 120, new byte[] { 1, 2 });

        var updated = original.WithTtl(4500);

        Assert.AreEqual((uint)120, original.Ttl);
        Assert.AreEqual((uint)4500, updated.Ttl);
        Assert.IsTrue(original.SameData(updated));
        Assert.IsFalse(ReferenceEquals(original, updated));
    }

    [TestMethod]
    public void SameData_Strings_ComparesAsDnsNames()
    {
        Assert.IsTrue(Record(DnsType.Ptr, "Printer.Local.").SameData(Record(DnsType.Ptr, "printer.local")));
        Assert.IsFalse(Record(DnsType.Ptr, "a.local").SameData(Record(DnsType.Ptr, "b.local")));
    }

    [TestMethod]
    public void SameData_Srv_ChecksPortAndTargetOnly()
    {
        var baseline = new DnsRecord("_ipp._tcp.local", DnsType.Srv, true, 0, new SrvData(1, 2, 631, "x.local"));

        var sameValues = new DnsRecord("_ipp._tcp.local", DnsType.Srv, true, 0, new SrvData(9, 9, 631, "X.LOCAL."));
        Assert.IsTrue(baseline.SameData(sameValues));

        var differentPort = new DnsRecord("_ipp._tcp.local", DnsType.Srv, true, 0, new SrvData(1, 2, 632, "x.local"));
        Assert.IsFalse(baseline.SameData(differentPort));

        var differentTarget = new DnsRecord("_ipp._tcp.local", DnsType.Srv, true, 0, new SrvData(1, 2, 631, "y.local"));
        Assert.IsFalse(baseline.SameData(differentTarget));
    }

    [TestMethod]
    public void SameData_Addresses_UseAddressEquality()
    {
        Assert.IsTrue(Record(DnsType.A, IPAddress.Parse("192.168.1.5"))
            .SameData(Record(DnsType.A, IPAddress.Parse("192.168.1.5"))));
        Assert.IsFalse(Record(DnsType.A, IPAddress.Parse("192.168.1.5"))
            .SameData(Record(DnsType.A, IPAddress.Parse("192.168.1.6"))));
    }

    [TestMethod]
    public void SameData_RawBytes_UseSequenceEquality()
    {
        Assert.IsTrue(Record(DnsType.Txt, new byte[] { 1, 2, 3 })
            .SameData(Record(DnsType.Txt, new byte[] { 1, 2, 3 })));
        Assert.IsFalse(Record(DnsType.Txt, new byte[] { 1, 2, 3 })
            .SameData(Record(DnsType.Txt, new byte[] { 1, 2, 4 })));
    }

    [TestMethod]
    public void SameData_MismatchedPayloadTypes_AreNotEqual()
    {
        Assert.IsFalse(Record(DnsType.Ptr, "x.local").SameData(Record(DnsType.Txt, new byte[] { 0 })));
        Assert.IsFalse(Record(DnsType.A, IPAddress.Loopback).SameData(Record(DnsType.Srv, new SrvData(0, 0, 1, "x"))));
        Assert.IsFalse(Record(DnsType.Txt, new byte[] { 0 }).SameData(Record(DnsType.Txt, 42)));
    }

    // ------------------------------------------------------------------ Header helpers

    [TestMethod]
    public void Response_SetsResponseAndAuthoritativeFlags()
    {
        var message = DnsMessage.Response();

        Assert.AreEqual((ushort)0x8400, message.Flags);
        Assert.IsTrue(message.IsResponse);
        Assert.AreEqual(0, message.Questions.Count);
        Assert.AreEqual(0, message.Answers.Count);
        Assert.AreEqual(0, message.Authorities.Count);
        Assert.AreEqual(0, message.Additionals.Count);
    }

    [TestMethod]
    public void IsResponse_ReflectsQrBit()
    {
        var message = new DnsMessage();
        Assert.IsFalse(message.IsResponse);

        message.Flags = 0x8000;
        Assert.IsTrue(message.IsResponse);

        message.Flags = 0x7FFF;
        Assert.IsFalse(message.IsResponse);
    }

    [TestMethod]
    public void Txt_ReturnsUsableBuilder()
    {
        CollectionAssert.AreEqual(new byte[] { 0 }, DnsMessage.Txt().Build());
    }

    // ------------------------------------------------------------------ Encode / Parse

    [TestMethod]
    public void Encode_NoQuestionsOrRecords_ProducesBareHeader()
    {
        var message = new DnsMessage { Id = 0xABCD, Flags = 0x0100 };

        var parsed = DnsMessage.Parse(message.Encode());

        Assert.AreEqual((ushort)0xABCD, parsed.Id);
        Assert.AreEqual((ushort)0x0100, parsed.Flags);
        Assert.AreEqual(0, parsed.Questions.Count);
        Assert.AreEqual(0, parsed.Answers.Count);
    }

    [TestMethod]
    public void Encode_EmptyName_WritesRootLabel()
    {
        var message = new DnsMessage();
        message.Questions.Add(new DnsQuestion(string.Empty, DnsType.Any, UnicastResponse: false));

        var parsed = DnsMessage.Parse(message.Encode());

        Assert.AreEqual(string.Empty, parsed.Questions[0].Name);
        Assert.AreEqual(DnsType.Any, parsed.Questions[0].Type);
        Assert.IsFalse(parsed.Questions[0].UnicastResponse);
    }

    [TestMethod]
    public void Encode_TrailingDot_IsNormalizedAway()
    {
        var message = new DnsMessage();
        message.Questions.Add(new DnsQuestion("example.com.", DnsType.A, UnicastResponse: false));

        var parsed = DnsMessage.Parse(message.Encode());

        Assert.AreEqual("example.com", parsed.Questions[0].Name);
    }

    [TestMethod]
    public void Encode_LongLabel_IsTruncatedTo63Bytes()
    {
        var label = new string('a', 70);
        var message = new DnsMessage();
        message.Answers.Add(new DnsRecord(label + ".local", DnsType.Txt, false, 1, new byte[] { 1, 2 }));

        var parsed = DnsMessage.Parse(message.Encode());

        Assert.AreEqual(63, parsed.Answers[0].Name.Split('.')[0].Length);
        Assert.AreEqual("local", parsed.Answers[0].Name.Split('.')[1]);
    }

    [TestMethod]
    public void Encode_LargeMessage_WritesNewNamesWithoutCompressionTargetsPastGuard()
    {
        var message = new DnsMessage();
        message.Answers.Add(new DnsRecord("a.local", DnsType.Txt, false, 1, new byte[20000]));
        message.Answers.Add(new DnsRecord("b.local", DnsType.Txt, false, 1, new byte[] { 1 }));

        var parsed = DnsMessage.Parse(message.Encode());

        Assert.AreEqual(20000, ((byte[])parsed.Answers[0].Data).Length);
        Assert.AreEqual("a.local", parsed.Answers[0].Name);
        Assert.AreEqual(1, ((byte[])parsed.Answers[1].Data).Length);
        Assert.AreEqual("b.local", parsed.Answers[1].Name);
    }

    [TestMethod]
    public void Encode_UnknownRdataType_WritesZeroLengthRdata()
    {
        var message = new DnsMessage();
        message.Answers.Add(new DnsRecord("x.local", DnsType.Txt, false, 5, 12345));

        var parsed = DnsMessage.Parse(message.Encode());

        Assert.AreEqual(DnsType.Txt, parsed.Answers[0].Type);
        Assert.AreEqual(0, ((byte[])parsed.Answers[0].Data).Length);
    }

    [TestMethod]
    public void EncodeParse_RoundTripsHeaderQuestionAndEverySection()
    {
        var txt = DnsMessage.Txt().Add("rp", "ipp/print").Add("ty", "Printman Test");
        var message = DnsMessage.Response();
        message.Id = 0x1234;
        message.Questions.Add(new DnsQuestion("_ipp._tcp.local", DnsType.Ptr, UnicastResponse: true));
        message.Questions.Add(new DnsQuestion("printer.local", DnsType.Aaaa, UnicastResponse: false));
        message.Answers.Add(new DnsRecord("_ipp._tcp.local", DnsType.Ptr, true, 4500, "printer.local."));
        message.Answers.Add(new DnsRecord("printer.local", DnsType.Srv, true, 120, new SrvData(10, 20, 631, "printer.local.")));
        message.Answers.Add(new DnsRecord("printer.local", DnsType.A, true, 120, IPAddress.Parse("192.168.1.10")));
        message.Answers.Add(new DnsRecord("printer.local", DnsType.Aaaa, true, 120, IPAddress.Parse("fe80::1")));
        message.Answers.Add(new DnsRecord("printer.local", DnsType.Txt, true, 4500, txt.Build()));
        message.Authorities.Add(new DnsRecord("local", DnsType.Ptr, false, 120, "ns.local"));
        message.Additionals.Add(new DnsRecord("host.local", DnsType.A, false, 60, IPAddress.Parse("10.0.0.1")));

        var parsed = DnsMessage.Parse(message.Encode());

        Assert.AreEqual((ushort)0x1234, parsed.Id);
        Assert.AreEqual((ushort)0x8400, parsed.Flags);
        Assert.IsTrue(parsed.IsResponse);
        Assert.AreEqual(2, parsed.Questions.Count);
        Assert.IsTrue(parsed.Questions[0].UnicastResponse);
        Assert.IsFalse(parsed.Questions[1].UnicastResponse);
        Assert.AreEqual(5, parsed.Answers.Count);
        Assert.AreEqual(1, parsed.Authorities.Count);
        Assert.AreEqual(1, parsed.Additionals.Count);

        for (int i = 0; i < message.Answers.Count; i++)
        {
            Assert.IsTrue(message.Answers[i].SameData(parsed.Answers[i]), $"Answer {i} did not round-trip.");
        }

        Assert.IsTrue(message.Authorities[0].SameData(parsed.Authorities[0]));
        Assert.IsTrue(message.Additionals[0].SameData(parsed.Additionals[0]));
    }

    // ------------------------------------------------------------------ Parse malformed / truncated

    [TestMethod]
    public void Parse_ShortHeader_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(new byte[11]));
        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(Array.Empty<byte>()));
    }

    [TestMethod]
    public void Parse_HeaderOnly_ReturnsEmptyMessage()
    {
        var parsed = DnsMessage.Parse(Header());

        Assert.AreEqual((ushort)0, parsed.Id);
        Assert.AreEqual((ushort)0, parsed.Flags);
        Assert.AreEqual(0, parsed.Questions.Count);
        Assert.AreEqual(0, parsed.Answers.Count);
        Assert.AreEqual(0, parsed.Authorities.Count);
        Assert.AreEqual(0, parsed.Additionals.Count);
    }

    [TestMethod]
    public void Parse_QuestionWithoutName_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(Header(qd: 1)));
    }

    [TestMethod]
    public void Parse_QuestionWithoutClass_Throws()
    {
        var bytes = Concat(Header(qd: 1), Name("a"), U16(DnsType.A));

        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(bytes));
    }

    [TestMethod]
    public void Parse_RecordWithoutFixedFields_Throws()
    {
        var bytes = Concat(Header(an: 1), Name("a"));

        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(bytes));
    }

    [TestMethod]
    public void Parse_TruncatedRdata_Throws()
    {
        var bytes = Concat(
            Header(an: 1),
            Name("a"),
            U16(DnsType.A),
            U16(1),
            U32(0),
            U16(4),
            new byte[] { 1, 2 });

        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(bytes));
    }

    // ------------------------------------------------------------------ Parse rdata variants

    [TestMethod]
    public void Parse_AddressRecordsWithWrongLength_FallBackToRawBytes()
    {
        var bytes = Concat(
            Header(an: 2),
            RawRecord(Name("a.local"), DnsType.A, 1, 30, new byte[] { 1, 2, 3 }),
            RawRecord(Name("b.local"), DnsType.Aaaa, 1, 30, new byte[] { 1, 2, 3, 4 }));

        var parsed = DnsMessage.Parse(bytes);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])parsed.Answers[0].Data);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, (byte[])parsed.Answers[1].Data);
    }

    [TestMethod]
    public void Parse_SrvRecordWithShortRdata_FallsBackToRawBytes()
    {
        var bytes = Concat(
            Header(an: 1),
            RawRecord(Name("svc"), DnsType.Srv, 0x8001, 120, new byte[] { 1, 2, 3 }));

        var parsed = DnsMessage.Parse(bytes);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])parsed.Answers[0].Data);
        Assert.IsTrue(parsed.Answers[0].CacheFlush);
    }

    [TestMethod]
    public void Parse_SrvRecordWithMinimumLength_ReadsEmptyTarget()
    {
        var rdata = new byte[] { 0x00, 0x01, 0x00, 0x02, 0x02, 0x7B, 0x00 };
        var bytes = Concat(
            Header(an: 1),
            RawRecord(Name("svc"), DnsType.Srv, 0x8001, 5, rdata));

        var parsed = DnsMessage.Parse(bytes);

        var srv = (SrvData)parsed.Answers[0].Data;
        Assert.AreEqual((ushort)1, srv.Priority);
        Assert.AreEqual((ushort)2, srv.Weight);
        Assert.AreEqual((ushort)635, srv.Port);
        Assert.AreEqual(string.Empty, srv.Target);
    }

    // ------------------------------------------------------------------ ReadName compression

    [TestMethod]
    public void Parse_CompressedNames_ResolveAgainstEarlierOffsets()
    {
        var bytes = Concat(
            Header(an: 2),
            RawRecord(Name("printer.local"), DnsType.Ptr, 0, 0, new byte[] { 0xC0, 0x0C }),
            RawRecord(new byte[] { 0xC0, 0x0C }, DnsType.A, 1, 0, new byte[] { 192, 168, 1, 1 }));

        var parsed = DnsMessage.Parse(bytes);

        Assert.AreEqual("printer.local", parsed.Answers[0].Name);
        Assert.AreEqual("printer.local", (string)parsed.Answers[0].Data);
        Assert.AreEqual("printer.local", parsed.Answers[1].Name);
        Assert.AreEqual(IPAddress.Parse("192.168.1.1"), (IPAddress)parsed.Answers[1].Data);
    }

    [TestMethod]
    public void Parse_ChainedCompressionPointers_FollowEveryHop()
    {
        // offset 12: name "a"; rdata at 25 points to 27, which points to 29 ("x").
        var bytes = Concat(
            Header(an: 1),
            Name("a"),                 // 12..14
            U16(DnsType.Ptr),          // 15..16
            U16(1),                    // 17..18
            U32(0),                    // 19..22
            U16(2),                    // 23..24
            new byte[] { 0xC0, 27 },   // 25..26
            new byte[] { 0xC0, 29 },   // 27..28
            Name("x"));                // 29..31

        var parsed = DnsMessage.Parse(bytes);

        Assert.AreEqual("a", parsed.Answers[0].Name);
        Assert.AreEqual("x", (string)parsed.Answers[0].Data);
    }

    [TestMethod]
    public void Parse_SelfReferentialPointer_Throws()
    {
        var bytes = Concat(Header(an: 1), new byte[] { 0xC0, 0x0C });

        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(bytes));
    }

    [TestMethod]
    public void Parse_PointerBeyondBuffer_Throws()
    {
        var bytes = Concat(Header(an: 1), new byte[] { 0xC0, 0xFF });

        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(bytes));
    }

    [TestMethod]
    public void Parse_LabelLongerThan63_Throws()
    {
        var bytes = Concat(Header(an: 1), new byte[] { 64 }, new byte[64], new byte[] { 0 });

        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(bytes));
    }

    [TestMethod]
    public void Parse_TruncatedLabel_Throws()
    {
        var bytes = Concat(Header(an: 1), new byte[] { 5, (byte)'a' });

        Assert.ThrowsExactly<FormatException>(() => DnsMessage.Parse(bytes));
    }

    // ------------------------------------------------------------------ DnsTxtBuilder

    [TestMethod]
    public void DnsTxtBuilder_Add_EncodesLengthPrefixedUtf8()
    {
        var data = DnsMessage.Txt().Add("rp", "ipp/print").Build();

        Assert.AreEqual((byte)"rp=ipp/print".Length, data[0]);
        Assert.AreEqual("rp=ipp/print", Encoding.UTF8.GetString(data, 1, data[0]));
    }

    [TestMethod]
    public void DnsTxtBuilder_Add_TruncatesValuesLongerThan255Bytes()
    {
        var data = DnsMessage.Txt().Add("k", new string('v', 300)).Build();

        Assert.AreEqual((byte)255, data[0]);
        Assert.AreEqual(256, data.Length);
    }

    [TestMethod]
    public void DnsTxtBuilder_Add_ReturnsSameInstanceForChaining()
    {
        var builder = DnsMessage.Txt();

        Assert.AreSame(builder, builder.Add("a", "1"));
    }

    [TestMethod]
    public void DnsTxtBuilder_Build_Empty_ReturnsSingleEmptyString()
    {
        CollectionAssert.AreEqual(new byte[] { 0 }, DnsMessage.Txt().Build());
    }

    // ------------------------------------------------------------------ DnsName

    [TestMethod]
    [DataRow("plain", "plain")]
    [DataRow("a.b", @"a\.b")]
    [DataRow(@"a\b", @"a\\b")]
    public void DnsName_EscapeLabel_EscapesBackslashAndDot(string label, string expected) =>
        Assert.AreEqual(expected, DnsName.EscapeLabel(label));

    [TestMethod]
    public void DnsName_Split_HandlesEscapesEmptyLabelsAndTrailingDots()
    {
        CollectionAssert.AreEqual(new[] { "a.b", "c" }, DnsName.Split(@"a\.b.c").ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b" }, DnsName.Split("a..b").ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b" }, DnsName.Split("a.b.").ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, DnsName.Split(".a").ToArray());
        CollectionAssert.AreEqual(new[] { "a\\b" }, DnsName.Split(@"a\\b").ToArray());
        CollectionAssert.AreEqual(new[] { "a\\" }, DnsName.Split("a\\").ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), DnsName.Split(string.Empty).ToArray());
    }

    [TestMethod]
    [DataRow("a", "a", true)]
    [DataRow("Printer.Local.", "printer.local", true)]
    [DataRow("a..", "a", true)]
    [DataRow("a", "b", false)]
    public void DnsName_Equal_IgnoresCaseAndTrailingDots(string a, string b, bool expected) =>
        Assert.AreEqual(expected, DnsName.Equal(a, b));

    // ------------------------------------------------------------------ Raw DNS helpers

    private static DnsRecord Record(ushort type, object data) =>
        new("name.local", type, false, 120, data);

    private static byte[] Header(int qd = 0, int an = 0, int ns = 0, int ar = 0)
    {
        var header = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)qd);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)an);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)ns);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)ar);
        return header;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        int offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static byte[] Name(string dotted)
    {
        var bytes = new List<byte>();
        foreach (var label in dotted.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var encoded = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)encoded.Length);
            bytes.AddRange(encoded);
        }

        bytes.Add(0);
        return bytes.ToArray();
    }

    private static byte[] U16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] RawRecord(byte[] name, ushort type, ushort cls, uint ttl, byte[] rdata) =>
        Concat(name, U16(type), U16(cls), U32(ttl), U16(rdata.Length), rdata);
}
