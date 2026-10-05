using System.Globalization;
using Printman.Core.Models;
using Printman.Services.Ipp;
using Printman.Tests.Support;

namespace Printman.Tests.Ipp;

/// <summary>Region-default selection and fallback branches for PWG media mapping.</summary>
[TestClass]
public sealed class PwgMediaMapperBranchTests
{
    [TestMethod]
    public void PickDefault_PreferredSizePresent_SelectsRegionDefault()
    {
        // RegionInfo.CurrentRegion follows the OS region (metric or not), so assert against
        // whichever default this machine uses. The opposite branch is not reachable in-process.
        var mapped = PwgMediaMapper.Map([TestData.A4, TestData.Letter]);
        var expected = RegionInfo.CurrentRegion.IsMetric ? "iso_a4_210x297mm" : "na_letter_8.5x11in";

        Assert.AreEqual(expected, PwgMediaMapper.PickDefault(mapped).Name);
    }

    [TestMethod]
    public void PickDefault_PreferredSizeAbsent_FallsBackToFirstMapped()
    {
        var a5 = new PaperSizeOption("A5", 11, 583, 827);
        var mapped = PwgMediaMapper.Map([a5]);

        Assert.AreEqual(mapped[0].Media.Name, PwgMediaMapper.PickDefault(mapped).Name);
    }
}
