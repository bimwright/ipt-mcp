using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Handlers.IPart;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class IPartInputTests
{
    private static JArray Columns() => IPartInput.Columns(JArray.Parse("[{name:'L',kind:'parameter',target:'Length'},{name:'Hole',kind:'suppression',target:'Cut1'},{name:'Material',kind:'material'}]"));
    [Theory]
    [InlineData("../escape")][InlineData("A\\B")][InlineData("CON")][InlineData("COM1.part")][InlineData("NUL")]
    [InlineData("x.ipt")][InlineData("x.")][InlineData("=formula")][InlineData("bad:name")][InlineData("")]
    public void Member_paths_and_device_names_are_rejected(string value) => Assert.Throws<ArgumentException>(()=>IPartInput.Member(value));
    [Theory]
    [InlineData("Shaft-01")][InlineData("Bushing 25x30")][InlineData("BRACKET_A.2")]
    public void Member_identifiers_preserve_valid_names(string value) => Assert.Equal(value,IPartInput.Member(value));
    [Fact]
    public void Complete_typed_rows_preserve_expressions_and_normalize_suppression()
    {
        var rows=IPartInput.Rows(JArray.Parse("[{member:'A',values:{L:'Length / 2',Hole:true,Material:'Steel'}},{member:'B',part_number:'B-42',values:{L:25.5,Hole:'COMPUTE',Material:'Steel'}}]"),Columns());
        Assert.Equal("Length / 2",(string?)rows[0]["values"]!["L"]);Assert.Equal("suppress",(string?)rows[0]["values"]!["Hole"]);
        Assert.Equal("25.5",(string?)rows[1]["values"]!["L"]);Assert.Equal("compute",(string?)rows[1]["values"]!["Hole"]);
        Assert.Equal("A",(string?)rows[0]["part_number"]);
    }
    [Theory]
    [InlineData("[{member:'A',values:{L:1,Hole:false,Material:'Steel'}},{member:'a',values:{L:2,Hole:false,Material:'Steel'}}]")]
    [InlineData("[{member:'A',values:{L:1,Hole:false}}]")]
    [InlineData("[{member:'A',values:{L:1,Hole:false,Material:'Steel',extra:1}}]")]
    [InlineData("[{member:'A',values:{L:true,Hole:false,Material:'Steel'}}]")]
    [InlineData("[{member:'A',values:{L:1,Hole:'maybe',Material:'Steel'}}]")]
    [InlineData("[{member:'A',values:{L:1,Hole:false,Material:'=HYPERLINK(...)'}}]")]
    public void Invalid_rows_are_rejected_before_native_work(string json) => Assert.Throws<ArgumentException>(()=>IPartInput.Rows(JArray.Parse(json),Columns()));
    [Theory]
    [InlineData("[{name:'member',kind:'parameter',target:'L'}]")]
    [InlineData("[{name:'A',kind:'custom',target:'L'}]")]
    [InlineData("[{name:'A',kind:'parameter',target:'L'},{name:'B',kind:'parameter',target:'L'}]")]
    [InlineData("[{name:'A',kind:'parameter',target:'L'},{name:'a',kind:'material'}]")]
    public void Ambiguous_or_unsupported_columns_are_rejected(string json) => Assert.Throws<ArgumentException>(()=>IPartInput.Columns(JArray.Parse(json)));
    [Fact]
    public void Member_selection_rejects_duplicates_and_large_batches()
    {
        Assert.Throws<ArgumentException>(()=>IPartInput.Members(new JArray("A","a")));
        var members=new JArray(Enumerable.Range(1,51).Select(i=>"M"+i));
        Assert.Throws<ArgumentException>(()=>IPartInput.Members(members));
    }
    [Theory]
    [InlineData("create_ipart_factory")][InlineData("UPDATE_IPART_TABLE")][InlineData("set_ipart_row")]
    [InlineData("generate_ipart_members")][InlineData("sync_ipart_excel")][InlineData("place_ipart_member")][InlineData("change_ipart_member")]
    public void Stateful_ipart_writes_are_refused_by_batch_before_dispatch(string command)
    {
        bool dispatched=false;
        var outcome=BatchExecutor.Run(new JArray(new JObject{["command"]=command,["params"]=new JObject()}),true,(_,_)=>{dispatched=true;return BatchExecutor.StepResult.Success(null);});
        Assert.False(dispatched);Assert.False(outcome.Results[0].Value<bool>("ok"));
    }
}
