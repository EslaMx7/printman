using System.Net;
using Printman.Core.Models;
using Printman.Services.Cups;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Cups;

[TestClass]
public sealed class CupsClientTests
{
    [TestMethod]
    public void ResolveEndpoint_CupsServerSocketPath_UsesSocket()
    {
        var ep = CupsClient.ResolveEndpoint("/tmp/cups.sock", _ => false);

        Assert.AreEqual("/tmp/cups.sock", ep.SocketPath);
        Assert.IsTrue(ep.ViaSocket);
        Assert.AreEqual("/tmp/cups.sock", ep.ToString());
    }

    [TestMethod]
    [DataRow("printhost", "printhost", 631)]
    [DataRow(" printhost:8631 ", "printhost", 8631)]
    [DataRow("[::1]", "[::1]", 631)]
    [DataRow("host:notaport", "host:notaport", 631)]
    public void ResolveEndpoint_CupsServerHost_ParsesHostAndPort(string value, string host, int port)
    {
        var ep = CupsClient.ResolveEndpoint(value, _ => true);

        Assert.IsNull(ep.SocketPath);
        Assert.AreEqual(host, ep.Host);
        Assert.AreEqual(port, ep.Port);
        Assert.AreEqual($"{host}:{port}", ep.ToString());
    }

    [TestMethod]
    public void ResolveEndpoint_NoVariable_PrefersExistingSocketThenTcp()
    {
        Assert.AreEqual("/var/run/cups/cups.sock", CupsClient.ResolveEndpoint(null, p => p == "/var/run/cups/cups.sock").SocketPath);
        Assert.AreEqual("/private/var/run/cupsd", CupsClient.ResolveEndpoint("", p => p == "/private/var/run/cupsd").SocketPath);

        var tcp = CupsClient.ResolveEndpoint("  ", _ => false);
        Assert.IsNull(tcp.SocketPath);
        Assert.AreEqual("localhost:631", tcp.ToString());
    }

    [TestMethod]
    public void StaticUris_AndSuccessPredicate()
    {
        Assert.AreEqual("ipp://localhost/printers/My%20Printer", CupsClient.PrinterUri("My Printer"));
        Assert.AreEqual("ipp://localhost/jobs/42", CupsClient.JobUri(42));
        Assert.IsTrue(CupsClient.IsSuccess(IppStatus.OkIgnoredOrSubstituted));
        Assert.IsFalse(CupsClient.IsSuccess(IppStatus.NotFound));
    }

    [TestMethod]
    public async Task SendAsync_AddsStandardOperationAttributes_AndDocumentBody()
    {
        using var temp = new TempDirectory();
        var doc = temp.Combine("doc.pdf");
        File.WriteAllBytes(doc, "%PDF-1.7 hello"u8.ToArray());
        var server = new FakeCupsServer();
        using var client = server.CreateClient();

        var reply = await client.SendAsync(IppOperation.PrintJob, "/printers/PDF",
            op => op.AddUri("printer-uri", CupsClient.PrinterUri("PDF")),
            job => job.AddInteger("copies", 2),
            doc);

        Assert.AreEqual(IppStatus.Ok, reply.Code);
        var sent = server.Requests.Single();
        Assert.AreEqual("/printers/PDF", sent.Resource);
        Assert.AreEqual(IppOperation.PrintJob, sent.Message.Code);
        var op = sent.Message.Group(IppTag.OperationAttributes)!;
        Assert.AreEqual("utf-8", op.Get("attributes-charset")!.First!.AsString());
        Assert.AreEqual("en", op.Get("attributes-natural-language")!.First!.AsString());
        Assert.AreEqual(Environment.UserName, op.Get("requesting-user-name")!.First!.AsString());
        Assert.AreEqual(2, sent.Message.Group(IppTag.JobAttributes)!.Get("copies")!.First!.AsInt());
        CollectionAssert.AreEqual("%PDF-1.7 hello"u8.ToArray(), sent.Document);
        Assert.IsNull(sent.Authorization);
    }

