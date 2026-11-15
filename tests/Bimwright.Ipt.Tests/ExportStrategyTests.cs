using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

public class ExportStrategyTests
{
    private static readonly string[] Doc = { "s1", "s2", "s3", "s4", "s5" };

    [Fact]
    public void AllSheetsInOrderIsWholeDocument() =>
        Assert.True(ExportStrategy.IsWholeDocument(Doc, Doc, anyExcludedFromPrinting: false));

    [Theory]
    [InlineData("s1,s2")]              // subset
    [InlineData("s2,s1,s3,s4,s5")]     // reordered
    [InlineData("")]
    public void SubsetOrReorderNeedsSheetCopy(string selected) =>
        Assert.False(ExportStrategy.IsWholeDocument(
            selected.Split(',', StringSplitOptions.RemoveEmptyEntries), Doc, anyExcludedFromPrinting: false));

    [Fact]
    public void ExcludedSheetNeedsSheetCopy() =>
        Assert.False(ExportStrategy.IsWholeDocument(Doc, Doc, anyExcludedFromPrinting: true));
}
