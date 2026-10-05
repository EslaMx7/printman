using Printman.Core.Models;
using Printman.Services.Ipp;

namespace Printman.Tests.Ipp;

[TestClass]
public sealed class PwgMediaMapperTests
{
    private static readonly PaperSizeOption A4 = new("A4", 9, 827, 1169);
    private static readonly PaperSizeOption Letter = new("Letter", 1, 850, 1100);

    [TestMethod]
    [DataRow("iso_a4_210x297mm")]
    [DataRow("ISO_A4_210X297MM")]
    public void FindByName_IsCaseInsensitive(string name) =>
        Assert.AreEqual("iso_a4_210x297mm", PwgMediaMapper.FindByName(name)!.Name);

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not-a-media")]
    public void FindByName_Unknown_ReturnsNull(string? name) =>
        Assert.IsNull(PwgMediaMapper.FindByName(name));

    [TestMethod]
    public void FindBySize_MatchesStandardSizesInEitherOrientation()
    {
        Assert.AreEqual("iso_a4_210x297mm", PwgMediaMapper.FindBySize(21000, 29700)!.Name);
        Assert.AreEqual("iso_a4_210x297mm", PwgMediaMapper.FindBySize(29700, 21000)!.Name);
        Assert.AreEqual("na_letter_8.5x11in", PwgMediaMapper.FindBySize(21590, 27940)!.Name);
    }

    [TestMethod]
    public void FindBySize_OutsideTolerance_ReturnsNull() =>
        Assert.IsNull(PwgMediaMapper.FindBySize(1000, 1000));

    [TestMethod]
    public void Map_EmptyInput_ReturnsA4AndLetterFallbacks()
    {
        var mapped = PwgMediaMapper.Map([]);

        Assert.AreEqual(2, mapped.Count);
        Assert.AreEqual("iso_a4_210x297mm", mapped[0].Media.Name);
        Assert.AreEqual("na_letter_8.5x11in", mapped[1].Media.Name);
        Assert.IsNull(mapped[0].Paper);
        Assert.IsNull(mapped[1].Paper);
    }

    [TestMethod]
    public void Map_PrinterSizes_ResolveToPwgNamesAndKeepPrinterOrder()
    {
        var mapped = PwgMediaMapper.Map([Letter, A4]);

        Assert.AreEqual(2, mapped.Count);
        Assert.AreEqual("na_letter_8.5x11in", mapped[0].Media.Name);
        Assert.AreEqual(Letter, mapped[0].Paper);
        Assert.AreEqual("iso_a4_210x297mm", mapped[1].Media.Name);
        Assert.AreEqual(A4, mapped[1].Paper);
    }

    [TestMethod]
    public void Map_DuplicateSizes_AreDeduplicated()
    {
        var mapped = PwgMediaMapper.Map([A4, A4, new PaperSizeOption("A4 copy", 9, 827, 1169)]);

        Assert.AreEqual(1, mapped.Count);
        Assert.AreEqual("iso_a4_210x297mm", mapped[0].Media.Name);
    }

    [TestMethod]
    public void PickDefault_ReturnsOneOfTheMappedSizes()
    {
        var mapped = PwgMediaMapper.Map([A4, Letter]);

        var chosen = PwgMediaMapper.PickDefault(mapped);

        Assert.IsTrue(mapped.Any(m => m.Media.Name == chosen.Name), $"'{chosen.Name}' was not mapped.");
    }
}