    [TestMethod]
    public async Task SendAsync_KeepsExplicitRequestingUserName_AndIncrementsRequestId()
    {
        var server = new FakeCupsServer();
        using var client = server.CreateClient();

        await client.SendAsync(IppOperation.GetJobs, "/", op => op.AddName("requesting-user-name", "alice"));
        await client.SendAsync(IppOperation.GetJobs, "/", _ => { });

        Assert.AreEqual("alice", server.Requests[0].Message.Group(IppTag.OperationAttributes)!.Get("requesting-user-name")!.First!.AsString());
        Assert.AreEqual(1, server.Requests[0].Message.RequestId);
        Assert.AreEqual(2, server.Requests[1].Message.RequestId);
        Assert.AreEqual(1, server.Requests[0].Message.Groups.Count, "no job group when none requested");
    }

    [TestMethod]
    public async Task SendAsync_Unauthorized_OverSocket_RetriesWithPeerCred()
    {
        var server = new FakeCupsServer { UnauthorizedFirst = 1 };
        using var client = server.CreateClient(viaSocket: true);

        await client.SendAsync(IppOperation.CancelJob, "/jobs/", _ => { });

        Assert.AreEqual(2, server.Requests.Count);
        Assert.IsNull(server.Requests[0].Authorization);
        Assert.AreEqual($"PeerCred {Environment.UserName}", server.Requests[1].Authorization);
    }

    [TestMethod]
    public async Task SendAsync_Unauthorized_OverTcp_ThrowsForbidden()
    {
        var server = new FakeCupsServer { UnauthorizedFirst = 5 };
        using var client = server.CreateClient(viaSocket: false);

        var ex = await Assert.ThrowsExactlyAsync<CupsException>(() => client.SendAsync(IppOperation.CancelJob, "/jobs/", _ => { }));

        Assert.AreEqual(IppStatus.Forbidden, ex.IppStatus);
        Assert.AreEqual(1, server.Requests.Count);
        Assert.IsFalse(ex.IsUnreachable);
    }

    [TestMethod]
    public async Task SendAsync_StillUnauthorizedAfterPeerCred_ThrowsForbidden()
    {
        var server = new FakeCupsServer { UnauthorizedFirst = 2 };
        using var client = server.CreateClient(viaSocket: true);

        var ex = await Assert.ThrowsExactlyAsync<CupsException>(() => client.SendAsync(IppOperation.CancelJob, "/jobs/", _ => { }));

        Assert.AreEqual(IppStatus.Forbidden, ex.IppStatus);
        StringAssert.Contains(ex.Message, "lpadmin");
    }

    [TestMethod]
    public async Task SendAsync_HttpError_ThrowsWithStatus()
    {
        var server = new FakeCupsServer { Status = HttpStatusCode.InternalServerError };
        using var client = server.CreateClient();

        var ex = await Assert.ThrowsExactlyAsync<CupsException>(() => client.SendAsync(IppOperation.GetJobs, "/", _ => { }));

        StringAssert.Contains(ex.Message, "HTTP 500");
        Assert.IsNull(ex.IppStatus);
    }

    [TestMethod]
    public async Task SendAsync_ConnectionRefused_ThrowsUnreachable()
    {
        var server = new FakeCupsServer { Fail = () => new HttpRequestException("Connection refused") };
        using var client = server.CreateClient(viaSocket: false);

        var ex = await Assert.ThrowsExactlyAsync<CupsException>(() => client.SendAsync(IppOperation.GetJobs, "/", _ => { }));

        Assert.IsTrue(ex.IsUnreachable);
        StringAssert.Contains(ex.Message, "localhost:631");
        StringAssert.Contains(ex.Message, "Connection refused");
    }

    [TestMethod]
    public async Task SendAsync_ConnectTimeout_ThrowsUnreachable_NotCancellation()
    {
        // SocketsHttpHandler reports its connect timeout as a TaskCanceledException nobody asked for
        var server = new FakeCupsServer { Fail = () => new TaskCanceledException("The operation was canceled.") };
        using var client = server.CreateClient();

        var ex = await Assert.ThrowsExactlyAsync<CupsException>(() => client.SendAsync(IppOperation.GetJobs, "/", _ => { }));

        Assert.IsTrue(ex.IsUnreachable);
        StringAssert.Contains(ex.Message, "no answer within 5 seconds");
    }

