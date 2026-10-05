using Printman.Core.Models;

namespace Printman.Tests.Models;

[TestClass]
public sealed class PageRangeTests
{
    [TestMethod]
    public void Parse_Null_IsAllPages()
    {
        var range = PageRange.Parse(null);

        Assert.IsTrue(range.IsAllPages);
        Assert.AreEqual("All Pages", range.ToString());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("all")]
    [DataRow("ALL")]
    public void Parse_BlankOrAll_IsAllPages(string expression) =>
        Assert.IsTrue(PageRange.Parse(expression).IsAllPages);

    [TestMethod]
    [DataRow("1:3", "1,2,3")]
    [DataRow("1-3", "1,2,3")]
    [DataRow("3-1", "1,2,3")]
    [DataRow("1,3,5", "1,3,5")]
    [DataRow("2-", "2,3,4,5,6,7,8,9,10")]
    [DataRow("-4", "1,2,3,4")]
    [DataRow("5", "5")]
    public void ResolvePages_ParsesExpression(string expression, string expected)
    {
        var pages = PageRange.Parse(expression).ResolvePages(10);

        Assert.AreEqual(expected, string.Join(',', pages));
    }

    [TestMethod]
    public void ResolvePages_ClampsToDocumentLength()
    {
        var pages = PageRange.Parse("5-20").ResolvePages(6);

        Assert.AreEqual("5,6", string.Join(',', pages));
    }

    [TestMethod]
    public void ResolvePages_NonPositiveTotal_ReturnsEmpty() =>
        CollectionAssert.AreEqual(Array.Empty<int>(), PageRange.Parse("1-3").ResolvePages(0));

    [TestMethod]
    [DataRow("abc")]
    [DataRow("1..3")]
    [DataRow("0")]
    [DataRow("-0")]
    [DataRow("1:60000")]
    [DataRow("1-20000")]
    public void Parse_InvalidExpression_Throws(string expression) =>
        Assert.ThrowsExactly<FormatException>(() => PageRange.Parse(expression));

    [TestMethod]
    public void ResolvePages_OverlappingSelectors_AreDeduplicated()
    {
        var pages = PageRange.Parse("1-3,2-4").ResolvePages(10);

        Assert.AreEqual("1,2,3,4", string.Join(',', pages));
    }
}
