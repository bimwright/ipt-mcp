using System.IO.Pipes;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Bake;
using Bimwright.Ipt.Server.Handlers;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.ToolBaker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ScriptBakeTests : IDisposable
{
    private const string Code = "foreach (JObject m in data) Tube((string)m[\"name\"]); return data.Count();";
    private readonly string _root = Directory.CreateTempSubdirectory("ipt-script-bake-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
    }

    private InventorMcpConfig Config(bool adaptive = true) => new()
    {
        BakeDirectory = Path.Combine(_root, "baked"), DescriptorDirectory = _root, EnableAdaptiveBake = adaptive, TimeoutMs = 5000,
    };

    private static JToken Data(int i) => JArray.Parse($"[{{\"name\":\"P{i:00}\"}}]");

    private BakeDb Db(InventorMcpConfig config)
    {
        var db = new BakeDb(BakePaths.Db(config));
        db.Migrate();
        return db;
    }

    [Fact]
    public void A_suggestion_opens_after_three_successful_runs_with_different_data()
    {
        var config = Config();
        Assert.Null(ScriptBake.Observe(config, Code, null, Data(1), ok: true, 10));
        Assert.Null(ScriptBake.Observe(config, Code, null, Data(1), ok: true, 10));  // same data: not a new input
        Assert.Null(ScriptBake.Observe(config, Code, null, Data(2), ok: false, 10)); // failed runs do not count
        Assert.Null(ScriptBake.Observe(config, Code, null, Data(3), ok: true, 10));
        var hint = ScriptBake.Observe(config, Code, null, Data(4), ok: true, 10);

        Assert.Equal("inventor_accept_bake_suggestion", (string?)hint?["tool"]);
        var suggestion = Assert.Single(Db(config).ListSuggestions());
        Assert.Equal(ScriptBake.Source, suggestion.Source);
        Assert.Equal(suggestion.Id, (string?)hint!["args_sketch"]?["suggestionId"]);
        Assert.DoesNotContain("Tube(", suggestion.PayloadJson); // only hashes are stored, never the code
    }

    [Fact]
    public void Nothing_is_recorded_when_adaptive_bake_is_off()
    {
        var config = Config(adaptive: false);
        for (var i = 0; i < 5; i++) Assert.Null(ScriptBake.Observe(config, Code, null, Data(i), ok: true, 10));
        var db = Db(config);
        Assert.Empty(db.ListSuggestions());
        Assert.Equal(0, db.CountSuccessfulRuns(ScriptBake.UsageTool, ScriptBake.Key(Code, null), DateTimeOffset.MinValue).Runs);
    }

    [Fact]
    public async Task Accepting_needs_the_same_code_and_then_points_send_code_at_the_tool()
    {
        var config = Config();
        for (var i = 0; i < 3; i++) ScriptBake.Observe(config, Code, ["pors_helpers"], Data(i), ok: true, 10);
        var db = Db(config);
        var id = Assert.Single(db.ListSuggestions()).Id;
        Task<JObject> Apply(JObject request)
        {
            var record = BakedToolRuntimeCommandFactory.FromApplyRequest(request); // the add-in's validation
            return Task.FromResult(new JObject { ["success"] = true, ["source_code"] = record.SourceCode, ["params_schema"] = record.ParamsSchema });
        }

        var missing = JObject.Parse(await AcceptBakeSuggestionHandler.HandleAsync(db, id, "pors_rail", pluginApply: Apply));
        Assert.Equal("code_required", (string?)missing["error_code"]);
        var other = JObject.Parse(await AcceptBakeSuggestionHandler.HandleAsync(db, id, "pors_rail", pluginApply: Apply, code: Code + " ", modules: ["pors_helpers"]));
        Assert.Equal("code_mismatch", (string?)other["error_code"]);
        var noModules = JObject.Parse(await AcceptBakeSuggestionHandler.HandleAsync(db, id, "pors_rail", pluginApply: Apply, code: Code));
        Assert.Equal("code_mismatch", (string?)noModules["error_code"]);

        var ok = JObject.Parse(await AcceptBakeSuggestionHandler.HandleAsync(db, id, "pors_rail", pluginApply: Apply, code: Code, modules: ["pors_helpers"]));
        Assert.True(ok.Value<bool>("ok"), ok.ToString());
        var tool = db.GetRegistryRecord("pors_rail")!;
        Assert.Equal(ScriptBake.Source, tool.Source);
        Assert.True(BakedToolRuntimeSource.TryReadScript(tool.SourceCode, out var code, out var modules, out _));
        Assert.Equal(Code, code);
        Assert.Equal(new[] { "pors_helpers" }, modules);
        Assert.Equal("{\"type\":\"array\"}", tool.ParamsSchema);

        var hint = ScriptBake.Observe(config, Code, ["pors_helpers"], Data(9), ok: true, 10);
        Assert.Equal("inventor_run_baked_tool", (string?)hint?["tool"]);
        Assert.Equal("pors_rail", (string?)hint!["args_sketch"]?["name"]);
    }

    [Fact]
    public void Script_records_are_policy_checked_like_send_code()
    {
        var record = new BakedToolRecord
        {
            Name = "bad_script", Source = "script", ParamsSchema = "{}",
            SourceCode = BakedToolRuntimeSource.BuildScript("System.IO.File.Delete(\"x\"); return 1;", [], "script:k"),
        };
        Assert.False(ToolCompiler.ValidateRecord(record).Ok);
        record.SourceCode = BakedToolRuntimeSource.BuildScript("return data.Count();", [], "script:k");
        Assert.True(ToolCompiler.ValidateRecord(record).Ok);
    }

    [Fact]
    public async Task A_script_tool_runs_as_send_code_with_params_as_data_and_obeys_the_send_code_gate()
    {
        var config = Config();
        var db = Db(config);
        Assert.True(db.TryInsertRegistryRecord(new BakedToolRecord
        {
            Name = "pors_rail", Description = "d", Source = "script", ParamsSchema = "{\"type\":\"array\"}", CompatMap = "{}",
            SourceCode = BakedToolRuntimeSource.BuildScript(Code, [], ScriptBake.Key(Code, null)),
            ReviewedByUser = true, CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        }));
        var modules = new CodeModuleStore(Path.Combine(_root, "modules"));

        var off = new InventorMcpConfig { BakeDirectory = config.BakeDirectory, DescriptorDirectory = _root, EnableSendCode = false };
        var refused = JObject.Parse(await new ToolBakerWriteTools(new PluginClient(off), off, modules, new ServerState(off)).RunBakedTool("pors_rail", "[]", default));
        Assert.Equal("SEND_CODE_DISABLED", (string?)refused["error"]?["code"]);

        var pipe = "ipt-script-bake-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        File.WriteAllText(Path.Combine(_root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = pipe,
            AuthToken = "test-token", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        var call = new ToolBakerWriteTools(new PluginClient(config), config, modules, new ServerState(config))
            .RunBakedTool("pors_rail", "[{\"name\":\"P07\"}]", timeout.Token);
        await connected;
        using var input = new StreamReader(server, leaveOpen: true);
        using var output = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var request = JObject.Parse((await input.ReadLineAsync(timeout.Token))!);
        Assert.Equal("send_code", (string?)request["command"]);
        Assert.Equal(Code, (string?)request["params"]?["code"]);
        Assert.Equal("P07", (string?)request["params"]?["data"]?[0]?["name"]);
        await output.WriteLineAsync(new JObject { ["id"] = request["id"], ["ok"] = true, ["data"] = JObject.Parse("{ok:true,result:1}") }.ToString(Formatting.None));
        var response = JObject.Parse(await call);
        Assert.Equal(1, (int?)response["result"]);
        Assert.Null(response["bake_hint"]); // running the tool does not suggest the tool
    }
}
