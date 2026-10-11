using System.IO.Compression;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Handlers.Parameters;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ExcelParameterSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ipt-excel-" + Guid.NewGuid().ToString("N"));
    public ExcelParameterSourceTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private string Workbook(string cells, string? extraEntry = null, string? shared = null)
    {
        var path = Path.Combine(_directory, Guid.NewGuid() + ".xlsx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content); }
        Add("xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Parameters' r:id='r7'/><sheet name='Other' r:id='r8'/></sheets></workbook>");
        Add("xl/_rels/workbook.xml.rels", "<Relationships><Relationship Id='r7' Target='/xl/worksheets/custom.xml'/><Relationship Id='r8' Target='worksheets/unused.xml'/></Relationships>");
        Add("xl/worksheets/custom.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData>" + cells + "</sheetData></worksheet>");
        if (shared != null) Add("xl/sharedStrings.xml", "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'>" + shared + "</sst>");
        if (extraEntry != null) Add(extraEntry, "ignored");
        return path;
    }
    private static string Text(string address, string value) => $"<c r='{address}' t='inlineStr'><is><t>{value}</t></is></c>";
    private static string Row(int row, string name = "Diameter", string value = "<v>210</v>", string units = "mm")
        => Text("C" + row, name) + $"<c r='D{row}'>{value}</c>" + Text("E" + row, units);

    [Fact]
    public void First_sheet_relationship_offset_shared_rich_strings_and_formula_cache_are_read()
    {
        var path = Workbook("<c r='C6' t='s'><v>0</v></c><c r='D6'><f>1+2</f><v>3</v></c>" + Text("E6", "mm") + Row(7, "Count", "<v>4</v>", "ul"),
            shared: "<si><r><t>Plate</t></r><r><t>Thickness</t></r></si>");
        var source = ExcelParameterSource.Read(path, "$c$6");
        Assert.Equal("C6", source.StartCell); Assert.Equal("Parameters", source.Sheet);
        Assert.Equal(new[] { "PlateThickness", "Count" }, source.Rows.Select(r => r.Name));
        Assert.Equal(new[] { 3.0, 4.0 }, source.Rows.Select(r => r.Value));
        Assert.Equal(source.TableId, ExcelParameterSource.Identity(path.ToUpperInvariant(), "C6"));
        source.VerifyUnchanged();
        File.AppendAllText(path, "changed");
        Assert.Throws<InvalidOperationException>(() => source.VerifyUnchanged());
    }
    [Theory]
    [InlineData("<f>1+2</f>")]
    [InlineData("<v>NaN</v>")]
    [InlineData("<v>Infinity</v>")]
    [InlineData("<v>1e999</v>")]
    [InlineData("<v>210 mm</v>")]
    public void Missing_or_invalid_saved_numeric_values_are_rejected(string value)
        => Assert.Throws<ArgumentException>(() => ExcelParameterSource.Read(Workbook(Row(6, value: value)), "C6"));

    [Theory]
    [InlineData("Diameter", "", false)]
    [InlineData("1Diameter", "mm", false)]
    [InlineData("Bad Name", "mm", false)]
    [InlineData("diameter", "mm", true)]
    public void Invalid_names_units_and_case_insensitive_duplicates_are_rejected(string name, string unit, bool duplicate)
        => Assert.Throws<ArgumentException>(() => ExcelParameterSource.Read(Workbook((duplicate ? Row(6) : "") + Row(duplicate ? 7 : 6, name, units: unit)), "C6"));

    [Theory]
    [InlineData("xl/vbaProject.bin")]
    [InlineData("xl/externalLinks/externalLink1.xml")]
    public void Active_or_external_workbook_content_is_rejected(string entry)
        => Assert.Throws<ArgumentException>(() => ExcelParameterSource.Read(Workbook(Row(6), entry), "C6"));

    [Theory]
    [InlineData("relative.xlsx", "A1")]
    [InlineData(@"\\server\share\book.xlsx", "A1")]
    [InlineData(@"C:\book.xlsm", "A1")]
    [InlineData(@"C:\book.xlsx", "Sheet2!A1")]
    [InlineData(@"C:\book.xlsx", "A0")]
    [InlineData(@"C:\book.xlsx", "XFD1")]
    [InlineData(@"C:\book.xlsx", "A1048577")]
    public void Unsupported_paths_and_cells_fail_before_file_access(string path, string cell)
        => Assert.Throws<ArgumentException>(() => ExcelParameterSource.Read(path, cell));

    [Fact]
    public void Blank_block_oversized_block_and_locked_workbook_fail()
    {
        Assert.Throws<ArgumentException>(() => ExcelParameterSource.Read(Workbook(""), "C6"));
        Assert.Throws<ArgumentException>(() => ExcelParameterSource.Read(Workbook(string.Concat(Enumerable.Range(6, 1001).Select(i => Row(i, "P" + i)))), "C6"));
        var path = Workbook(Row(6));
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Throws<IOException>(() => ExcelParameterSource.Read(path, "C6"));
    }

    [Theory]
    [InlineData("link_excel_table", false)]
    [InlineData("LINK_EXCEL_TABLE", true)]
    [InlineData("reload_excel_table", false)]
    [InlineData("RELOAD_EXCEL_TABLE", true)]
    public void External_table_mutations_are_blocked_in_batch(string command, bool continueOnError)
    {
        var invoked = new List<string>();
        var result = BatchExecutor.Run(new JArray(new JObject { ["command"] = command }, new JObject { ["command"] = "list_excel_tables" }),
            continueOnError, (name, _) => { invoked.Add(name); return BatchExecutor.StepResult.Success(null); });
        Assert.True(result.AnyFailed); Assert.DoesNotContain(command, invoked);
        Assert.Contains("cannot run inside batch_execute", (string?)result.Results[0]?["error"]);
        Assert.Equal(continueOnError ? new[] { "list_excel_tables" } : Array.Empty<string>(), invoked);
    }
}
