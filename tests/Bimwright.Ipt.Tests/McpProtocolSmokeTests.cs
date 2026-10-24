using System.Diagnostics;
using Bimwright.Ipt.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class McpProtocolSmokeTests
{
    [Fact]
    public async Task Send_code_publishes_task_guide_and_returns_routing_on_failure_over_MCP()
    {
        var responses = await RunProtocolHandshake(expressionTool: "inventor_send_code",
            expressionArguments: new JObject { ["code"] = "return doc.ComponentDefinition.Occurrences[1].RangeBox;" });
        var tool = Assert.Single((JArray)responses[1]["result"]!["tools"]!, t => (string?)t["name"] == "inventor_send_code");
        Assert.Contains(SendCodeTaskGuide.Text, (string?)tool["description"]);
        Assert.Contains(SendCodeTaskGuide.Text, (string?)responses[0]["result"]?["instructions"]);
        var body = JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);
        Assert.False(body.Value<bool>("ok"));
        Assert.Equal("NO_TARGET", (string?)body["error"]?["code"]);
        Assert.Equal("inventor_list_occurrences", (string?)body["routing_hint"]?["tool"]);
    }

    [Theory]
    [InlineData("inventor_get_ipart_info", "{max_rows:10,row_offset:0}", true)]
    [InlineData("inventor_create_ipart_factory", "{output_path:'C:/copy.ipt',base_member:'A',columns:[{name:'L',kind:'parameter',target:'Length'}]}", false)]
    [InlineData("inventor_update_ipart_table", "{rows:[{member:'A',values:{L:20}}]}", false)]
    [InlineData("inventor_set_ipart_row", "{member:'A'}", false)]
    [InlineData("inventor_generate_ipart_members", "{members:['A'],dry_run:true}", false)]
    [InlineData("inventor_sync_ipart_excel", "{path:'C:/catalog.xlsx',sheet:'Shafts',mapping:{member:'Code',part_number:'PN',L:'Length'}}", false)]
    [InlineData("inventor_place_ipart_member", "{factory_document:'copy.ipt',member:'A',position_mm:[10,20,30]}", false)]
    [InlineData("inventor_change_ipart_member", "{occurrence:'A:1',member:'B'}", false)]
    public async Task Standard_ipart_tools_bind_over_MCP_and_filter_writes(string name,string input,bool readOnly)
    {
        var responses=await RunProtocolHandshake(readOnly:readOnly,expressionTool:name,expressionArguments:JObject.Parse(input));
        var catalog=(JArray)responses[1]["result"]!["tools"]!;
        Assert.Equal(readOnly,Assert.Single(catalog,t=>(string?)t["name"]==name)["annotations"]!.Value<bool>("readOnlyHint"));
        if(readOnly)Assert.Single(catalog.Where(t=>((string?)t["name"])?.Contains("ipart")==true));
        var result=JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);Assert.Equal("NO_TARGET",(string?)result["error"]?["code"]);
    }

    [Theory]
    [InlineData(false, 146)]
    [InlineData(true, 38)]
    public async Task Combined_excel_and_point_dimension_catalog_preserves_both_surfaces(bool readOnly, int count)
    {
        var responses = await RunProtocolHandshake(readOnly: readOnly);
        var tools = (JArray)responses[1]["result"]!["tools"]!;
        Assert.Equal(count, tools.Count);
        foreach (var name in new[] { "inventor_list_excel_tables", "inventor_list_sketch_points" })
            Assert.True(Assert.Single(tools, t => (string?)t["name"] == name)["annotations"]!.Value<bool>("readOnlyHint"));
        foreach (var name in new[] { "inventor_link_excel_table", "inventor_reload_excel_table", "inventor_add_sketch_point_dimension" })
        {
            if (readOnly) Assert.DoesNotContain(tools, t => (string?)t["name"] == name);
            else Assert.False(Assert.Single(tools, t => (string?)t["name"] == name)["annotations"]!.Value<bool>("readOnlyHint"));
        }
    }

    [Theory]
    [InlineData("12.5")]
    [InlineData("\"HoleX / 2\"")]
    public async Task Point_dimension_binds_over_MCP_and_publishes_complete_contract(string value)
    {
        var args = new JObject { ["point1_ref"] = "first", ["point2_ref"] = "second", ["orientation"] = "horizontal", ["value"] = JToken.Parse(value), ["text_position"] = new JArray(10, 20) };
        var responses = await RunProtocolHandshake(expressionTool: "inventor_add_sketch_point_dimension", expressionArguments: args);
        var tool = ((JArray)responses[1]["result"]!["tools"]!).Single(t => (string?)t["name"] == "inventor_add_sketch_point_dimension");
        Assert.Equal(new[] { "orientation", "point1_ref", "point2_ref", "value" }, tool["inputSchema"]!["required"]!.Values<string>().OrderBy(x => x).ToArray());
        Assert.IsType<JObject>(tool["inputSchema"]!["properties"]!["value"]);
        Assert.False(tool["annotations"]!.Value<bool>("readOnlyHint"));
        var reply = JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);
        Assert.Equal("NO_TARGET", (string?)reply["error"]?["code"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Point_inventory_is_callable_in_read_only_mode(bool readOnly)
    {
        var responses = await RunProtocolHandshake(readOnly: readOnly, expressionTool: "inventor_list_sketch_points", expressionArguments: new JObject { ["sketch_name"] = "Holes", ["max_items"] = 20 });
        var tools = (JArray)responses[1]["result"]!["tools"]!;
        var query = tools.Single(t => (string?)t["name"] == "inventor_list_sketch_points");
        Assert.True(query["annotations"]!.Value<bool>("readOnlyHint"));
        if (readOnly) Assert.DoesNotContain(tools, t => (string?)t["name"] == "inventor_add_sketch_point_dimension");
        var reply = JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);
        Assert.Equal("NO_TARGET", (string?)reply["error"]?["code"]);
    }

    [Theory]
    [InlineData("inventor_list_excel_tables", "{}", true)]
    [InlineData("inventor_link_excel_table", "{path:'C:/fixture.xlsx',start_cell:'A6'}", false)]
    [InlineData("inventor_reload_excel_table", "{table_id:'excel:fixture'}", false)]
    public async Task Excel_tools_bind_over_MCP_and_obey_read_only_registration(string tool, string input, bool readOnly)
    {
        var responses = await RunProtocolHandshake(readOnly: readOnly, expressionTool: tool, expressionArguments: JObject.Parse(input));
        var tools = (JArray)responses[1]["result"]!["tools"]!;
        var published = Assert.Single(tools, item => (string?)item["name"] == tool);
        Assert.Equal(readOnly, (bool)published["annotations"]!["readOnlyHint"]!);
        if (readOnly)
        {
            Assert.DoesNotContain(tools, item => (string?)item["name"] == "inventor_link_excel_table");
            Assert.DoesNotContain(tools, item => (string?)item["name"] == "inventor_reload_excel_table");
        }
        var response = JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);
        Assert.Equal("NO_TARGET", (string?)response["error"]?["code"]);
    }

    [Theory]
    [InlineData("inventor_add_sketch_dimension", "value", "12.5")]
    [InlineData("inventor_add_sketch_dimension", "value", "\"driver / 2\"")]
    [InlineData("inventor_revolve", "angle", "-90")]
    [InlineData("inventor_revolve", "angle", "\"sweep_angle\"")]
    public async Task Expression_inputs_bind_over_MCP_with_B_compatible_schemas(string tool, string field, string value)
    {
        var args = tool == "inventor_revolve"
            ? new JObject { ["sketchName"] = "profile", ["axisId"] = "YAxis" }
            : new JObject { ["entityId"] = "1" };
        args[field] = JToken.Parse(value);
        var responses = await RunProtocolHandshake(expressionTool: tool, expressionArguments: args);
        var tools = (JArray)responses[1]["result"]!["tools"]!;
        var published = Assert.Single(tools, item => (string?)item["name"] == tool);
        Assert.IsType<JObject>(published["inputSchema"]!["properties"]![field]);
        var response = JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);
        Assert.Equal("NO_TARGET", (string?)response["error"]?["code"]);
    }

    [Fact]
    public async Task Server_exposes_tools_list_over_stdio()
    {
        var responses = await RunProtocolHandshake();

        var initialize = Assert.Single(responses, r => (int?)r["id"] == 1);
        Assert.NotNull(initialize["result"]?["capabilities"]?["tools"]);

        var toolsList = Assert.Single(responses, r => (int?)r["id"] == 2);
        var tools = Assert.IsAssignableFrom<JArray>(toolsList["result"]?["tools"]);
        Assert.NotEmpty(tools);
        Assert.Contains(tools, t => (string?)t["name"] == "inventor_list_available_targets");
        var report = Assert.Single(tools, t => (string?)t["name"] == "inventor_report_task_result");
        Assert.Equal(new[] { "outcome", "summary", "task_id" },
            report["inputSchema"]!["required"]!.Values<string>().OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ILogic_scalar_arguments_bind_through_the_real_MCP_protocol()
    {
        var responses = await RunProtocolHandshake(checkILogicArguments: true);
        var tools = (JArray)responses[1]["result"]!["tools"]!;
        var rule = Assert.Single(tools, tool => (string?)tool["name"] == "inventor_ilogic_run_rule");
        var schema = rule["inputSchema"]!["properties"]!["arguments"]!;
        Assert.Contains("object", schema["type"]!.Values<string>());
        Assert.Contains("number", schema["additionalProperties"]!["type"]!.Values<string>());
        var response = JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);
        Assert.False((bool)response["ok"]!);
        Assert.Equal("NO_TARGET", (string?)response["error"]!["code"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Health_schema_and_offline_identity_are_available_over_MCP_including_read_only(
        bool readOnly, bool explicitArgument)
    {
        var responses = await RunProtocolHandshake(checkHealth: true, readOnly: readOnly, explicitHealthArgument: explicitArgument);
        var tools = (JArray)responses[1]["result"]!["tools"]!;
        var health = Assert.Single(tools, tool => (string?)tool["name"] == "inventor_health");
        Assert.Equal("boolean", (string?)health["inputSchema"]?["properties"]?["check_updates"]?["type"]);
        Assert.DoesNotContain("check_updates", health["inputSchema"]?["required"]?.Values<string>() ?? Array.Empty<string>());
        Assert.True(health["annotations"]!.Value<bool>("readOnlyHint"));
        Assert.False(health["annotations"]!.Value<bool>("destructiveHint"));
        Assert.True(health["annotations"]!.Value<bool>("idempotentHint"));
        Assert.True(health["annotations"]!.Value<bool>("openWorldHint"));
        Assert.Contains("inventor_health(check_updates=true)", (string?)health["description"]);
        Assert.Contains("update.notification", (string?)health["description"]);
        Assert.Contains("bundle_update_proposal_allowed", (string?)health["description"]);
        Assert.Contains("protected_components", (string?)health["description"]);
        Assert.Contains("update_guidance.mode", (string?)health["description"]);
        Assert.Contains("unobserved_components", (string?)health["description"]);
        Assert.Contains("inventor_health(check_updates=true)", (string?)responses[0]["result"]?["instructions"]);
        Assert.Contains("update.notification", (string?)responses[0]["result"]?["instructions"]);
        Assert.Contains("bundle_update_proposal_allowed", (string?)responses[0]["result"]?["instructions"]);
        Assert.Contains("protected_components", (string?)responses[0]["result"]?["instructions"]);
        var result = JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);
        Assert.False(result.Value<bool>("ok"));
        Assert.False(result.Value<bool>("host_connected"));
        Assert.Equal("NO_TARGET", (string?)result["error"]?["code"]);
        Assert.Equal((string?)responses[0]["result"]?["serverInfo"]?["version"], (string?)result["mcp"]?["server_informational_version"]);
        Assert.Equal(Program.ServerInformationalVersion, (string?)responses[0]["result"]?["serverInfo"]?["version"]);
        Assert.True(JToken.DeepEquals(health["_meta"]?[HealthRuntimeInfo.ClientRuntimeMetadataKey],
            result["client_verification"]?["expected_runtime"]));
        Assert.Equal("not_observable_by_server", (string?)result["client_verification"]?["status"]);
        Assert.NotEmpty(result["mcp"]!["server_build_id"]!.Value<string>()!);
        Assert.Equal("not_checked", (string?)result["update"]?["status"]);
        Assert.Equal(JTokenType.Null, result["update"]!["notification"]!.Type);
        Assert.Equal("check_only", (string?)result["update_guidance"]?["mode"]);
        Assert.DoesNotContain("install.ps1", result["update_guidance"]!["steps"]!.ToString());
    }

    [Fact]
    public async Task Reconnected_client_can_detect_a_catalog_cached_from_the_previous_process_with_the_same_version()
    {
        var previous = await RunProtocolHandshake(checkHealth: true);
        var current = await RunProtocolHandshake(checkHealth: true);
        var oldHealthTool = ((JArray)previous[1]["result"]!["tools"]!).Single(t => (string?)t["name"] == "inventor_health");
        var newHealthTool = ((JArray)current[1]["result"]!["tools"]!).Single(t => (string?)t["name"] == "inventor_health");
        var health = JObject.Parse((string)current[2]["result"]!["content"]![0]!["text"]!);
        var expected = health["client_verification"]!["expected_runtime"]!;
        Assert.Equal((string?)previous[0]["result"]?["serverInfo"]?["version"], (string?)current[0]["result"]?["serverInfo"]?["version"]);
        Assert.False(JToken.DeepEquals(oldHealthTool["_meta"]?[HealthRuntimeInfo.ClientRuntimeMetadataKey], expected));
        Assert.True(JToken.DeepEquals(newHealthTool["_meta"]?[HealthRuntimeInfo.ClientRuntimeMetadataKey], expected));
    }

    private sealed class Profile : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ipt-protocol-" + Guid.NewGuid().ToString("N"));
        internal Profile() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static async Task<JObject[]> RunProtocolHandshake(bool checkILogicArguments = false,
        bool checkHealth = false, bool readOnly = false, bool explicitHealthArgument = false,
        string? expressionTool = null, JObject? expressionArguments = null)
    {
        using var profile = new Profile();
        var serverAssembly = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(serverAssembly)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(serverAssembly);
        startInfo.ArgumentList.Add("--local-app-data");
        startInfo.ArgumentList.Add(profile.Root);
        if (readOnly) startInfo.ArgumentList.Add("--read-only");
        if (checkILogicArguments || expressionTool is not null)
        {
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add("__isolated_ilogic_binding_test__");
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ipt-mcp server.");
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke-test","version":"0.0"}}}""");
            var initialize = await ReadJsonResponse(process, "initialize");

            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
            var toolsList = await ReadJsonResponse(process, "tools/list");
            JObject? ruleCall = null;
            if (expressionTool is not null)
            {
                await process.StandardInput.WriteLineAsync(new JObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = 3, ["method"] = "tools/call",
                    ["params"] = new JObject { ["name"] = expressionTool, ["arguments"] = expressionArguments },
                }.ToString(Newtonsoft.Json.Formatting.None));
                ruleCall = await ReadJsonResponse(process, "expression binding");
            }
            if (checkILogicArguments)
            {
                await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"inventor_ilogic_run_rule","arguments":{"name":"unit-test-rule","arguments":{"increment":2,"flag":true,"text":"value","empty":null}}}}""");
                ruleCall = await ReadJsonResponse(process, "iLogic arguments");
            }
            if (checkHealth)
            {
                var arguments = explicitHealthArgument ? new JObject { ["check_updates"] = false } : new JObject();
                await process.StandardInput.WriteLineAsync(new JObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = 3, ["method"] = "tools/call",
                    ["params"] = new JObject { ["name"] = "inventor_health", ["arguments"] = arguments },
                }.ToString(Newtonsoft.Json.Formatting.None));
                ruleCall = await ReadJsonResponse(process, "health");
            }
            process.StandardInput.Close();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("ipt-mcp server did not exit after stdin closed.");
            }

            var stderr = await stderrTask;
            Assert.True(process.ExitCode == 0, "ipt-mcp server exited with code " + process.ExitCode + ". Stderr: " + stderr);

            var responses = ruleCall is null ? new[] { initialize, toolsList } : new[] { initialize, toolsList, ruleCall };

            Assert.DoesNotContain(responses, r => r["error"]?["code"]?.Value<int>() == -32601);
            Assert.DoesNotContain(responses, r => r["error"] is not null);
            return responses;
        }
        finally
        {
            // A startup/read timeout must not leave the owned server holding the temporary profile lock.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            await stderrTask;
        }
    }

    private static async Task<JObject> ReadJsonResponse(Process process, string label)
    {
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException("No MCP response received for " + label + ".");
        }

        return JObject.Parse(line);
    }
}
