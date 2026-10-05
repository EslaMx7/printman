using System.Text;
using Printman.Core.Models;
using Printman.Services.Ipp;

namespace Printman.Tests.Ipp;

[TestClass]
public sealed class IppMessageCodecTests
{
    [TestMethod]
    public void RoundTrip_PreservesHeaderAndAttributeValues()
    {
        var message = new IppMessage
        {
            VersionMajor = 2,
            VersionMinor = 0,
            Code = IppOperation.PrintJob,
            RequestId = 42
        };

        var op = message.AddGroup(IppTag.OperationAttributes);
        op.AddCharset("attributes-charset", "utf-8");
        op.AddLanguage("attributes-natural-language", "en");
        op.AddUri("printer-uri", "ipp://host:631/ipp/print/hp");
        op.AddName("requesting-user-name", "tester");
        op.AddKeyword("document-format", "application/pdf");

        var job = message.AddGroup(IppTag.JobAttributes);
        job.AddInteger("copies", 2);
        job.AddKeyword("sides", "two-sided-long-edge");
        job.AddBoolean("some-flag", true);
        job.AddEnum("orientation-requested", 3);

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));

        Assert.AreEqual(2, parsed.VersionMajor);
        Assert.AreEqual((short)IppOperation.PrintJob, parsed.Code);
        Assert.AreEqual(42, parsed.RequestId);

        var parsedOp = parsed.Group(IppTag.OperationAttributes);
        Assert.IsNotNull(parsedOp);
        Assert.AreEqual("utf-8", parsedOp.Get("attributes-charset")!.First!.AsString());
        Assert.AreEqual("ipp://host:631/ipp/print/hp", parsedOp.Get("printer-uri")!.First!.AsString());

        Assert.AreEqual(2, parsed.FindJobAttribute("copies")!.First!.AsInt());
        Assert.AreEqual("two-sided-long-edge", parsed.FindJobAttribute("sides")!.First!.AsString());
        Assert.AreEqual(true, parsed.FindJobAttribute("some-flag")!.First!.AsBool());
        Assert.AreEqual(3, parsed.FindJobAttribute("orientation-requested")!.First!.AsInt());
    }

    [TestMethod]
    public void Parse_MultiValuedAttribute_KeepsAllValues()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes)
            .AddKeywords("requested-attributes", ["copies", "sides", "media"]);

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));

        var attribute = parsed.FindRequestAttribute("requested-attributes");
        Assert.IsNotNull(attribute);
        Assert.AreEqual(3, attribute.Values.Count);
        CollectionAssert.AreEqual(
            new[] { "copies", "sides", "media" },
            attribute.Values.Select(v => v.AsString()).ToArray());
    }

    [TestMethod]
    public void RoundTrip_NestedCollection_SurvivesEndCollectionFraming()
    {
        // Regression coverage for the Android/Mopria bug: a nested media-col -> media-size
        // collection in the operation attributes must not desynchronize the parser.
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 7 };
        var op = message.AddGroup(IppTag.OperationAttributes);

        var mediaSize = new IppCollection();
        mediaSize.AddInteger("x-dimension", 21000);
        mediaSize.AddInteger("y-dimension", 29700);
        mediaSize.AddKeyword("media-size-name", "iso_a4_210x297mm");

        var mediaCol = new IppCollection();
        mediaCol.AddCollection("media-size", mediaSize);

        var details = new IppCollection();
        details.AddText("document-source-os-name", "Android");
        op.AddCollection("document-format-details", details);
        op.AddCollection("media-col", mediaCol);
        op.AddName("requesting-user-name", "phone");

        var parsed = IppMessageReader.Parse(IppMessageWriter.Write(message));

        var parsedDetails = parsed.FindRequestAttribute("document-format-details")!.First!.AsCollection();
        Assert.IsNotNull(parsedDetails);
        Assert.AreEqual("Android", parsedDetails.Get("document-source-os-name")!.First!.AsString());

        var parsedCol = parsed.FindRequestAttribute("media-col")!.First!.AsCollection();
        Assert.IsNotNull(parsedCol);

        var parsedSize = parsedCol.Get("media-size")!.First!.AsCollection();
        Assert.IsNotNull(parsedSize);
        Assert.AreEqual(21000, parsedSize.Get("x-dimension")!.First!.AsInt());
        Assert.AreEqual(29700, parsedSize.Get("y-dimension")!.First!.AsInt());
        Assert.AreEqual("iso_a4_210x297mm", parsedSize.Get("media-size-name")!.First!.AsString());

        Assert.IsNotNull(parsed.FindRequestAttribute("requesting-user-name"));
    }

    [TestMethod]
    public async Task ReadAsync_StopsAfterEndOfAttributes_LeavingDocumentDataUnread()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 5 };
        message.AddGroup(IppTag.OperationAttributes).AddKeyword("document-format", "application/pdf");

        var document = Encoding.ASCII.GetBytes("%PDF-1.4 fake document");
        var payload = IppMessageWriter.Write(message).Concat(document).ToArray();
        using var stream = new MemoryStream(payload, writable: false);

        var parsed = await IppMessageReader.ReadAsync(stream);

        Assert.AreEqual(5, parsed.RequestId);
        using var rest = new MemoryStream();
        await stream.CopyToAsync(rest);
        CollectionAssert.AreEqual(document, rest.ToArray());
    }

    [TestMethod]
    public void Parse_TruncatedHeader_Throws()
    {
        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse([0x02, 0x00]));
    }

    [TestMethod]
    public void Parse_TruncatedAttributeValue_Throws()
    {
        // Valid 8-byte header, then an attribute header claiming 10 bytes of value that are missing.
        byte[] truncated =
        [
            0x02, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01,
            0x01, // operation-attributes group
            0x41, 0x00, 0x01, (byte)'x', 0x00, 0x0A
        ];

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(truncated));
    }

    [TestMethod]
    public void Parse_AttributeBeforeAnyGroup_Throws()
    {
        byte[] noGroup =
        [
            0x02, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01,
            0x41, 0x00, 0x01, (byte)'x', 0x00, 0x01, (byte)'y'
        ];

        Assert.ThrowsExactly<IppParseException>(() => IppMessageReader.Parse(noGroup));
    }

    [TestMethod]
    public void Parse_ExceedsMaxAttributeBytes_Throws()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes).AddName("blob", new string('a', 512));

        var bytes = IppMessageWriter.Write(message);

        Assert.ThrowsExactly<IppParseException>(() =>
            IppMessageReader.ReadAsync(new MemoryStream(bytes, writable: false), maxBytes: 64).GetAwaiter().GetResult());
    }

    [TestMethod]
    public void Write_ValueLargerThanUInt16_Throws()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.AddGroup(IppTag.OperationAttributes).AddName("blob", new string('a', 70000));

        Assert.ThrowsExactly<InvalidOperationException>(() => IppMessageWriter.Write(message));
    }
}
