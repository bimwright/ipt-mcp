using System.IO.Pipes;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class SendCodeDataTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ipt-send-data-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static System.Text.Json.JsonElement Json(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Data_and_compile_only_reach_the_add_in_unchanged()
    {
        var pipe = "ipt-send-data-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        File.WriteAllText(Path.Combine(_root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = pipe,
            AuthToken = "test-token", Capabilities = DocumentTargetFixture.Capabilities, LastHeartbeatUtc = DateTimeOffset.UtcNow,
        }));
        var config = new InventorMcpConfig { DescriptorDirectory = _root, TimeoutMs = 5000 };
        var tools = new CodeTools(new PluginClient(config), new CodeModuleStore(Path.Combine(_root, "modules")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        const string data = "{\"members\":[{\"name\":\"P00\",\"a\":[0,0,175],\"b\":[0,0,1242.85]}],\"od\":48.3," +
                            "\"ts\":\"2026-10-26T10:00:00+07:00\",\"id\":9007199254740993,\"x\":1234567.123456789}";
        var call = tools.SendCode("return data[\"members\"].Count();", data: Json(data), compile_only: true, ct: timeout.Token);
        await connected;
        using var input = new StreamReader(server, leaveOpen: true);
        using var output = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var line = (await input.ReadLineAsync(timeout.Token))!;
        var request = JObject.Parse(line);
        Assert.Null(request["params"]?["data"]);
        Assert.Equal(data, (string?)request["params"]?["data_json"]);
        Assert.True(request["params"]!.Value<bool>("compile_only"));

        // The add-in reads the line as an envelope, then data_json with the prelude's reader settings.
        var env = InventorCommandEnvelope.Parse(line);
        var seen = WireJson.Load((string)env.Params["data_json"]!);
        Assert.Equal(JTokenType.String, seen["ts"]!.Type);
        Assert.Equal("2026-10-26T10:00:00+07:00", (string?)seen["ts"]);
        Assert.Equal(9007199254740993L, (long)seen["id"]!);
        Assert.Equal(1234567.123456789, (double)seen["x"]!);
        await output.WriteLineAsync(new JObject
        {
            ["id"] = request["id"], ["ok"] = true, ["data"] = JObject.Parse("{ok:true,compile_only:true,modules:[]}"),
        }.ToString(Formatting.None));
        var response = JObject.Parse(await call);
        Assert.True(response.Value<bool>("ok"));
        Assert.Null(response["size_hint"]);
    }

    [Fact]
    public async Task Omitted_data_and_compile_only_are_not_sent()
    {
        var tools = new CodeTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root }),
            new CodeModuleStore(Path.Combine(_root, "modules")));
        // No target: the call fails before the wire, but must not fail on argument handling.
        var response = JObject.Parse(await tools.SendCode("return 1;", data: Json("null")));
        Assert.Equal("NO_TARGET", (string?)response["error"]?["code"]);
    }

    [Theory]
    [InlineData("1e400", "not a finite double")]
    [InlineData("{\"a\":[1,-1e400]}", "a[1]")]
    [InlineData("NaN", "not a finite double")]
    [InlineData("{\"a\":1,\"a\":2}", "data is not valid JSON")]
    [InlineData("1 2", "Additional text")]
    [InlineData("{\"a\":", "data is not valid JSON")]
    public void Data_that_the_script_could_not_see_exactly_is_rejected(string json, string message)
    {
        Assert.False(JsonArg.TryData(json, out var data, out var error));
        Assert.Null(data);
        Assert.Contains(message, error);
    }

    [Fact]
    public void Data_depth_limit_matches_the_add_in_reader()
    {
        static string Nested(int depth) => new string('[', depth) + new string(']', depth);
        Assert.True(JsonArg.TryData(Nested(64), out _, out _));
        Assert.False(JsonArg.TryData(Nested(65), out _, out var error));
        Assert.Contains("MaxDepth", error);
    }

    [Theory]
    [InlineData("{ \"x\" : 1 }", "{\"x\":1}", true)]           // whitespace does not change what the script sees
    [InlineData("{\"x\":1.10}", "{\"x\":1.1}", true)]          // the same double
    [InlineData("{\"x\":1.0}", "{\"x\":1}", false)]            // Float vs Integer token in the script
    [InlineData("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}", false)] // property order is visible to the script
    [InlineData("{\"ts\":\"2026-10-26T10:00:00+07:00\"}", "{\"ts\":\"2026-10-26T03:00:00Z\"}", false)]
    public void Data_identity_is_the_data_json_text(string a, string b, bool same)
    {
        Assert.True(JsonArg.TryData(a, out var da, out _));
        Assert.True(JsonArg.TryData(b, out var db, out _));
        Assert.Equal(same, da!.ToString(Formatting.None) == db!.ToString(Formatting.None));
    }

    [Fact]
    public async Task Overflowing_data_is_rejected_before_contacting_Inventor()
    {
        var tools = new CodeTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root }),
            new CodeModuleStore(Path.Combine(_root, "modules")));
        var response = JObject.Parse(await tools.SendCode("return 1;", data: Json("{\"len\":1e400}")));
        Assert.Equal("INVALID_ARGUMENT", (string?)response["error"]?["code"]);
        Assert.Contains("'len'", (string?)response["error"]?["message"]);
    }

    [Fact]
    public void Iso_date_strings_stay_strings_for_every_tool()
    {
        const string ts = "2026-10-26T10:00:00.000+07:00";
        var line = JsonConvert.SerializeObject(new InventorCommandEnvelope
        {
            Command = "set_iproperty", Params = new JObject { ["value"] = ts },
        });
        Assert.Equal(JTokenType.Date, JsonConvert.DeserializeObject<InventorCommandEnvelope>(line)!.Params["value"]!.Type); // the old path
        var value = InventorCommandEnvelope.Parse(line).Params["value"]!;
        Assert.Equal(JTokenType.String, value.Type);
        Assert.Equal(ts, value.Value<string>());
        Assert.Equal(ts, (string?)JsonArg.From(Json($"{{\"v\":\"{ts}\"}}"))!["v"]);
        Assert.Throws<JsonReaderException>(() => WireJson.LoadObject("[]"));
    }

    [Fact]
    public async Task Oversized_data_is_rejected_before_contacting_Inventor()
    {
        var tools = new CodeTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root }),
            new CodeModuleStore(Path.Combine(_root, "modules")));
        var big = JsonConvert.SerializeObject(new { blob = new string('x', CodeTools.MaxDataBytes) });
        var response = JObject.Parse(await tools.SendCode("return 1;", data: Json(big)));
        Assert.Equal("INVALID_ARGUMENT", (string?)response["error"]?["code"]);
        Assert.Contains("data exceeds", (string?)response["error"]?["message"]);
    }

    [Theory]
    [InlineData(4000, false, false, null)]
    [InlineData(4001, false, false, "data and modules")]
    [InlineData(4001, true, false, "Use modules")]
    [InlineData(4001, false, true, "Use data")]
    [InlineData(4001, true, true, null)]
    public void Size_hint_names_what_the_large_script_is_missing(int length, bool hasData, bool hasModules, string? expected)
    {
        var hint = CodeTools.SizeHint(new string('x', length), hasData, hasModules);
        if (expected is null) Assert.Null(hint);
        else
        {
            Assert.Contains(expected, hint);
            Assert.Contains(length.ToString(), hint);
            Assert.Contains("compile_only=true", hint);
        }
    }

    [Fact]
    public async Task Large_inlined_script_gets_a_size_hint_even_on_errors()
    {
        var tools = new CodeTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root }),
            new CodeModuleStore(Path.Combine(_root, "modules")));
        var code = "return 1;" + new string(' ', CodeTools.SizeHintChars);
        var response = JObject.Parse(await tools.SendCode(code));
        Assert.Equal("NO_TARGET", (string?)response["error"]?["code"]);
        Assert.Contains("data and modules", (string?)response["size_hint"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Size_hint_stays_visible_when_the_final_MCP_response_spills(bool success)
    {
        var payload = new JObject { ["ok"] = success, ["result"] = new string('x', 10000), ["size_hint"] = "use data" };
        var config = InventorMcpConfig.Load(new[] { "--local-app-data", _root, "--output-budget-bytes", "4096",
            "--output-warning-bytes", "1024", "--output-strong-warning-bytes", "2048" });
        var guarded = AgentOutputGuard.Apply("inventor_send_code", AgentOutputGuardTests.Result(payload), config);
        var response = AgentOutputGuardTests.Data(guarded);
        Assert.Equal("use data", (string?)response["size_hint"]);
        Assert.True(AgentOutputGuard.Measure(guarded) <= config.OutputBudgetBytes);
    }
}
