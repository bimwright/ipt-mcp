using System.IO.Pipes;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class SendCodeRoutingTests : IDisposable
{
    private const string Survey = "foreach (ComponentOccurrence o in doc.ComponentDefinition.Occurrences) { var b = o.RangeBox; }";
    private readonly string _root = Directory.CreateTempSubdirectory("ipt-routing-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(Survey, "inventor_list_occurrences")]
    [InlineData("return doc.ComponentDefinition.Occurrences[1].Appearance.DisplayName;", "inventor_list_occurrences")]
    [InlineData("return doc.ComponentDefinition.AnalyzeInterference(items);", "inventor_check_interference")]
    [InlineData("ComponentOccurrence a = occs[1]; return app.MeasureTools.GetMinimumDistance(a, b);", "inventor_measure_min_distance")]
    [InlineData("return doc.File.ReferencedFileDescriptors;", "inventor_get_document_info")]
    [InlineData("foreach (var c in doc.ComponentDefinition.Constraints) { var h = c.HealthStatus; }", "inventor_list_constraints")]
    [InlineData("foreach (Document d in app.Documents) { var dirty = d.Dirty; }", "inventor_list_open_documents")]
    [InlineData("foreach (var d in doc.AllReferencedDocuments) { if (d.Dirty) d.Save2(); }", "inventor_save_all")]
    [InlineData("doc.Save2();", "inventor_save_document")]
    [InlineData("doc.SaveAs(path, false);", "inventor_save_document")]
    [InlineData("app.Documents.Open(path, false);", "inventor_open_document")]
    [InlineData("doc.RepresentationsManager.DesignViewRepresentations.Add(name);", "inventor_create_design_view")]
    [InlineData("foreach (ComponentOccurrence o in occs) o.Visible = false;", "inventor_set_occurrence_state")]
    [InlineData("foreach (ComponentOccurrence o in occs) o.Appearance = asset;", "inventor_set_appearance")]
    public void Recognized_workflows_get_one_argument_template(string code, string tool)
    {
        var hint = Assert.IsType<JObject>(SendCodeRouting.Match(code));
        Assert.Equal(tool, (string?)hint["tool"]);
        Assert.Equal(new[] { "tool", "args_sketch", "reason" }, hint.Properties().Select(p => p.Name));
        Assert.IsType<JObject>(hint["args_sketch"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)hint["reason"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("return 42;")]
    [InlineData("// doc.ComponentDefinition.Occurrences[1].RangeBox\nreturn 1;")]
    [InlineData("/* app.Documents.Open(path); */ return 1;")]
    [InlineData("return \"doc.ComponentDefinition.Occurrences[1].RangeBox\";")]
    [InlineData("return @\"app.Documents.Open(path);\";")]
    [InlineData("return \"\"\"app.Documents.Open(path);\"\"\";")]
    [InlineData("var x = doc.ComponentDefinition.Occurrences[1].RangeBox; doc.Update();")]
    [InlineData("var x = doc.ComponentDefinition.Occurrences[1].RangeBox; occs[1].Delete();")]
    [InlineData("var x = doc.ComponentDefinition.Occurrences[1].RangeBox; doc.DisplayName = name;")]
    [InlineData("var x = doc.ComponentDefinition.Occurrences[1].RangeBox; doc.ComponentDefinition.Occurrences.Add(path, m);")]
    [InlineData("return app.MeasureTools.GetMinimumDistance(pointA, pointB);")]
    [InlineData("doc.SaveAs(path, true);")]
    [InlineData("var x = doc.ComponentDefinition.Occurrences[1].RangeBox; doc.Counter++;")]
    [InlineData("var x = doc.ComponentDefinition.Occurrences[1].RangeBox; ++doc.Counter;")]
    public void Unrelated_text_and_mutating_surveys_get_no_query_hint(string code)
        => Assert.Null(SendCodeRouting.Match(code));

    [Fact]
    public void Interpolation_expressions_remain_code_and_member_comparisons_are_not_writes()
    {
        Assert.NotNull(SendCodeRouting.Match("return $\"{doc.ComponentDefinition.Occurrences[1].RangeBox}\";"));
        Assert.NotNull(SendCodeRouting.Match(Survey + "return doc.DisplayName == name;"));
        Assert.NotNull(SendCodeRouting.Match(Survey + "return doc.DisplayName+\"=\"+doc.Dirty+\";\"+doc.DisplayName;"));
    }

    [Fact]
    public void Modules_and_unavailable_tools_suppress_hints_and_argument_templates_are_independent()
    {
        Assert.Null(SendCodeRouting.Match(Survey, usesModules: true));
        Assert.Null(SendCodeRouting.Match(Survey, availableTools: new HashSet<string>()));
        SendCodeRouting.Match(Survey)!["args_sketch"]!["fields"] = new JArray("changed");
        Assert.Contains("bbox_mm", SendCodeRouting.Match(Survey)!["args_sketch"]!["fields"]!.Values<string>());
    }

    [Fact]
    public void Multiple_workflow_steps_still_return_only_the_first_available_hint()
    {
        const string code = "doc.RepresentationsManager.DesignViewRepresentations.Add(name); " +
            "foreach (ComponentOccurrence o in occs) o.Visible = false; doc.Save2();";
        Assert.Equal("inventor_create_design_view", (string?)SendCodeRouting.Match(code)?["tool"]);
        Assert.Equal("inventor_set_occurrence_state", (string?)SendCodeRouting.Match(code,
            availableTools: new HashSet<string> { "inventor_set_occurrence_state", "inventor_save_document" })?["tool"]);
    }

    [Fact]
    public void Every_embedded_rule_targets_a_registered_tool_and_valid_argument_names()
    {
        using var stream = typeof(CodeTools).Assembly.GetManifestResourceStream("Bimwright.Ipt.SendCodeRouting.json")!;
        using var reader = new StreamReader(stream);
        var rules = (JArray)JObject.Parse(reader.ReadToEnd())["rules"]!;
        Assert.Equal(13, rules.Count);
        Assert.Equal(rules.Count, rules.Select(r => (string?)r["id"]).Distinct().Count());
        var methods = Program.ResolveToolTypesForRegistration(new InventorMcpConfig()).SelectMany(t => t.GetMethods());
        foreach (var rule in rules)
        {
            var method = Assert.Single(methods, m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name == (string?)rule["tool"]);
            foreach (var arg in ((JObject)rule["args_sketch"]!).Properties())
                Assert.Contains(method.GetParameters(), p => p.Name == arg.Name);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("script-error")]
    [InlineData("runtime-error")]
    [InlineData("gateway-error")]
    [InlineData("module")]
    [InlineData("filtered")]
    public async Task Routing_preserves_wire_request_result_diagnostics_and_gateway_receipts(string scenario)
    {
        var pipe = "ipt-routing-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        File.WriteAllText(Path.Combine(_root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = pipe,
            AuthToken = "test-token", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        }));
        var config = new InventorMcpConfig { DescriptorDirectory = _root, TimeoutMs = 5000 };
        if (scenario == "filtered") config.Toolsets.Add("code");
        var store = new CodeModuleStore(Path.Combine(_root, "modules"));
        if (scenario == "module") store.Save("helpers", "static int F() => 42;", null, Array.Empty<string>(), new JArray());
        var tools = new CodeTools(new PluginClient(config), store, new ServerState(config));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        var call = tools.SendCode(Survey, scenario == "module" ? new[] { "helpers" } : null,
            timeout_ms: 12345, silent: true, ct: timeout.Token);
        await connected;
        using var input = new StreamReader(server, leaveOpen: true);
        using var output = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var request = JObject.Parse((await input.ReadLineAsync(timeout.Token))!);
        Assert.Equal("send_code", (string?)request["command"]);
        Assert.Equal(Survey, (string?)request["params"]?["code"]);
        Assert.True(request["params"]!.Value<bool>("silent"));
        Assert.Equal(scenario == "module", request["params"]?["modules"] is JArray);
        var data = scenario switch
        {
            "script-error" => JObject.Parse("{ok:false,error:'compile error',diagnostics:[{source:'script',line:1,code:'CS1002',hint:'keep repair hint'}],hint:'keep runtime hint'}"),
            "runtime-error" => JObject.Parse("{ok:false,error:'runtime error',location:{source:'script',line:2,text:'throw new Exception();'},hint:'keep repair hint',warnings:['kept']}"),
            "gateway-error" => JObject.Parse("{mutation_applied:true,outcome:'committed',receipt:{id:42}}"),
            _ => JObject.Parse("{ok:true,result:{rows:[1,2]},stdout:'kept',result_file:'C:/temp/kept.json'}"),
        };
        var gatewayError = scenario == "gateway-error";
        await output.WriteLineAsync(new JObject
        {
            ["id"] = request["id"], ["ok"] = !gatewayError, ["data"] = data,
            ["error"] = gatewayError ? new JObject { ["code"] = "API_ERROR", ["message"] = "readback failed" } : null,
        }.ToString(Formatting.None));
        var response = JObject.Parse(await call);
        var hint = response["routing_hint"];
        Assert.Equal(scenario is not ("module" or "filtered"), hint is JObject);
        response.Remove("routing_hint");
        if (gatewayError)
        {
            Assert.False(response.Value<bool>("ok"));
            Assert.Equal("API_ERROR", (string?)response["error"]?["code"]);
            Assert.True(JToken.DeepEquals(data, response["data"]));
        }
        else Assert.True(JToken.DeepEquals(data, response));
    }

    [Fact]
    public async Task No_target_error_also_receives_an_advisory_hint()
    {
        var tools = new CodeTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root }),
            new CodeModuleStore(Path.Combine(_root, "modules")));
        var response = JObject.Parse(await tools.SendCode(Survey));
        Assert.Equal("NO_TARGET", (string?)response["error"]?["code"]);
        Assert.Equal("inventor_list_occurrences", (string?)response["routing_hint"]?["tool"]);
        var moduleError = JObject.Parse(await tools.SendCode(Survey, new[] { "missing" }));
        Assert.Null(moduleError["routing_hint"]);
        Assert.Equal("INVALID_ARGUMENT", (string?)moduleError["error"]?["code"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Routing_hint_stays_visible_when_the_final_MCP_response_spills(bool success)
    {
        var hint = SendCodeRouting.Match(Survey)!;
        var payload = new JObject { ["ok"] = success, ["result"] = new string('x', 10000), ["routing_hint"] = hint };
        var config = InventorMcpConfig.Load(new[] { "--local-app-data", _root, "--output-budget-bytes", "4096",
            "--output-warning-bytes", "1024", "--output-strong-warning-bytes", "2048" });
        var guarded = AgentOutputGuard.Apply("inventor_send_code", AgentOutputGuardTests.Result(payload), config);
        var response = AgentOutputGuardTests.Data(guarded);
        Assert.Equal(!success, guarded.IsError);
        Assert.True(JToken.DeepEquals(hint, response["routing_hint"]));
        Assert.True(response.Value<bool>("readback_required"));
        Assert.True(AgentOutputGuard.Measure(guarded) <= config.OutputBudgetBytes);
        var spilled = JObject.Parse(File.ReadAllText((string)response["response_file"]!));
        var original = JObject.Parse((string)spilled["content"]![0]!["text"]!);
        Assert.True(JToken.DeepEquals(payload, original));
    }
}
