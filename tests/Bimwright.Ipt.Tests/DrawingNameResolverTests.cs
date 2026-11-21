using System;
using Bimwright.Ipt.Shared.Contracts;
using Xunit;

namespace Bimwright.Ipt.Tests;

public class DrawingNameResolverTests
{
    private static readonly (string Name, string? Code)[] Sheets = { ("S1 SHELL GA:1", "S1"), ("S2 HEADS:2", null), ("S4 OVERFLOW BOX:4", null) };

    [Theory]
    [InlineData("S1 SHELL GA:1", 0)] // exact name
    [InlineData("S1", 0)]            // code
    [InlineData("2", 1)]             // 1-based index (agents send "1" for the first sheet)
    [InlineData("S4 OVERFLOW BOX", 2)] // name without Inventor's ":n" suffix
    public void Sheet_resolves_name_code_index_and_bare_name(string request, int expected) =>
        Assert.Equal(expected, DrawingNameResolver.Sheet(Sheets, request));

    [Fact]
    public void Sheet_index_resolves_default_sheet_name() =>
        Assert.Equal(0, DrawingNameResolver.Sheet(new (string, string?)[] { ("Sheet:1", null) }, "1"));

    [Fact]
    public void Sheet_refusal_lists_the_sheets()
    {
        var ex = Assert.Throws<ArgumentException>(() => DrawingNameResolver.Sheet(Sheets, "9"));
        Assert.StartsWith("Sheet must resolve uniquely: 9", ex.Message);
        Assert.Contains("1=S1 SHELL GA:1 (code S1)", ex.Message);
        Assert.Contains("3=S4 OVERFLOW BOX:4", ex.Message);
    }

    [Fact]
    public void Sheet_exact_match_wins_over_index()
    {
        var sheets = new (string, string?)[] { ("A:1", "2"), ("B:2", null) };
        Assert.Equal(0, DrawingNameResolver.Sheet(sheets, "2"));
    }

    private static Func<int, string?> Titles(params string?[] titles) => i => titles[i];

    [Fact]
    public void Table_resolves_by_title_when_unmanaged()
    {
        // Tables made outside the typed tools carry no managed name.
        var names = new string?[] { null, "BOM_1" }; var titles = Titles("NOZZLES & OPENINGS", "BILL OF MATERIALS");
        Assert.Equal(0, DrawingNameResolver.Item(names, "table", " Nozzles & Openings ", titles));
        Assert.Equal(1, DrawingNameResolver.Item(names, "table", "BOM_1", titles));
        Assert.Equal(1, DrawingNameResolver.Item(names, "table", "BILL OF MATERIALS", titles));
    }

    [Fact]
    public void Without_title_matching_only_the_exact_managed_name_resolves()
    {
        // delete_drawing_items / edit_drawing_annotation: no title fallback, no case folding.
        var names = new string?[] { null, "BOM_1" };
        Assert.Equal(1, DrawingNameResolver.Item(names, "table", "BOM_1"));
        Assert.Throws<ArgumentException>(() => DrawingNameResolver.Item(names, "table", "bom_1"));
        Assert.Throws<ArgumentException>(() => DrawingNameResolver.Item(names, "table", "NOZZLES & OPENINGS"));
    }

    [Fact]
    public void Titles_are_read_only_when_no_managed_name_matches()
    {
        var reads = 0;
        Assert.Equal(1, DrawingNameResolver.Item(new string?[] { null, "BOM_1" }, "table", "BOM_1", i => { reads++; return "T"; }));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void A_throwing_title_getter_counts_as_no_title()
    {
        var names = new string?[] { null, null };
        Func<int, string?> titles = i => i == 0 ? throw new InvalidOperationException("COM getter failed") : "DESIGN DATA";
        Assert.Equal(1, DrawingNameResolver.Item(names, "table", "design data", titles));
        var ex = Assert.Throws<ArgumentException>(() => DrawingNameResolver.Item(names, "table", "X", titles));
        Assert.Contains("Candidates: (unnamed); title=DESIGN DATA.", ex.Message);
    }

    [Fact]
    public void Edit_table_refusal_lists_names_and_titles_and_points_to_its_locator_parameter()
    {
        var names = new string?[] { null, null, "T2", null };
        var ex = Assert.Throws<ArgumentException>(() => DrawingNameResolver.Item(names, "table", "DESIGN DATA", Titles("DESIGN DATA", "DESIGN DATA", null, null)));
        Assert.Equal("Drawing target must resolve uniquely on the selected sheet: 2 table match 'DESIGN DATA'. " +
            "Candidates: title=DESIGN DATA; title=DESIGN DATA; name=T2; (unnamed). " +
            "For an untitled or same-titled table, pass locator (from inventor_get_drawing_info(include=items)) instead of name.", ex.Message);
    }

    [Fact]
    public void Delete_or_annotation_refusal_lists_names_only_and_points_to_the_item_locator()
    {
        var names = new string?[] { null, "T2" };
        var ex = Assert.Throws<ArgumentException>(() => DrawingNameResolver.Item(names, "table", "DESIGN DATA"));
        Assert.Equal("Drawing target must resolve uniquely on the selected sheet: 0 table match 'DESIGN DATA'. " +
            "Candidates: (unnamed); name=T2. Pass the exact managed name, or the item's locator from inventor_get_drawing_info(include=items).", ex.Message);
        Assert.DoesNotContain("title", ex.Message);
    }

    [Fact]
    public void Item_refusal_on_empty_sheet_says_so()
    {
        var ex = Assert.Throws<ArgumentException>(() => DrawingNameResolver.Item(Array.Empty<string?>(), "table", "X", Titles()));
        Assert.Contains("no table exists", ex.Message);
    }
}
