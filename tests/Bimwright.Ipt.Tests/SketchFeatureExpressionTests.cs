using System.IO.Pipes;
using System.Text.Json;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Feature;
using Bimwright.Ipt.Shared.Handlers.Sketch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Expression-driven <c>add_sketch_dimension</c> value and <c>revolve</c> angle: the API-agnostic
/// parameter rules, and that the server tools forward numbers and expression strings unchanged.
/// </summary>
public sealed class SketchFeatureExpressionTests : IDisposable
{
    // ---- SketchDimensionParams --------------------------------------------------------------

    [Fact]
    public void Dimension_number_is_mm()
    {
        Assert.True(SketchDimensionParams.TryParseValue(new JValue(12.5), out var mm, out var expr, out _));
        Assert.Equal(12.5, mm);
        Assert.Null(expr);
    }

    [Fact]
    public void Dimension_expression_passed_verbatim()
    {
        Assert.True(SketchDimensionParams.TryParseValue(new JValue("Shaft_D / 2"), out var mm, out var expr, out _));
        Assert.Null(mm);
        Assert.Equal("Shaft_D / 2", expr);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void Dimension_non_positive_number_rejected(double v)
    {
        Assert.False(SketchDimensionParams.TryParseValue(new JValue(v), out _, out _, out var err));
        Assert.Equal("value_mm must be greater than 0", err);
    }

    [Fact]
    public void Dimension_missing_keeps_existing_message()
    {
        Assert.False(SketchDimensionParams.TryParseValue(null, out _, out _, out var err));
        Assert.Equal("entity_id and value_mm are required", err);
    }

    // ---- RevolveParams ----------------------------------------------------------------------

    [Theory]
    [InlineData(90.0)]
    [InlineData(0.0)]     // existing behaviour: 0 means a full revolve
    [InlineData(-45.0)]   // existing behaviour: negative means the negative direction
    [InlineData(360.0)]
    public void Revolve_numeric_angles_keep_existing_semantics(double v)
    {
        Assert.True(RevolveParams.TryParseAngle(new JValue(v), out var deg, out var expr, out _));
        Assert.Equal(v, deg);
        Assert.Null(expr);
    }

    [Fact]
    public void Revolve_expression_passed_verbatim()
    {
        Assert.True(RevolveParams.TryParseAngle(new JValue("sweep_angle"), out var deg, out var expr, out _));
        Assert.Null(deg);
        Assert.Equal("sweep_angle", expr);
    }

    [Fact]
    public void Revolve_missing_keeps_existing_message()
    {
        Assert.False(RevolveParams.TryParseAngle(null, out _, out _, out var err));
        Assert.Equal("sketch_name, axis_id and angle_deg are required", err);
    }

    [Fact]
    public void Revolve_wrong_type_names_degrees()
    {
        Assert.False(RevolveParams.TryParseAngle(new JValue(true), out _, out _, out var err));
        Assert.Equal("angle_deg must be a number (degrees) or an expression string", err);
    }

    // ---- server tools forward value/angle unchanged ------------------------------------------

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-wire-" + Guid.NewGuid().ToString("N"));
    private readonly string _pipeName = "BimwrightIptTests-" + Guid.NewGuid().ToString("N");
    private readonly PluginClient _client;

    public SketchFeatureExpressionTests()
    {
        Directory.CreateDirectory(_dir);
        var descriptor = new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId,
            InventorYear = 2027,
            ProcessId = Environment.ProcessId,
            HostApp = "Inventor",
            Transport = "pipe",
            PipeName = _pipeName,
            AuthToken = "test-token",
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(Path.Combine(_dir, "target.json"), JsonConvert.SerializeObject(descriptor));
        _client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir, TimeoutMs = 5000 });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Fact]
    public async Task AddSketchDimension_forwards_number_as_number()
    {
        var p = await ParamsOf("add_sketch_dimension", () => new SketchTools(_client).AddSketchDimension("3", Json("12.5")));
        Assert.Equal("3", (string?)p["entity_id"]);
        Assert.Equal(JTokenType.Float, p["value_mm"]!.Type);
        Assert.Equal(12.5, (double)p["value_mm"]!);
    }

    [Fact]
    public async Task AddSketchDimension_forwards_expression_as_string()
    {
        var p = await ParamsOf("add_sketch_dimension", () => new SketchTools(_client).AddSketchDimension("3", Json("\"Shaft_D / 2\"")));
        Assert.Equal(JTokenType.String, p["value_mm"]!.Type);
        Assert.Equal("Shaft_D / 2", (string?)p["value_mm"]);
    }

    [Fact]
    public async Task Revolve_forwards_number_and_expression()
    {
        var tools = new FeatureTools(_client);
        var num = await ParamsOf("revolve", () => tools.Revolve("Sketch1", "XAxis", Json("90")));
        Assert.Equal(90.0, (double)num["angle_deg"]!);

        var expr = await ParamsOf("revolve", () => tools.Revolve("Sketch1", "XAxis", Json("\"sweep_angle\"")));
        Assert.Equal("sweep_angle", (string?)expr["angle_deg"]);
        Assert.Equal("join", (string?)expr["operation"]);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("\"HoleX / 2\"")]
    public async Task Point_dimension_forwards_raw_value_and_explicit_target(string raw)
    {
        var p = await ParamsOf("add_sketch_point_dimension", () => new SketchTools(_client).AddSketchPointDimension("first", "second", "vertical", Json(raw), "fixture.ipt", new[] { 10.0, 20.0 }));
        Assert.Equal("first", (string?)p["point1_ref"]); Assert.Equal("second", (string?)p["point2_ref"]);
        Assert.Equal("vertical", (string?)p["orientation"]); Assert.Equal("fixture.ipt", (string?)p["document"]);
        Assert.True(JToken.DeepEquals(JToken.Parse(raw), p["value"])); Assert.Equal(20, (double)p["text_position"]![1]!);
    }

    [Fact]
    public async Task Point_inventory_forwards_read_scope_and_limit()
    {
        var p = await ParamsOf("list_sketch_points", () => new QueryTools(_client).ListSketchPoints("Holes", "fixture.ipt", 50));
        Assert.Equal("Holes", (string?)p["sketch_name"]); Assert.Equal("fixture.ipt", (string?)p["document"]); Assert.Equal(50, (int)p["max_items"]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Point_dimension_preserves_failure_recovery_receipt(bool rollbackFailed)
    {
        var data = new JObject { ["rolled_back"] = !rollbackFailed, ["mutation_applied"] = rollbackFailed ? new JValue("unknown") : new JValue(false), ["rollback_failed"] = rollbackFailed };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var receive = ReplyWithFailureAsync(server, data, timeout.Token);
        var result = JObject.Parse(await new SketchTools(_client).AddSketchPointDimension("a", "b", "aligned", Json("10"), ct: timeout.Token));
        Assert.Equal("add_sketch_point_dimension", await receive); Assert.False(result.Value<bool>("ok")); Assert.True(JToken.DeepEquals(data, result["data"]));
    }

    private async Task<JObject> ParamsOf(string command, Func<Task<string>> invoke)
    {
        using var server = new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var receive = ReceiveOnceAsync(server);
        var invokeTask = invoke();
        var envelope = await receive;
        await invokeTask;
        Assert.Equal(command, (string?)envelope["command"]);
        return Assert.IsType<JObject>(envelope["params"]);
    }

    [Theory]
    [InlineData("add_sketch_dimension", false)]
    [InlineData("add_sketch_dimension", true)]
    [InlineData("revolve", false)]
    [InlineData("revolve", true)]
    [InlineData("batch_execute", false)]
    [InlineData("batch_execute", true)]
    [InlineData("sheet_fabrication_set_thickness", false)]
    [InlineData("sheet_fabrication_set_thickness", true)]
    public async Task Mutation_failures_preserve_rollback_receipts(string command, bool rollbackFailed)
    {
        var data = new JObject
        {
            ["rolled_back"] = !rollbackFailed,
            ["mutation_applied"] = rollbackFailed ? new JValue("unknown") : new JValue(false),
            ["rollback_failed"] = rollbackFailed,
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var receive = ReplyWithFailureAsync(server, data, timeout.Token);
        var invoke = command switch
        {
            "add_sketch_dimension" => new SketchTools(_client).AddSketchDimension("3", Json("\"Shaft_D / 2\""), timeout.Token),
            "revolve" => new FeatureTools(_client).Revolve("Sketch1", "XAxis", Json("\"sweep_angle\""), ct: timeout.Token),
            "batch_execute" => new FeatureTools(_client).BatchExecute(Json("[{\"command\":\"get_document_info\"}]"), ct: timeout.Token),
            _ => new SheetFabricationTools(_client).SetSheetFabricationThickness(Json("2"), ct: timeout.Token),
        };
        var result = JObject.Parse(await invoke);
        Assert.Equal(command, await receive);
        Assert.False(result.Value<bool>("ok"));
        Assert.Equal("API_ERROR", (string?)result["error"]?["code"]);
        Assert.True(JToken.DeepEquals(data, result["data"]), result.ToString());
    }

    private static async Task<string> ReplyWithFailureAsync(NamedPipeServerStream server, JObject data, CancellationToken ct)
    {
        await server.WaitForConnectionAsync(ct);
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var envelope = JObject.Parse((await reader.ReadLineAsync(ct))!);
        await writer.WriteLineAsync(new JObject
        {
            ["id"] = envelope["id"], ["ok"] = false,
            ["error"] = new JObject { ["code"] = "API_ERROR", ["message"] = "Mutation failed" },
            ["data"] = data, ["meta"] = new JObject(),
        }.ToString(Formatting.None));
        return envelope.Value<string>("command")!;
    }

    private static async Task<JObject> ReceiveOnceAsync(NamedPipeServerStream server)
    {
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var envelope = JObject.Parse((await reader.ReadLineAsync())!);
        await writer.WriteLineAsync(new JObject
        {
            ["id"] = envelope["id"],
            ["ok"] = true,
            ["data"] = new JObject { ["accepted"] = true },
            ["meta"] = new JObject(),
        }.ToString(Formatting.None));
        return envelope;
    }
}
