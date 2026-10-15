using System.IO.Compression;
using Bimwright.Ipt.Shared.Handlers.IPart;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class IPartCatalogTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"ipart-tests-"+Guid.NewGuid().ToString("N"));
    public IPartCatalogTests()=>Directory.CreateDirectory(_root);
    public void Dispose()=>Directory.Delete(_root,true);
    private static JArray Columns()=>IPartInput.Columns(JArray.Parse("[{name:'Length',kind:'parameter',target:'Length'}]"));
    private static string Text(string address,string text)=>$"<c r='{address}' t='inlineStr'><is><t>{text}</t></is></c>";
    private string Workbook(string rows,string? extra=null)
    {
        var path=Path.Combine(_root,Guid.NewGuid()+".xlsx");using var z=ZipFile.Open(path,ZipArchiveMode.Create);
        void Entry(string n,string value){using var w=new StreamWriter(z.CreateEntry(n).Open());w.Write(value);}
        Entry("xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Other' r:id='r1'/><sheet name='Shafts' r:id='r2'/></sheets></workbook>");
        Entry("xl/_rels/workbook.xml.rels","<Relationships><Relationship Id='r2' Target='/xl/worksheets/catalog.xml'/></Relationships>");
        Entry("xl/worksheets/catalog.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData>"+Text("C4","Code")+Text("D4","PN")+Text("E4","L")+rows+"</sheetData></worksheet>");
        if(extra!=null)Entry(extra,"ignored");return path;
    }
    private static string Row(int n,string member,string value)=>Text("C"+n,member)+Text("D"+n,member)+$"<c r='E{n}'>{value}</c>";
    private static JObject Map()=>JObject.Parse("{member:'Code',part_number:'PN',Length:'L'}");
    [Fact]
    public void Named_sheet_mapping_formula_cache_and_row_order_are_preserved()
    {
        var path=Workbook(Row(5,"B","<f>10+20</f><v>30</v>")+Row(6,"A","<v>20</v>"));
        var catalog=IPartExcelCatalog.Read(path,"Shafts","$c$4",Map(),Columns());
        Assert.Equal(new[]{"B","A"},catalog.Rows.Select(r=>(string)r["member"]!).ToArray());Assert.Equal("30",(string?)catalog.Rows[0]["values"]!["Length"]);
        catalog.VerifyUnchanged();File.AppendAllText(path,"change");Assert.Throws<InvalidOperationException>(()=>catalog.VerifyUnchanged());
    }
    [Theory]
    [InlineData("<f>10+20</f>")][InlineData("<v>NaN</v>")][InlineData("<v>Infinity</v>")]
    public void Unsaved_or_invalid_formula_results_are_rejected(string value)=>Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(Workbook(Row(5,"A",value)),"Shafts","C4",Map(),Columns()));
    [Theory]
    [InlineData("xl/vbaProject.bin")][InlineData("xl/externalLinks/externalLink1.xml")]
    public void Active_workbook_content_is_rejected(string entry)=>Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(Workbook(Row(5,"A","<v>10</v>"),entry),"Shafts","C4",Map(),Columns()));
    [Fact]
    public void Duplicate_members_and_blank_gaps_are_not_silently_accepted()
    {
        Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(Workbook(Row(5,"A","<v>10</v>")+Row(6,"a","<v>20</v>")),"Shafts","C4",Map(),Columns()));
        Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(Workbook(Row(5,"A","<v>10</v>")+Row(7,"B","<v>20</v>")),"Shafts","C4",Map(),Columns()));
    }
    [Fact]
    public void Members_beyond_the_scan_limit_are_not_silently_dropped()
    {
        var path=Workbook(Row(5,"A","<v>10</v>")+Row(5000,"B","<v>20</v>"));
        Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(path,"Shafts","C4",Map(),Columns()));
    }
    [Fact]
    public void Empty_configured_cells_fail_before_native_table_writes()
    {
        var columns=IPartInput.Columns(JArray.Parse("[{name:'Length',kind:'parameter',target:'Length'},{name:'Description',kind:'description'}]"));
        var mapping=Map();mapping["Description"]="Description";
        var path=Workbook(Text("F4","Description")+Row(5,"A","<v>10</v>"));
        Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(path,"Shafts","C4",mapping,columns));
        Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(Workbook(Text("F4","Description")+Row(5,"A","")),"Shafts","C4",mapping,columns));
        Assert.Throws<ArgumentException>(()=>IPartInput.Rows(JArray.Parse("[{member:'A',values:{Length:10,Description:''}}]"),columns));
    }
    [Fact]
    public void Incorrect_mapping_or_missing_sheet_fails()
    {
        var path=Workbook(Row(5,"A","<v>10</v>"));Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(path,"Missing","C4",Map(),Columns()));
        Assert.Throws<ArgumentException>(()=>IPartExcelCatalog.Read(path,"Shafts","C4",JObject.Parse("{member:'Code',part_number:'PN',Length:'Missing'}"),Columns()));
    }
}
