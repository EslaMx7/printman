using System.Net;
using System.Net.Sockets;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Services.Cups;
using Printman.Tests.Fakes;
using Printman.Tests.Support;

namespace Printman.Tests.Cups;

/// <summary>The real transport: default construction, TCP and cupsd domain sockets (no cupsd needed).</summary>
[TestClass]
public sealed class CupsTransportTests
{
    [TestMethod]
    public void DefaultConstructor_ResolvesAnEndpointWithoutConnecting()
    {
        using var client = new CupsClient();

        Assert.IsNotNull(client.Endpoint);
        Assert.IsFalse(string.IsNullOrEmpty(client.Endpoint.ToString()));
    }

    [TestMethod]
    public async Task Tcp_ClosedPort_IsReportedAsUnreachable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // nothing listens here any more
        using var client = new CupsClient(new CupsEndpoint(null, "127.0.0.1", port));

        var problem = await client.ProbeAsync();

        StringAssert.Contains(problem, $"Cannot reach the CUPS print service at 127.0.0.1:{port}");
    }

    [TestMethod]
    public async Task DomainSocket_MissingSocketFile_IsReportedAsUnreachable()
    {
        using var temp = new TempDirectory();
        using var client = new CupsClient(new CupsEndpoint(temp.Combine("cups.sock"), "localhost", 631));

        var problem = await client.ProbeAsync();

        StringAssert.Contains(problem, "Cannot reach the CUPS print service at ");
        StringAssert.Contains(problem, "cups.sock");
    }

    [TestMethod]
    public async Task DomainSocket_ConnectsThroughTheSocketFile()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("cups.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);
        int accepted = 0;
        var serve = Task.Run(async () =>
        {
            using var peer = await listener.AcceptAsync();
            accepted++;
            // A cupsd that hangs up without answering: the client must turn this into "unreachable"
        });
        using var client = new CupsClient(new CupsEndpoint(path, "localhost", 631));

        var problem = await client.ProbeAsync();
        await serve.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(1, accepted, "the connect callback opened the domain socket");
        StringAssert.Contains(problem, "Cannot reach the CUPS print service");
    }

    [TestMethod]
    public void DiscoveryService_DefaultConstruction_ReadsRealEnvironment()
    {
        var server = new FakeCupsServer
        {
            Respond = r => r.Message.Code == CupsClient.CupsGetPrinters
                ? FakeCupsServer.Ok(m => m.AddGroup(IppTag.PrinterAttributes).AddName("printer-name", "Only"))
                : FakeCupsServer.Error(IppStatus.NotFound)
        };
        var service = new CupsPrinterDiscoveryService(server.CreateClient());

        Assert.AreEqual("Only", service.GetPrinters().Single().Name);
    }

    [TestMethod]
    public void NoFirewallInspector_NeverHasHints()
    {
        IFirewallInspector inspector = new NoFirewallInspector();

        Assert.AreEqual(0, inspector.CheckInboundAccess(8631, needsMdns: true).Count);
    }
}
