using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Logging;
using Bimwright.Ipt.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class DrawingPhase2LifecycleTests
{
    [Theory]
    [InlineData("add_drawing_symbol", "{name:'S',kind:'symbol',definition:'D',position_mm:[1,2],prompts:{L:'100'},scale:1}")]
    [InlineData("add_drawing_symbol", "{name:'C',kind:'centermark',view:'V',intent:{model_edge:'body:1/edge:1',point_intent:'center'}}")]
    [InlineData("edit_drawing_annotation", "{items:[{kind:'note',locator:{document_id:'D',sheet_id:'S',kind:'note',reference_key:'AQ=='},changes:{text_override:'literal < & >'}}]}")]
    [InlineData("delete_drawing_items", "{document:'D',sheet:'S',selector:{kind:'all'}}")]
    [InlineData("delete_drawing_items", "{document:'D',sheet:'S',dry_run:false,items:[{kind:'view',name:'V'}]}")]
    [InlineData("edit_drawing_table", "{name:'T',changes:{insert_rows:[{index:1,rows:[['A','B']]}]}}")]
    [InlineData("set_drawing_styles", "{changes:{text_styles:[{name:'T',bold:true}],layers:[{name:'L',color_rgb:[12,34,56]}],dimension_styles:[{name:'D',units:'inch',display_format:'fractional'}],object_defaults:[{kind:'general_note',style:'T'}]}}")]
    [InlineData("edit_sheet", "{document:'D',sheet:'S',changes:{delete:true},contents:[]}")]
    public void Supported_contracts_validate(string command, string input) => DrawingInput.Validate(command, DrawingInput.Normalize(command, JObject.Parse(input)));

    [Theory]
    [InlineData("add_drawing_symbol", "{name:'C',kind:'centermark',view:'V',intent:{model_edge:'E'},definition:'D'}")]
    [InlineData("add_drawing_symbol", "{name:'S',kind:'symbol',definition:'D',position_mm:[1,2],intent:{model_edge:'E'}}")]
    [InlineData("add_drawing_symbol", "{name:'S',kind:'symbol',definition:'D',position_mm:[1,2],scale:0}")]
    [InlineData("edit_drawing_annotation", "{items:[]}")]
    [InlineData("edit_drawing_annotation", "{items:[{kind:'note',name:'N',locator:{},changes:{text_override:'X'}}]}")]
    [InlineData("edit_drawing_annotation", "{items:[{kind:'note',name:'N',changes:{precision:2}}]}")]
    [InlineData("edit_drawing_annotation", "{items:[{kind:'dimension',name:'N',changes:{leader_arrowhead:'open'}}]}")]
    [InlineData("edit_drawing_annotation", "{items:[{kind:'note',name:'N',changes:{text_override:'X'}},{kind:'note',name:'N',changes:{text_override:'Y'}}]}")]
    [InlineData("edit_drawing_annotation", "{items:[{kind:'note',locator:{document_id:'D',sheet_id:'S',kind:'symbol',reference_key:'AQ=='},changes:{text_override:'X'}}]}")]
    [InlineData("edit_drawing_annotation", "{items:[{kind:'note',locator:{document_id:'D',sheet_id:'S',kind:'note',reference_key:'bad'},changes:{text_override:'X'}}]}")]
    [InlineData("delete_drawing_items", "{sheet:'S',selector:{kind:'all'}}")]
    [InlineData("delete_drawing_items", "{document:'D',sheet:'S',dry_run:false,selector:{kind:'all'}}")]
    [InlineData("delete_drawing_items", "{document:'D',sheet:'S',dry_run:false,items:[]}")]
    [InlineData("delete_drawing_items", "{document:'D',sheet:'S',selector:{kind:'note',names:[null]}}")]
    [InlineData("delete_drawing_items", "{document:'D',sheet:'S',selector:{kind:'note',names:['N','N']}}")]
    [InlineData("delete_drawing_items", "{document:'D',sheet:'S',selector:{kind:'all',region_mm:{min:[0,0],max:[0,1]}}}")]
    [InlineData("edit_drawing_table", "{name:'T',changes:{header_height_mm:10}}")]
    [InlineData("edit_drawing_table", "{name:'T',changes:{delete_rows:[null]}}")]
    [InlineData("edit_drawing_table", "{name:'T',changes:{delete_rows:[1,1]}}")]
    [InlineData("edit_drawing_table", "{name:'T',changes:{cells:[{row:0,column:1,text:'X'}]}}")]
    [InlineData("edit_drawing_table", "{name:'T',changes:{cells:[{row:1.2,column:1,text:'X'}]}}")]
    [InlineData("set_drawing_styles", "{changes:{layers:[{name:'L',color_rgb:[12,null,56]}]}}")]
    [InlineData("set_drawing_styles", "{changes:{layers:[{name:'L',color_rgb:[12,256,56]}]}}")]
    [InlineData("set_drawing_styles", "{changes:{dimension_styles:[{name:'D',display_format:'fractional',linear_precision:2}]}}")]
    [InlineData("set_drawing_styles", "{changes:{text_styles:[{name:'T',bold:true},{name:'T',italic:true}]}}")]
    [InlineData("set_drawing_styles", "{changes:{text_styles:[{name:'T',bold:1}]}}")]
    [InlineData("set_drawing_styles", "{changes:{object_defaults:[{kind:'table',font:'Arial'}]}}")]
    [InlineData("edit_sheet", "{sheet:'S',changes:{name:'X',index:2}}")]
    [InlineData("edit_sheet", "{sheet:'S',changes:{width_mm:100}}")]
    [InlineData("edit_sheet", "{sheet:'S',changes:{width_mm:100,height_mm:200,orientation:'landscape'}}")]
    [InlineData("edit_sheet", "{sheet:'S',changes:{width_mm:200,height_mm:100,orientation:'portrait'}}")]
    [InlineData("edit_sheet", "{sheet:'S',changes:{active:false}}")]
    [InlineData("edit_sheet", "{sheet:'S',changes:{delete:true},contents:[]}")]
    public void Ambiguous_or_unsupported_contracts_fail_before_host(string command, string input) => Assert.Throws<ArgumentException>(() => DrawingInput.Validate(command, DrawingInput.Normalize(command, JObject.Parse(input))));

    [Fact]
    public void Document_selector_accepts_a_loaded_path_longer_than_an_item_name()
    {
        var input = JObject.Parse("{sheet:'S',changes:{name:'Renamed'}}");
        input["document"] = "C:\\" + new string('x', 140) + "\\fixture.idw";
        DrawingInput.Validate("edit_sheet", DrawingInput.Normalize("edit_sheet", input));
    }

    [Fact]
    public void Table_plan_addresses_final_rows_after_original_deletion_and_sequential_inserts()
    {
        var rows = JArray.Parse("[['A','1'],['B','2'],['C','3']]");
        var c = JObject.Parse("{delete_rows:[2],insert_rows:[{index:2,rows:[['D','4'],['E','5']]}],cells:[{row:3,column:2,text:'6'}],row_heights_mm:[7,8,9,10],column_widths_mm:[30,40]}");
        Assert.True(JToken.DeepEquals(JArray.Parse("[['A','1'],['D','4'],['E','6'],['C','3']]"), DrawingTableEditPlan.Apply(rows, 2, c)));
        Assert.Equal("B", rows[1]![0]!.Value<string>());
    }
    [Theory]
    [InlineData("{delete_rows:[3]}")][InlineData("{cells:[{row:1,column:0,text:'X'}]}")]
    [InlineData("{cells:[{row:1,column:1,text:'X'},{row:1,column:1,text:'Y'}]}")]
    [InlineData("{insert_rows:[{index:4,rows:[['A','B']]}]}")][InlineData("{insert_rows:[{index:1,rows:[['A']]}]}")]
    [InlineData("{column_widths_mm:[30]}")][InlineData("{row_heights_mm:[7]}")]
    public void Table_plan_refuses_invalid_final_addresses_and_cardinality(string changes) => Assert.Throws<ArgumentException>(() => DrawingTableEditPlan.Apply(JArray.Parse("[['A','1'],['B','2']]"), 2, JObject.Parse(changes)));
    [Fact]
    public void Exact_deletion_requires_dependents_and_complete_sheet_contents()
    {
        DrawingTableEditPlan.RequireDependencies(["V", "C"], ["C"]);
        Assert.Throws<ArgumentException>(() => DrawingTableEditPlan.RequireDependencies(["V"], ["C"]));
        DrawingTableEditPlan.RequireSheetDeletion(2, ["V", "B"], ["B", "V"]);
        Assert.Throws<ArgumentException>(() => DrawingTableEditPlan.RequireSheetDeletion(1, [], []));
        Assert.Throws<ArgumentException>(() => DrawingTableEditPlan.RequireSheetDeletion(2, ["V"], ["V", "B"]));
        Assert.Throws<ArgumentException>(() => DrawingTableEditPlan.RequireSheetDeletion(2, ["V", "V"], ["V", "B"]));
    }
    [Theory]
    [InlineData("add_drawing_symbol", "{created:true,name:'Level'}", "created Level")]
    [InlineData("edit_drawing_annotation", "{updated:true,updated_count:2}", "2 annotations updated")]
    [InlineData("delete_drawing_items", "{dry_run:true,count:2}", "preview 2 items")]
    [InlineData("delete_drawing_items", "{deleted:true,deleted_count:2}", "deleted 2 items")]
    [InlineData("edit_drawing_table", "{updated:true,rebuilt:true,name:'T'}", "rebuilt T")]
    [InlineData("set_drawing_styles", "{updated:true,count:2,affected_count:3}", "2 styles / 3 affected items")]
    [InlineData("edit_sheet", "{deleted:true,sheet:'S'}", "deleted sheet S")]
    public void Every_added_write_has_history_and_batch_blocking(string command, string data, string expected)
    {
        Assert.Contains(command, BatchExecutor.BlockedCommands); Assert.Equal(expected, SummaryGenerator.Generate(command, null, data, true, null));
    }
    [Theory]
    [InlineData("edit_drawing_annotation", "updated")][InlineData("delete_drawing_items", "deleted")]
    [InlineData("edit_drawing_table", "updated")][InlineData("set_drawing_styles", "updated")][InlineData("edit_sheet", "deleted")]
    public void Oversized_completed_edits_and_deletes_preserve_outcome_and_counts(string command, string outcome)
    {
        var data = new JObject { [outcome] = true, [outcome + "_count"] = 2, ["name"] = "Fixture", ["details"] = new string('x', 1100000) };
        var result = AgentOutputGuardTests.Data(AgentOutputGuard.Apply("inventor_" + command, AgentOutputGuardTests.Result(data), new InventorMcpConfig()));
        Assert.True(result.Value<bool>(outcome)); Assert.Equal(2, result.Value<int>(outcome + "_count")); Assert.Null(result["outcome_unknown"]); Assert.True(result.Value<bool>("response_compacted"));
    }
}
