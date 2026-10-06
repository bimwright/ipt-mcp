using System.Reflection;
using System.Text.Json;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

[CollectionDefinition("Process-wide privacy state", DisableParallelization = true)]
public sealed class PrivacyStateCollection { }

public sealed class DrawingPhase3Tests
{
    [Theory]
    [InlineData("find_view_geometry", "{view:'V',region_mm:{min:[0,0],max:[10,10]}}")]
    [InlineData("find_view_geometry", "{view:'V',occurrence_path:'Sub:1/Part:1'}")]
    [InlineData("sketch_on_view", "{name:'Markup',entities:[{line:{from:[0,0],to:[10,10]}},{circle:{center:[0,0],radius_mm:5}},{arc:{center:[0,0],radius_mm:4,start_deg:20,sweep_deg:-90}},{text:{text:'<literal>',position:[1,2],font_size_mm:3}}]}")]
    [InlineData("hide_view_edges", "{document:'D',sheet:'S',view:'V',max_model_size_mm:3,dry_run:true}")]
    [InlineData("create_design_view", "{name:'Detail',occurrence_visibility:[{occurrence_path:'Sub:1/Part:1',visible:false}],appearance:[{occurrence_path:'Sub:1/Part:1',asset:'Steel'}]}")]
    public void Valid_contracts_are_accepted(string command, string input) => DrawingPhase3Input.Validate(command, JObject.Parse(input));

    [Theory]
    [InlineData("find_view_geometry", "{view:'V'}")]
    [InlineData("find_view_geometry", "{view:'V',model_edge:'body:1/edge:1',model_point_mm:[1,2,3]}")]
    [InlineData("find_view_geometry", "{view:'V',region_mm:{min:[0,0],max:[0,10]}}")]
    [InlineData("find_view_geometry", "{view:'V',occurrence_path:'P',max_items:1001}")]
    [InlineData("find_view_geometry", "{view:'V',occurrence_path:'P',offset:0.5}")]
    [InlineData("sketch_on_view", "{name:'M',space:'view_local',entities:[{text:{text:'X',position:[0,0]}}]}")]
    [InlineData("sketch_on_view", "{name:'M',entities:[{line:{from:[0,0],to:[0,0]}}]}")]
    [InlineData("sketch_on_view", "{name:'M',entities:[{arc:{center:[0,0],radius_mm:1,start_deg:0,sweep_deg:360}}]}")]
    [InlineData("sketch_on_view", "{name:'M',entities:[{circle:{center:[0,0],radius_mm:-1}}]}")]
    [InlineData("sketch_on_view", "{name:'M',entities:[{text:{text:'X',position:[0,0]}},{spline:{points:[]}}]}")]
    [InlineData("hide_view_edges", "{view:'V',max_model_size_mm:1}")]
    [InlineData("hide_view_edges", "{document:'D',sheet:'S',view:'V',max_model_size_mm:0}")]
    [InlineData("hide_view_edges", "{document:'D',sheet:'S',view:'V',max_model_size_mm:1,timeout_ms:1}")]
    [InlineData("create_design_view", "{name:'D',appearance:[{occurrence_path:'P',asset:'Steel',rgb:[0,0,0]}]}")]
    [InlineData("create_design_view", "{name:'D',occurrence_visibility:[{occurrence_path:'P',visible:false},{occurrence_path:'P',visible:true}]}")]
    public void Invalid_contracts_fail_before_native_access(string command, string input) => Assert.Throws<ArgumentException>(() => DrawingPhase3Input.Validate(command, JObject.Parse(input)));

    [Theory]
    [InlineData("{geometry_id:'curve:1'}")]
    [InlineData("{geometry_id:'curve:1',revision:'r',occurrence_path:'P'}")]
    [InlineData("{geometry_id:'curve:1',revision:'r',model_edge:'body:1/edge:1'}")]
    [InlineData("{model_point_mm:[0,0,0],revision:'r'}")]
    public void Invalid_geometry_identity_contract_is_rejected(string input) => Assert.Throws<ArgumentException>(() => DrawingPhase3Input.GeometryIntent(JObject.Parse(input)));

    [Fact]
    public void Geometry_identity_is_accepted_by_all_annotation_contracts()
    {
        const string intent = "{geometry_id:'curve:1',revision:'rev',point_intent:'center'}";
        DrawingInput.Validate("add_drawing_dimension", JObject.Parse("{items:[{name:'D',view:'V',kind:'diameter',intents:[" + intent + "],text_position_mm:[1,2]}]}"));
        DrawingInput.Validate("add_drawing_note", JObject.Parse("{name:'N',kind:'leader',view:'V',text:'X',position_mm:[1,2],intent:" + intent + "}"));
        DrawingInput.Validate("add_balloon", JObject.Parse("{items:[{name:'B',view:'V',occurrence_path:'P',text:'1',position_mm:[1,2],intent:" + intent + "}]}"));
        DrawingInput.Validate("add_drawing_symbol", JObject.Parse("{name:'C',kind:'centermark',view:'V',intent:" + intent + "}"));
    }

    [Theory]
    [InlineData("sketch_on_view")][InlineData("hide_view_edges")][InlineData("create_design_view")]
    public void Writes_reject_readonly_without_touching_the_host(string command)
    {
        IInventorCommand handler = command == "create_design_view" ? new Bimwright.Ipt.Shared.Handlers.Assembly.CreateDesignViewHandler() : new Bimwright.Ipt.Shared.Handlers.Drawing.DrawingCommandHandler(command);
        Assert.Equal("READ_ONLY", handler.Execute(new InventorCommandContext { ReadOnly = true }, new JObject()).Error!.Code);
        Assert.Contains(command, BatchExecutor.BlockedCommands);
    }

    [Fact]
    public void All_four_declarations_have_explicit_hints_and_geometry_query_survives_readonly()
    {
        var methods = new[] { typeof(DrawingTools), typeof(DrawingQueryTools), typeof(AssemblyTools) }.SelectMany(t => t.GetMethods()).Where(m => new[] { "FindViewGeometry", "SketchOnView", "HideViewEdges", "CreateDesignView" }.Contains(m.Name)).ToArray();
        Assert.Equal(4, methods.Length);
        foreach (var method in methods)
        {
            var declaration = method.CustomAttributes.Single(a => a.AttributeType == typeof(McpServerToolAttribute));
            foreach (var hint in new[] { "ReadOnly", "Destructive", "Idempotent", "OpenWorld" }) Assert.Contains(declaration.NamedArguments, a => a.MemberName == hint);
            var tool = method.GetCustomAttribute<McpServerToolAttribute>()!;
            Assert.Equal(method.Name == "FindViewGeometry", tool.ReadOnly); Assert.True(tool.Idempotent); Assert.False(tool.OpenWorld);
        }
        Assert.Contains(typeof(DrawingQueryTools), Program.ResolveToolTypesForRegistration(new InventorMcpConfig { ReadOnly = true }));
        Assert.Contains("max_items", ResponseSizePolicyCatalog.GetNarrowingHint("find_view_geometry"));
    }
}
