using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Logging;
using Bimwright.Ipt.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class DrawingPhase2Tests
{
    [Theory]
    [InlineData("add_drawing_note", "{name:'N',text:'t',position_mm:[1,2],kind:'leader'}")]
    [InlineData("add_drawing_note", "{name:'N',text:'t',position_mm:[1,2],kind:'leader',view:'V',intent:{model_edge:'body:1/edge:1',typo:1}}")]
    [InlineData("add_drawing_note", "{name:'N',text:'t',position_mm:[1,2],kind:'leader',view:'V',intent:{}}")]
    [InlineData("add_drawing_note", "{name:'N',text:12,position_mm:[1,2]}")]
    [InlineData("add_drawing_note", "{name:'N',text:'t',position_mm:[1,2],intent:{}}")]
    [InlineData("add_drawing_note", "{name:'N',text:'t',position_mm:[1,2],kind:'leader',view:'V',intent:{},box_mm:{width:10,height:10}}")]
    [InlineData("add_drawing_note", "{name:'N',text:'t',position_mm:[1,2],box_mm:{width:10,height:0}}")]
    [InlineData("add_drawing_note", "{name:'N',text:'t',position_mm:[1,2],box_mm:{width:10,height:10,other:true}}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[],rows:[],position_mm:[1,2]}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:0}],rows:[],position_mm:[1,2]}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:10,other:1}],rows:[],position_mm:[1,2]}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:10}],rows:[['A','B']],position_mm:[1,2]}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:10}],rows:[[12]],position_mm:[1,2]}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:10}],rows:[['A']],position_mm:[1,2],row_heights_mm:[]}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:10}],rows:[['A']],position_mm:[1,2],row_heights_mm:[-2]}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:10}],rows:[['A']],position_mm:[1,2],row_heights_mm:[null]}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:10}],rows:[],position_mm:[1,2],anchor:'bottom_left'}")]
    public void Invalid_nested_values_are_rejected_at_boundary(string command, string json) =>
        Assert.Throws<ArgumentException>(() => DrawingInput.Validate(command, DrawingInput.Normalize(command, JObject.Parse(json))));

    [Theory]
    [InlineData("add_drawing_note", "{name:'N',text:'literal < & >',position_mm:[1,2],style:'Existing text style',layer:'Existing layer',box_mm:{width:30,height:10}}")]
    [InlineData("add_drawing_note", "{name:'N',text:'t',position_mm:[1,2],kind:'leader',view:'V',intent:{model_edge:'body:1/edge:1',point_intent:'center'},style:'Existing dimension style'}")]
    [InlineData("add_drawing_table", "{name:'T',columns:[{heading:'H',width_mm:30}],rows:[],position_mm:[1,2],row_heights_mm:[],style:'Existing table style'}")]
    public void Template_style_names_and_empty_tables_are_valid(string command, string json) =>
        DrawingInput.Validate(command, DrawingInput.Normalize(command, JObject.Parse(json)));

    [Fact]
    public void Table_engineering_limits_and_nonfinite_dimensions_are_enforced()
    {
        var p = JObject.Parse("{name:'T',columns:[{heading:'H',width_mm:30}],rows:[],position_mm:[1,2]}");
        p["rows"] = new JArray(Enumerable.Range(0, 500).Select(_ => new JArray("A"))); DrawingInput.Validate("add_drawing_table", p);
        ((JArray)p["rows"]!).Add(new JArray("A")); Assert.Throws<ArgumentException>(() => DrawingInput.Validate("add_drawing_table", p));
        p["rows"] = new JArray(); p["columns"] = new JArray(Enumerable.Range(0, 51).Select(_ => JObject.Parse("{heading:'H',width_mm:30}"))); Assert.Throws<ArgumentException>(() => DrawingInput.Validate("add_drawing_table", p));
        p["columns"] = new JArray(new JObject { ["heading"] = "H", ["width_mm"] = double.PositiveInfinity }); Assert.Throws<ArgumentException>(() => DrawingInput.Validate("add_drawing_table", p));
    }

    [Theory]
    [InlineData("add_drawing_note")][InlineData("add_drawing_table")]
    public void Writes_are_blocked_in_batch_and_have_meaningful_history(string command)
    {
        Assert.Contains(command, BatchExecutor.BlockedCommands);
        Assert.Equal("created Fixture", SummaryGenerator.Generate(command, null, "{created:true,name:'Fixture'}", true, null));
        Assert.Equal("existing Fixture", SummaryGenerator.Generate(command, null, "{created:false,name:'Fixture'}", true, null));
    }

    [Theory]
    [InlineData("inventor_add_drawing_note")][InlineData("inventor_add_drawing_table")]
    public void Oversized_completed_creates_preserve_name_and_outcome(string tool)
    {
        var data = new JObject { ["created"] = true, ["created_count"] = 1, ["name"] = "Fixture", ["text"] = new string('x', 1100000) };
        var result = AgentOutputGuardTests.Data(AgentOutputGuard.Apply(tool, AgentOutputGuardTests.Result(data), new InventorMcpConfig()));
        Assert.True(result.Value<bool>("created")); Assert.True(result.Value<bool>("response_compacted")); Assert.Equal(1, result.Value<int>("created_count")); Assert.Equal("Fixture", result.Value<string>("name")); Assert.Null(result["outcome_unknown"]);
    }
}
