using System.IO.Pipes;
using Bimwright.Ipt.Scripting;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Code;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

// result_contract=task_v1 + McpTaskResult (rvt-mcp parity).
public sealed class SendCodeTaskTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ipt-send-task-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static JObject Ok(JToken? result = null) => new() { ["ok"] = true, ["error"] = null, ["result"] = result };

    [Theory]
    [InlineData(null, "legacy")]
    [InlineData("\"legacy\"", "legacy")]
    [InlineData("\"task_v1\"", "task_v1")]
    public void Contract_is_read_from_the_wire(string? json, string expected)
    {
        Assert.True(SendCodeTask.TryReadContract(json is null ? null : JToken.Parse(json), out var contract, out _));
        Assert.Equal(expected, contract);
    }

    [Theory]
    [InlineData("\"task_v2\"")]
    [InlineData("\"TASK_V1\"")]
    [InlineData("1")]
    public void Unknown_contract_is_rejected(string json)
    {
        Assert.False(SendCodeTask.TryReadContract(JToken.Parse(json), out _, out var error));
        Assert.Contains("task_v1", error);
    }

    [Fact]
    public void Pass_reports_passed_and_returns_its_value()
    {
        var pass = McpTaskResult.Pass(new { within = true });
        Assert.Same(pass.Value, SendCodeTask.ResultValue("task_v1", pass));
        var data = Ok(JObject.FromObject(pass.Value!));
        SendCodeTask.Apply(data, "task_v1", completed: true, pass);
        Assert.True((bool)data["ok"]!);
        Assert.Equal("passed", (string?)data["_task"]?["status"]);
        Assert.Equal("task_v1", (string?)data["_task"]?["contract"]);
        Assert.Null(data["_task"]?["error"]);
    }

    [Fact]
    public void Fail_fails_the_call_and_returns_its_details()
    {
        var fail = McpTaskResult.Fail("OUT_OF_TOLERANCE", "Volume differs", new { actual = 2.0 });
        Assert.Same(fail.Details, SendCodeTask.ResultValue("task_v1", fail));
        var data = Ok();
        SendCodeTask.Apply(data, "task_v1", completed: true, fail);
        Assert.False((bool)data["ok"]!);
        Assert.Equal("OUT_OF_TOLERANCE: Volume differs", (string?)data["error"]);
        Assert.Equal("failed", (string?)data["_task"]?["status"]);
        Assert.Equal("OUT_OF_TOLERANCE", (string?)data["_task"]?["error"]?["code"]);
    }

    [Fact]
    public void Fail_without_a_code_uses_TASK_FAILED() =>
        Assert.Equal("TASK_FAILED", McpTaskResult.Fail(" ", "x").ErrorCode);

    [Fact]
    public void Fail_keeps_an_earlier_error_such_as_an_unserializable_result()
    {
        var data = Ok();
        data["ok"] = false;
        data["error"] = "result could not be serialized";
        SendCodeTask.Apply(data, "task_v1", completed: true, McpTaskResult.Fail("BAD", "bad"));
        Assert.Equal("result could not be serialized", (string?)data["error"]);
        Assert.Equal("failed", (string?)data["_task"]?["status"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    public void Task_v1_without_McpTaskResult_requires_one(object? returned)
    {
        var data = Ok();
        SendCodeTask.Apply(data, "task_v1", completed: true, returned);
        Assert.False((bool)data["ok"]!);
        Assert.StartsWith("TASK_RESULT_REQUIRED: ", (string?)data["error"]);
        Assert.Equal("not_reported", (string?)data["_task"]?["status"]);
    }

    [Fact]
    public void A_script_that_did_not_finish_reports_not_reported_and_keeps_its_error()
    {
        var data = new JObject { ["ok"] = false, ["error"] = "compile error: x" };
        SendCodeTask.Apply(data, "task_v1", completed: false, null);
        Assert.Equal("compile error: x", (string?)data["error"]);
        Assert.Equal("not_reported", (string?)data["_task"]?["status"]);
        Assert.Null(data["_task"]?["error"]);
    }

    [Fact]
    public void Legacy_is_unchanged_except_for_a_hint_on_a_failed_flag()
    {
        var plain = Ok(new JValue(1));
        SendCodeTask.Apply(plain, "legacy", completed: true, 1);
        Assert.Equal(Ok(new JValue(1)).ToString(), plain.ToString());

        var flagged = Ok(JObject.Parse("{\"passed\":false}"));
        SendCodeTask.Apply(flagged, "legacy", completed: true, null);
        Assert.True((bool)flagged["ok"]!);
        Assert.Null(flagged["_task"]);
        Assert.Contains("task_v1", (string?)flagged["task_contract_hint"]);
    }

    [Fact]
    public async Task Gateway_rejects_an_unknown_contract_before_contacting_Inventor()
    {
        var tools = new CodeTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root }),
            new CodeModuleStore(Path.Combine(_root, "modules")));
        var response = JObject.Parse(await tools.SendCode("return 1;", result_contract: "task_v2"));
        Assert.Equal("INVALID_ARGUMENT", (string?)response["error"]?["code"]);
        var context = JObject.Parse(await tools.SendCode("return 1;", usage_context: "production"));
        Assert.Equal("INVALID_ARGUMENT", (string?)context["error"]?["code"]);
    }

    [Theory]
    [InlineData("legacy", false, "unknown")]
    [InlineData("task_v1", true, "project")]
    [InlineData("legacy", false, "test")]
    public async Task Only_task_v1_is_sent_on_the_wire(string contract, bool sent, string usage)
    {
        var pipe = "ipt-send-task-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        File.WriteAllText(Path.Combine(_root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = pipe,
            AuthToken = "test-token", Capabilities = DocumentTargetFixture.Capabilities, LastHeartbeatUtc = DateTimeOffset.UtcNow,
        }));
        var tools = new CodeTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root, TimeoutMs = 5000 }).Confirmed(),
            new CodeModuleStore(Path.Combine(_root, "modules")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        var call = tools.SendCode("return McpTaskResult.Pass();", result_contract: contract, usage_context: usage, ct: timeout.Token);
        await connected;
        using var input = new StreamReader(server, leaveOpen: true);
        using var output = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var request = JObject.Parse((await input.ReadLineAsync(timeout.Token))!);
        Assert.Equal(sent, request["params"]?["result_contract"] is not null);
        if (sent) Assert.Equal("task_v1", (string?)request["params"]?["result_contract"]);
        // usage_context, like result_contract, is only on the wire when set (rvt-mcp parity).
        Assert.Equal(usage == "unknown" ? null : usage, (string?)request["params"]?["usage_context"]);
        await output.WriteLineAsync(new JObject { ["id"] = request["id"], ["ok"] = true, ["data"] = Ok() }.ToString(Formatting.None));
        await call;
    }
}
