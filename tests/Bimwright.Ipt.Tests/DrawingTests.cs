using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class DrawingTests : IDisposable
{
    [Theory]
    [InlineData("new_drawing")][InlineData("add_sheet")][InlineData("set_title_block")]
    [InlineData("add_drawing_view")][InlineData("add_section_view")][InlineData("edit_drawing_view")]
    [InlineData("add_drawing_dimension")][InlineData("add_balloon")][InlineData("export_drawing")][InlineData("capture_sheet")]
    [InlineData("add_drawing_note")][InlineData("add_drawing_table")]
    [InlineData("add_drawing_symbol")][InlineData("edit_drawing_annotation")][InlineData("delete_drawing_items")][InlineData("edit_drawing_table")][InlineData("set_drawing_styles")][InlineData("edit_sheet")]
    public void Direct_write_handler_rejects_readonly_before_validation_or_host_access(string command)
    {
        var handler = new Bimwright.Ipt.Shared.Handlers.Drawing.DrawingCommandHandler(command);
        var result = handler.Execute(new Bimwright.Ipt.Shared.Infrastructure.InventorCommandContext { ReadOnly = true }, new JObject());
        Assert.False(result.Ok); Assert.Equal("READ_ONLY", result.Error!.Code);
    }

    [Fact]
    public void Drawing_partial_failure_preserves_effects_and_has_failed_history_outcome()
    {
        var data=JObject.Parse("{ok:false,completed_count:1,failed_count:1,files:[{ok:true,path:'first.pdf'}],error:'Second export failed.'}");
        var result=InventorCommandResult.Success(Guid.Empty,data,new InventorResponseMeta());
        DrawingResponsePolicy.NormalizeOutcome(result);
        Assert.False(result.Ok); Assert.Same(data,result.Data); Assert.Equal("Second export failed.",result.Error!.Message);
        Assert.Equal("Second export failed.",Bimwright.Ipt.Shared.Logging.SummaryGenerator.Generate("export_drawing",null,data.ToString(),result.Ok,result.Error.Message));
    }

    [Fact]
    public async Task Failed_drawing_transport_preserves_partial_file_effects()
    {
        using var pipe=new NamedPipeServerStream(_pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
        var receive=Task.Run(async()=>{
            await pipe.WaitForConnectionAsync();using var reader=new StreamReader(pipe,leaveOpen:true);using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
            var request=JObject.Parse((await reader.ReadLineAsync())!);
            await writer.WriteLineAsync(new JObject{["id"]=request["id"],["ok"]=false,["error"]=new JObject{["code"]="API_ERROR",["message"]="Partial export."},["data"]=new JObject{["ok"]=false,["completed_count"]=1,["failed_count"]=1,["files"]=new JArray(new JObject{["path"]="first.pdf"})}}.ToString(Formatting.None));
        });
        var data=await _client.SendAsync("export_drawing",new JObject(),CancellationToken.None);await receive;
        Assert.False(data.Value<bool>("ok"));Assert.Equal(1,data.Value<int>("completed_count"));Assert.Equal("first.pdf",(string?)data["files"]![0]!["path"]);
    }
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-drawing-wire-" + Guid.NewGuid().ToString("N"));
    private readonly string _pipe = "IptDrawingTest-" + Guid.NewGuid().ToString("N");
    private readonly PluginClient _client;
    public DrawingTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor { TargetId = "inventor-2027-" + Environment.ProcessId, InventorYear = 2027, ProcessId = Environment.ProcessId, HostApp = "Inventor", Transport = "pipe", PipeName = _pipe, AuthToken = "fixture-token", LastHeartbeatUtc = DateTimeOffset.UtcNow }));
        _client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir, TimeoutMs = 5000 });
    }
    public void Dispose() => Directory.Delete(_dir, true);
    [Theory]
    [InlineData("get_drawing_info")]
    [InlineData("new_drawing")]
    [InlineData("add_sheet")]
    [InlineData("set_title_block")]
    [InlineData("add_drawing_view")]
    [InlineData("add_section_view")]
    [InlineData("edit_drawing_view")]
    [InlineData("add_drawing_dimension")]
    [InlineData("add_balloon")]
    [InlineData("export_drawing")]
    [InlineData("capture_sheet")]
    [InlineData("add_drawing_note")][InlineData("add_drawing_table")]
    [InlineData("add_drawing_symbol")][InlineData("edit_drawing_annotation")][InlineData("delete_drawing_items")][InlineData("edit_drawing_table")][InlineData("set_drawing_styles")][InlineData("edit_sheet")]
    public async Task Every_wrapper_invokes_transport_and_preserves_timeout_and_snake_case(string command)
    {
        using var server = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var receive = Receive(server, cancellation.Token);
        var tools = new DrawingTools(_client);
        const int timeout = 12000;
        var intent = JsonDocument.Parse("{\"model_edge\":\"body:1/edge:1\",\"point_intent\":\"start\"}").RootElement.Clone();
        var invoke = command switch
        {
            "get_drawing_info" => new DrawingQueryTools(_client).GetDrawingInfo(document: "D.idw", timeout_ms: timeout),
            "new_drawing" => tools.NewDrawing("D", Path.Combine(_dir, "template.idw"), timeout_ms: timeout),
            "add_sheet" => tools.AddSheet("Sheet2", document: "D.idw", timeout_ms: timeout),
            "set_title_block" => tools.SetTitleBlock("D.idw", title_block: "Title", timeout_ms: timeout),
            "add_drawing_view" => tools.AddDrawingView("Front", "base", [100, 100], model: "Block.ipt", timeout_ms: timeout),
            "add_section_view" => tools.AddSectionView("Section", "Front", [[10, 10], [20, 20]], [100, 100], timeout_ms: timeout),
            "edit_drawing_view" => tools.EditDrawingView("D.idw", "Sheet2", "Front", scale: 0.5, timeout_ms: timeout),
            "add_drawing_dimension" => tools.AddDrawingDimension([new DrawingDimensionItem { Name = "Dim1", View = "Front", Kind = "aligned", Intents = [intent, intent], TextPosition = [80, 80] }], timeout_ms: timeout),
            "add_balloon" => tools.AddBalloon([new DrawingBalloonItem { Name = "B1", View = "Front", OccurrencePath = "Part:1", Text = "1", Position = [80, 80] }], symbol: "Balloon", timeout_ms: timeout),
            "export_drawing" => tools.ExportDrawing("D.idw", "pdf", Path.Combine(_dir, "out.pdf"), all_sheets: true, timeout_ms: timeout),
            "add_drawing_note" => tools.AddDrawingNote("Note", "Text", [80, 80], timeout_ms: timeout),
            "add_drawing_table" => tools.AddDrawingTable("Table", [new DrawingTableColumn { Heading = "ITEM", WidthMm = 30 }], [["A"]], [80, 80], timeout_ms: timeout),
            "add_drawing_symbol" => tools.AddDrawingSymbol("Level", "symbol", definition: "Level", position_mm: [80, 80], timeout_ms: timeout),
            "edit_drawing_annotation" => tools.EditDrawingAnnotation([JsonDocument.Parse("{\"kind\":\"note\",\"name\":\"Note\",\"changes\":{\"text_override\":\"Updated\"}}").RootElement.Clone()], timeout_ms: timeout),
            "delete_drawing_items" => tools.DeleteDrawingItems("D.idw", "S", selector: JsonDocument.Parse("{\"kind\":\"all\"}").RootElement.Clone(), timeout_ms: timeout),
            "edit_drawing_table" => tools.EditDrawingTable("Table", JsonDocument.Parse("{\"title\":\"Updated\"}").RootElement.Clone(), timeout_ms: timeout),
            "set_drawing_styles" => tools.SetDrawingStyles(JsonDocument.Parse("{\"text_styles\":[{\"name\":\"Text\",\"bold\":true}]}").RootElement.Clone(), timeout_ms: timeout),
            "edit_sheet" => tools.EditSheet("S", JsonDocument.Parse("{\"active\":true}").RootElement.Clone(), timeout_ms: timeout),
            "capture_sheet" => tools.CaptureSheet("D.idw", "Sheet2", timeout_ms: timeout),
            _ => throw new Exception()
        };
        var envelope = await receive; var reply = JObject.Parse(await invoke);
        Assert.True(reply.Value<bool>("accepted")); Assert.Equal(command, (string?)envelope["command"]); Assert.Equal(timeout, (int)envelope["timeout_ms"]!);
        var p = Assert.IsType<JObject>(envelope["params"]); Assert.Equal(timeout, (int)p["timeout_ms"]!);
        if (command == "add_drawing_dimension") { Assert.Equal("Dim1", (string?)p["items"]![0]!["name"]); Assert.NotNull(p["items"]![0]!["text_position_mm"]); Assert.Null(p["items"]![0]!["Name"]); }
        if (command == "add_balloon") Assert.Equal("Part:1", (string?)p["items"]![0]!["occurrence_path"]);
        if (command == "export_drawing") Assert.False(p.Value<bool>("overwrite_existing"));
        Assert.Equal("inventor_" + command, (string?)envelope["tool"]?["name"]);
        Assert.Equal(command == "get_drawing_info" ? "drawing_query" : "drawing", (string?)envelope["tool"]?["toolset"]);
        Assert.Equal(timeout, (int)envelope["tool"]!["timeout_ms"]!);
    }
    private static async Task<JObject> Receive(NamedPipeServerStream server, CancellationToken ct)
    {
        await server.WaitForConnectionAsync(ct); using var reader = new StreamReader(server, leaveOpen: true); using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var envelope = JObject.Parse((await reader.ReadLineAsync(ct))!);
        await writer.WriteLineAsync(new JObject { ["id"] = envelope["id"], ["ok"] = true, ["data"] = new JObject { ["accepted"] = true }, ["meta"] = new JObject() }.ToString(Formatting.None)); return envelope;
    }
    [Fact]
    public void Readonly_surface_has_only_the_drawing_query_and_all_new_tools_have_explicit_hints()
    {
        var config = new InventorMcpConfig { ReadOnly = true, Toolsets = { "all" }, EnableSendCode = true }; var types = Program.ResolveToolTypesForRegistration(config);
        Assert.Contains(typeof(DrawingQueryTools), types); Assert.DoesNotContain(typeof(DrawingTools), types);
        var methods = new[] { typeof(DrawingTools), typeof(DrawingQueryTools) }.SelectMany(t => t.GetMethods()).Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null).ToArray(); Assert.Equal(19, methods.Length);
        foreach (var m in methods)
        {
            var declaration = m.CustomAttributes.Single(a => a.AttributeType == typeof(McpServerToolAttribute));
            foreach (var hint in new[] { "ReadOnly", "Destructive", "Idempotent", "OpenWorld" }) Assert.Contains(declaration.NamedArguments, x => x.MemberName == hint);
            var annotation = m.GetCustomAttribute<McpServerToolAttribute>()!; Assert.False(annotation.OpenWorld); Assert.Equal(m.DeclaringType == typeof(DrawingQueryTools), annotation.ReadOnly);
        }
    }
    [Theory]
    [InlineData("get_drawing_info", "{max_items:0}")]
    [InlineData("capture_sheet", "{document:'D',sheet:'S',width:5000}")]
    [InlineData("capture_sheet", "{document:'D',sheet:'S',region_mm:[]}")]
    [InlineData("add_drawing_view", "{name:'v',kind:'projected',parent_view:'p',position_mm:[1,2],detail_region_mm:{}}")]
    [InlineData("add_section_view", "{name:'s',parent_view:'v',cut_line_mm:[[1,2],[3,4]],position_mm:[1,2],inherit_3d:[]}")]
    [InlineData("add_balloon", "{items:[{name:'B',view:'v',occurrence_path:'P',text:'1',position_mm:[1,2],intent:[]}]}")]
    [InlineData("add_drawing_view", "{name:'v',kind:'base',model:'m',position_mm:[1]}")]
    [InlineData("add_section_view", "{name:'s',parent_view:'v',cut_line_mm:[[1,2],[1,2]],position_mm:[1,2]}")]
    [InlineData("export_drawing", "{document:'D',format:'pdf',output_path:'x.pdf'}")]
    [InlineData("edit_drawing_view", "{document:'D',sheet:'S',view:'V'}")]
    [InlineData("add_drawing_dimension", "{items:[{name:'D',view:'V',kind:'diameter',intents:[{},{}],text_position_mm:[1,2]}]}")]
    [InlineData("add_drawing_dimension", "{items:[{name:'D',view:'V',kind:'chain',intents:[{},{},{}],text_positions_mm:[[1,2]]}]}")]
    public void Invalid_requests_fail_before_transport(string command, string input) => Assert.Throws<ArgumentException>(() => DrawingInput.Validate(command, JObject.Parse(input)));
    [Fact]
    public async Task Invalid_wrapper_request_does_not_attempt_a_host_connection()
    {
        var response = JObject.Parse(await new DrawingTools(_client).EditDrawingView("D", "S", "V")); Assert.False(response.Value<bool>("ok")); Assert.Equal("INVALID_ARGUMENT", (string?)response["error"]!["code"]);
    }
    [Fact]
    public void Signature_ignores_routing_order_and_null_fields_but_keeps_geometry()
    {
        var a = JObject.Parse("{name:'v',position_mm:[1,2],document:'D',timeout_ms:5000,optional:null}"); var b = JObject.Parse("{position_mm:[1,2],name:'v',document:'Other',sheet:'S'}"); Assert.Equal(DrawingInput.Signature(a), DrawingInput.Signature(b)); b["position_mm"] = new JArray(1, 3); Assert.NotEqual(DrawingInput.Signature(a), DrawingInput.Signature(b));
    }
    [Fact]
    public void Nonfinite_input_and_duplicate_batch_names_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => DrawingInput.Validate("add_drawing_view", new JObject { ["name"] = "v", ["kind"] = "base", ["model"] = "m", ["position_mm"] = new JArray(double.NaN, 2) }));
        Assert.Throws<ArgumentException>(() => DrawingInput.Validate("add_balloon", JObject.Parse("{items:[{name:'B',view:'v',occurrence_path:'P',text:'1',position_mm:[1,2]},{name:'B',view:'v',occurrence_path:'P',text:'2',position_mm:[1,2]}]}")));
    }
    [Fact]
    public void Oversized_completed_write_preserves_effects_and_never_becomes_retryable_query_failure()
    {
        var data = new JObject { ["ok"] = true, ["count"] = 1, ["created_count"] = 1, ["items"] = new JArray(new JObject { ["name"] = "length", ["attached"] = true, ["created"] = true, ["value"] = 100, ["text"] = new string('x', 1100000) }) };
        var result = AgentOutputGuardTests.Data(AgentOutputGuard.Apply("inventor_add_drawing_dimension", AgentOutputGuardTests.Result(data), new InventorMcpConfig()));
        Assert.True(result.Value<bool>("ok")); Assert.True(result.Value<bool>("response_compacted")); Assert.Equal(1, result.Value<int>("created_count")); Assert.Equal("length", (string?)result["items"]![0]!["name"]); Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.ToString(Formatting.None)) < ResponseSizeGuard.RejectBytes);
    }
    [Fact]
    public void Oversized_query_requests_a_real_narrowing_parameter()
    {
        var result = AgentOutputGuardTests.Data(AgentOutputGuard.Apply("inventor_get_drawing_info", AgentOutputGuardTests.Result(new JObject { ["notes"] = new string('x', 1100000) }), new InventorMcpConfig()));
        Assert.False(result.Value<bool>("ok")); Assert.Equal("RESPONSE_TOO_LARGE", (string?)result["error"]!["code"]); Assert.Contains("max_items", (string?)result["error"]!["message"]);
    }
    [Fact]
    public void Default_normalization_matches_explicit_defaults_without_mutating_input()
    {
        var input = JObject.Parse("{name:'S'}"); var a = DrawingInput.Normalize("add_sheet", input); var b = DrawingInput.Normalize("add_sheet", JObject.Parse("{name:'S',size:'A1',orientation:'landscape'}")); Assert.Equal(DrawingInput.Signature(a), DrawingInput.Signature(b)); Assert.Null(input["size"]);
    }
    [Fact]
    public void Spill_retention_is_forwarded_and_custom_cleanup_never_deletes_younger_files()
    {
        var config = InventorMcpConfig.Load(["--spill-retention-hours", "48"]); Assert.Equal(48, config.SpillRetentionHours);
        var writer = new Bimwright.Ipt.Shared.Infrastructure.ResponseSpillWriter(_dir, 48); var path = Path.Combine(_dir, "fresh.txt"); File.WriteAllText(path, "fixture"); File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(40)); Assert.Equal(0, writer.Cleanup(DateTime.UtcNow)); Assert.True(File.Exists(path)); Assert.Throws<ArgumentException>(() => InventorMcpConfig.Load(["--spill-retention-hours", "0"]));
    }
}
