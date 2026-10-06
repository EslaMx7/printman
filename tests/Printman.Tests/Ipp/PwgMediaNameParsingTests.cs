using Printman.Services.Ipp;

namespace Printman.Tests.Ipp;

/// <summary>Parsing CUPS media-supported keywords (PWG 5101.1 self-describing names) into paper sizes.</summary>
[TestClass]
public sealed class PwgMediaNameParsingTests
{
    [TestMethod]
    [DataRow("iso_a4_210x297mm", "A4", 827, 1169)]
    [DataRow("na_letter_8.5x11in", "Letter", 850, 1100)]
    [DataRow("na_ledger_11x17in", "Tabloid", 1100, 1700)]
    [DataRow("iso_ra4_215x305mm", "RA4", 846, 1201)]
    [DataRow("jis_b0_1030x1456mm", "JIS B0", 4055, 5732)]
    [DataRow("na_super-b_13x19in", "Super B", 1300, 1900)]
    public void FromPwgName_StandardNames_UseFriendlyNameAndSize(string keyword, string name, int width, int height)
    {
        var paper = PwgMediaMapper.FromPwgName(keyword)!;

        Assert.AreEqual(name, paper.Name);
        Assert.AreEqual(width, paper.WidthHundredthsInch);
        Assert.AreEqual(height, paper.HeightHundredthsInch);
        Assert.AreEqual(keyword, paper.Keyword);
        Assert.AreEqual(0, paper.RawKind);
    }

    [TestMethod]
    public void FromPwgName_CustomSizeCloseToStandard_GetsStandardName()
    {
        // PPDs often define A5 slightly off-size; CUPS then reports a custom_ keyword
        var paper = PwgMediaMapper.FromPwgName("custom_148.52x209.9mm_148.52x209.9mm")!;

        Assert.AreEqual("A5", paper.Name);
        Assert.AreEqual("custom_148.52x209.9mm_148.52x209.9mm", paper.Keyword);
    }

    [TestMethod]
    public void FromPwgName_CustomSizeWithoutStandardMatch_IsLabelledCustom()
    {
        Assert.AreEqual("Custom 16x24 in", PwgMediaMapper.FromPwgName("custom_16x24in_16x24in")!.Name);
    }

    [TestMethod]
    [DataRow("custom_min_0.5x0.5in")]
    [DataRow("custom_max_35277.78x35277.78mm")]
    [DataRow("A4")]
    [DataRow("iso_a4")]
    [DataRow("iso_a4_210x297cm")]
    public void FromPwgName_RangesAndUnknownFormats_ReturnNull(string keyword)
    {
        Assert.IsNull(PwgMediaMapper.FromPwgName(keyword));
    }
}