    [TestMethod]
    public async Task SendAsync_CallerCancels_PropagatesCancellation()
    {
        var server = new FakeCupsServer();
        using var client = server.CreateClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => client.SendAsync(IppOperation.GetJobs, "/", _ => { }, ct: cts.Token));
    }

    [TestMethod]
    public async Task SendAsync_MalformedReply_ThrowsInvalidResponse()
    {
        var server = new FakeCupsServer { RawBody = [2, 0, 0, 0] };
        using var client = server.CreateClient();

        var ex = await Assert.ThrowsExactlyAsync<CupsException>(() => client.SendAsync(IppOperation.GetJobs, "/", _ => { }));

        StringAssert.Contains(ex.Message, "Invalid IPP response");
    }

    [TestMethod]
    public async Task SendAsync_IppErrorStatus_UsesStatusMessage_OrCode()
    {
        var server = new FakeCupsServer { Respond = _ => FakeCupsServer.Error(IppStatus.NotFound, "The printer does not exist.") };
        using var client = server.CreateClient();

        var withMessage = await Assert.ThrowsExactlyAsync<CupsException>(() => client.SendAsync(IppOperation.GetJobs, "/", _ => { }));
        Assert.AreEqual("The printer does not exist.", withMessage.Message);
        Assert.AreEqual(IppStatus.NotFound, withMessage.IppStatus);

        server.Respond = _ => FakeCupsServer.Error(IppStatus.Busy);
        var withoutMessage = await Assert.ThrowsExactlyAsync<CupsException>(() => client.SendAsync(IppOperation.GetJobs, "/", _ => { }));
        Assert.AreEqual("CUPS returned IPP status 0x0507.", withoutMessage.Message);
    }

    [TestMethod]
    public void Send_SynchronousWrapper_ReturnsReply()
    {
        var server = new FakeCupsServer();
        using var client = server.CreateClient();

        var reply = client.Send(CupsClient.CupsGetDefault, "/", _ => { });

        Assert.AreEqual(IppStatus.Ok, reply.Code);
        Assert.AreEqual(CupsClient.CupsGetDefault, server.Requests.Single().Message.Code);
    }

    [TestMethod]
    public async Task ProbeAsync_Ready_NoQueues_AndUnreachable()
    {
        var server = new FakeCupsServer();
        using var client = server.CreateClient();
        Assert.IsNull(await client.ProbeAsync());
        Assert.AreEqual("printer-name", server.Requests.Single().Message.Group(IppTag.OperationAttributes)!.Get("requested-attributes")!.First!.AsString());

        server.Respond = _ => FakeCupsServer.Error(IppStatus.NotFound, "No destinations added.");
        Assert.IsNull(await client.ProbeAsync(), "cupsd without queues is still ready");

        server.Fail = () => new HttpRequestException("Connection refused");
        StringAssert.Contains(await client.ProbeAsync(), "Cannot reach the CUPS print service");
    }

    [TestMethod]
    public async Task DownloadPpdAsync_SavesFile_OrReportsMissing()
    {
        using var temp = new TempDirectory();
        var server = new FakeCupsServer { Ppd = "*PPD-Adobe: \"4.3\"\n*ModelName: \"Test\"\n" };
        using var client = server.CreateClient();
        var target = temp.Combine("q.ppd");

        Assert.IsTrue(await client.DownloadPpdAsync("My Queue", target));
        Assert.AreEqual(server.Ppd, File.ReadAllText(target));

        server.Ppd = null;
        Assert.IsFalse(await client.DownloadPpdAsync("My Queue", temp.Combine("none.ppd")));

        server.Fail = () => new HttpRequestException("down");
        Assert.IsFalse(await client.DownloadPpdAsync("My Queue", temp.Combine("down.ppd")));

        server.Fail = () => new TaskCanceledException("timeout");
        Assert.IsFalse(await client.DownloadPpdAsync("My Queue", temp.Combine("slow.ppd")));
    }

    [TestMethod]
    public async Task DownloadPpdAsync_CallerCancels_Propagates()
    {
        var server = new FakeCupsServer();
        using var client = server.CreateClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => client.DownloadPpdAsync("q", "x.ppd", cts.Token));
    }

    [TestMethod]
    public void CupsException_IsUnreachable_OnlyForConnectionFailures()
    {
        Assert.IsTrue(new CupsException("Cannot reach the CUPS print service at x (y).").IsUnreachable);
        Assert.IsFalse(new CupsException("Cannot reach the CUPS print service", IppStatus.NotFound).IsUnreachable);
        Assert.IsFalse(new CupsException("Other").IsUnreachable);
    }
}
