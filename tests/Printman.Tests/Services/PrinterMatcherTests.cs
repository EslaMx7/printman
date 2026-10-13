using Printman.Core.Models;
using Printman.Services;
using Printman.Tests.Support;

namespace Printman.Tests.Services;

/// <summary>Fuzzy printer name matching shared by the Windows and CUPS discovery services.</summary>
[TestClass]
public sealed class PrinterMatcherTests
{
    private static readonly IReadOnlyList<PrinterInfo> Printers =
    [
        TestData.Printer("Microsoft Print to PDF"),
        TestData.Printer("HP_LaserJet_Professional_P1102"),
        TestData.Printer("Canon MF240"),
        TestData.Printer("PDF")
    ];

    [TestMethod]
    public void Find_ExactName_IgnoresCaseAndSurroundingWhitespace()
    {
        Assert.AreEqual("PDF", PrinterMatcher.Find(Printers, "  pdf ")!.Name);
    }

    [TestMethod]
    public void Find_Prefix_MatchesStartOfName()
    {
        Assert.AreEqual("Canon MF240", PrinterMatcher.Find(Printers, "canon")!.Name);
    }

    [TestMethod]
    public void Find_Substring_MatchesInsideName()
    {
        Assert.AreEqual("Microsoft Print to PDF", PrinterMatcher.Find(Printers, "print to")!.Name);
    }

    [TestMethod]
    public void Find_Tokens_MatchCupsQueueNamesWithUnderscores()
    {
        Assert.AreEqual("HP_LaserJet_Professional_P1102", PrinterMatcher.Find(Printers, "HP Laser P1102")!.Name);
    }

    [TestMethod]
    public void Find_TokensSplitOnDashAndUnderscore()
    {
        Assert.AreEqual("HP_LaserJet_Professional_P1102", PrinterMatcher.Find(Printers, "laserjet-p1102")!.Name);
    }

    [TestMethod]
    public void Find_NoMatch_ReturnsNull()
    {
        Assert.IsNull(PrinterMatcher.Find(Printers, "Epson Ink"));
    }

    [TestMethod]
    public void Find_OnlySeparators_ReturnsNull()
    {
        Assert.IsNull(PrinterMatcher.Find(Printers, "_ -"));
    }
}
