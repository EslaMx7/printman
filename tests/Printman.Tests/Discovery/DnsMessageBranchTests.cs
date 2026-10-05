using System.Net;
using Printman.Services.Discovery;

namespace Printman.Tests.Discovery;

/// <summary>Branch-focused coverage for DNS record data comparison and record parsing.</summary>
[TestClass]
public sealed class DnsMessageBranchTests
{
    [TestMethod]
    public void SameData_MatchingTypes_ComparesPayloads()
    {
        var a = new DnsRecord("a.local", DnsType.Txt, false, 10, "value");
        var b = new DnsRecord("a.local", DnsType.Txt, false, 20, "VALUE");
        Assert.IsTrue(a.SameData(b));

        var srvA = new DnsRecord("a.local", DnsType.Srv, false, 10, new SrvData(0, 0, 631, "host.local"));
        var srvB = new DnsRecord("a.local", DnsType.Srv, false, 10, new SrvData(1, 2, 631, "host.local"));
        Assert.IsTrue(srvA.SameData(srvB));

        var ipA = new DnsRecord("a.local", DnsType.A, false, 10, IPAddress.Loopback);
        var ipB = new DnsRecord("a.local", DnsType.A, false, 10, IPAddress.Loopback);
        Assert.IsTrue(ipA.SameData(ipB));

        var bytesA = new DnsRecord("a.local", DnsType.Any, false, 10, new byte[] { 1, 2 });
        var bytesB = new DnsRecord("a.local", DnsType.Any, false, 10, new byte[] { 1, 2 });
        Assert.IsTrue(bytesA.SameData(bytesB));
    }

    [TestMethod]
    public void SameData_MismatchedTypes_ReturnsFalse()
    {
        var nameRecord = new DnsRecord("a.local", DnsType.Txt, false, 10, "value");
        var addressRecord = new DnsRecord("a.local", DnsType.A, false, 10, IPAddress.Loopback);

        Assert.IsFalse(nameRecord.SameData(addressRecord));
        Assert.IsFalse(addressRecord.SameData(nameRecord));
    }

    [TestMethod]
    public void Parse_SrvRecordWithShortRdata_FallsBackToRawBytes()
    {
        // SRV requires an rdlength of at least 7; anything shorter must be kept as raw bytes.
        byte[] message =
        [
            0x00, 0x00, 0x84, 0x00,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00,
            0x01, (byte)'a', 0x00,
            0x00, 0x21,
            0x00, 0x01,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x03,
            0x01, 0x02, 0x03
        ];

        var parsed = DnsMessage.Parse(message);

        Assert.AreEqual(1, parsed.Answers.Count);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])parsed.Answers[0].Data);
    }

    [TestMethod]
    public void Parse_AaaaRecord_ReturnsAddress()
    {
        byte[] message =
        [
            0x00, 0x00, 0x84, 0x00,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00,
            0x01, (byte)'a', 0x00,
            0x00, 0x1C,
            0x00, 0x01,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x10,
            0x20, 0x01, 0x0D, 0xB8, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01
        ];

        var parsed = DnsMessage.Parse(message);

        Assert.AreEqual("2001:db8::1", parsed.Answers[0].Data.ToString());
    }

    [TestMethod]
    public void Parse_AddressRecordWithWrongLength_FallsBackToRawBytes()
    {
        // An A record must have exactly 4 rdata bytes; 2 bytes must stay raw.
        byte[] message =
        [
            0x00, 0x00, 0x84, 0x00,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00,
            0x01, (byte)'a', 0x00,
            0x00, 0x01,
            0x00, 0x01,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x02,
            0x0A, 0x0B
        ];

        var parsed = DnsMessage.Parse(message);

        CollectionAssert.AreEqual(new byte[] { 0x0A, 0x0B }, (byte[])parsed.Answers[0].Data);
    }
}
