using Printman.Core.Models;

namespace Printman.Tests.Models;

[TestClass]
public sealed class ServerRuntimeInfoTests
{
    [TestMethod]
    public void ServerRuntimeInfo_Defaults()
    {
        var info = new ServerRuntimeInfo();

        Assert.IsFalse(info.WebUiEnabled);
        Assert.AreEqual(0, info.WebPort);
        Assert.AreEqual("0.0.0.0", info.BindAddress);
        Assert.IsFalse(info.RequireAuth);
        Assert.IsNull(info.Pin);
        Assert.IsNull(info.LocalUrl);
        Assert.AreEqual(0, info.LanUrls.Count);
        Assert.AreEqual(0, info.LanIps.Count);
        Assert.IsFalse(info.SharingEnabled);
        Assert.AreEqual(0, info.IppPort);
        Assert.IsFalse(info.MdnsActive);
        Assert.IsNull(info.MdnsHostName);
        Assert.AreEqual(0, info.SharedPrinters.Count);
        Assert.AreEqual(0, info.Warnings.Count);
    }

    [TestMethod]
    public void ServerRuntimeInfo_InitProperties_AreRetained()
    {
        var endpoint = new SharedPrinterEndpoint
        {
            SystemName = "CLX-3185",
            DisplayName = "Printman - CLX-3185",
            ResourcePath = "ipp/print/clx-3185",
            IppUrls = ["ipp://host.local:8631/ipp/print/clx-3185"]
        };

        var info = new ServerRuntimeInfo
        {
            WebUiEnabled = true,
            WebPort = 5000,
            BindAddress = "127.0.0.1",
            RequireAuth = true,
            Pin = "123456",
            LocalUrl = "http://localhost:5000/?pin=123456",
            LanUrls = ["http://192.168.1.10:5000/?pin=123456"],
            LanIps = ["192.168.1.10"],
            SharingEnabled = true,
            IppPort = 8631,
            MdnsActive = true,
            MdnsHostName = "host.local",
            SharedPrinters = [endpoint],
            Warnings = ["warn"]
        };

        Assert.IsTrue(info.WebUiEnabled);
        Assert.AreEqual(5000, info.WebPort);
        Assert.AreEqual("127.0.0.1", info.BindAddress);
        Assert.IsTrue(info.RequireAuth);
        Assert.AreEqual("123456", info.Pin);
        Assert.AreEqual("http://localhost:5000/?pin=123456", info.LocalUrl);
        Assert.AreEqual(1, info.LanUrls.Count);
        Assert.AreEqual(1, info.LanIps.Count);
        Assert.IsTrue(info.SharingEnabled);
        Assert.AreEqual(8631, info.IppPort);
        Assert.IsTrue(info.MdnsActive);
        Assert.AreEqual("host.local", info.MdnsHostName);
        Assert.AreEqual("CLX-3185", info.SharedPrinters[0].SystemName);
        Assert.AreEqual("Printman - CLX-3185", info.SharedPrinters[0].DisplayName);
        Assert.AreEqual("ipp/print/clx-3185", info.SharedPrinters[0].ResourcePath);
        Assert.AreEqual(1, info.SharedPrinters[0].IppUrls.Count);
        Assert.AreEqual(1, info.Warnings.Count);
    }
}
