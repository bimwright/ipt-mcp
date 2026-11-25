using System.IO.Pipes;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>Family contract FC-TGT-07: a client AppContainer sandbox is reported as CLIENT_SANDBOXED, never bypassed.</summary>
public sealed class ClientSandboxTests : IDisposable
{
    private const string Plain = @"C:\Users\x\AppData\Local";
    private const string Ac = @"C:\Users\x\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\AC";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ipt-sandbox-" + Guid.NewGuid().ToString("N"));
    private readonly string _pipe = "IptSandboxTest-" + Guid.NewGuid().ToString("N");

    public ClientSandboxTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    // ---- classifier (FC-TGT-07) ----

    [Fact]
    public void FC_TGT_07_appcontainer_token_alone_is_detected()
    {
        var r = ClientSandbox.Classify(true, Plain);
        Assert.True(r.Detected);
        Assert.Equal("appcontainer", r.Kind);
        Assert.Equal(new[] { "token_is_appcontainer" }, r.Evidence);
    }

    [Fact]
    public void FC_TGT_07_packages_ac_path_alone_is_detected()
    {
        var r = ClientSandbox.Classify(false, Ac + @"\#!sandbox.{0B9D2F2E-1111-2222-3333-444455556666}");
        Assert.True(r.Detected);
        Assert.Equal("appcontainer", r.Kind);
        Assert.Equal(new[] { "local_app_data_under_packages_ac" }, r.Evidence);
    }

    [Fact]
    public void FC_TGT_07_token_and_path_report_both_evidence()
    {
        var r = ClientSandbox.Classify(true, Ac);
        Assert.True(r.Detected);
        Assert.Equal(new[] { "token_is_appcontainer", "local_app_data_under_packages_ac" }, r.Evidence);
    }

    [Fact]
    public void FC_TGT_07_neither_signal_is_not_detected()
    {
        var r = ClientSandbox.Classify(false, Plain);
        Assert.False(r.Detected);
        Assert.Null(r.Kind);
        Assert.Empty(r.Evidence);
    }

    [Fact]
    public void FC_TGT_07_unknown_token_falls_back_to_the_path()
    {
        Assert.False(ClientSandbox.Classify(null, Plain).Detected);
        Assert.True(ClientSandbox.Classify(null, Ac).Detected);
    }

    [Theory]
    [InlineData(Ac + @"\")]
    [InlineData(Ac + @"\\")]
    [InlineData("C:/Users/x/AppData/Local/Packages/OpenAI.Codex_2p2nqsd0c76g0/AC")]
    [InlineData("C:/Users/x/AppData/Local/Packages/OpenAI.Codex_2p2nqsd0c76g0/AC/")]
    [InlineData(@"c:\users\x\appdata\local\packages\pkg\ac\Bimwright\ipt-mcp")]
    public void FC_TGT_07_path_rule_tolerates_trailing_separators_forward_slashes_and_case(string path)
        => Assert.True(ClientSandbox.Classify(false, path).Detected);

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Packages\Foo\ACX")]
    [InlineData(@"C:\Users\x\AppData\Local\PackagesX\Foo\AC")]
    [InlineData(@"C:\Users\x\AppData\Local\Packages\AC")]
    [InlineData(@"C:\Users\x\AppData\Local")]
    [InlineData("")]
    [InlineData(null)]
    public void FC_TGT_07_lookalike_paths_are_not_detected(string? path)
        => Assert.False(ClientSandbox.Classify(false, path).Detected);

    [Fact]
    public void FC_TGT_07_either_path_matching_is_enough()
        => Assert.True(ClientSandbox.Classify(false, Plain, Ac + @"\Bimwright\ipt-mcp").Detected);

    [Fact]
    public void FC_TGT_07_token_probe_never_throws_and_is_cached()
        => Assert.Equal(ClientSandbox.ProcessTokenIsAppContainer(), ClientSandbox.ProcessTokenIsAppContainer());

    // ---- server behaviour (FC-TGT-07) ----

    private PluginClient Client(bool sandboxed) => new(new InventorMcpConfig
    {
        LocalAppDataRoot = _root, DescriptorDirectory = _root, TimeoutMs = 1000,
    }) { AppContainerProbe = () => sandboxed };

    [Fact]
    public void FC_TGT_07_list_targets_reports_sandbox_and_guidance_when_sandboxed_with_no_targets()
    {
        var result = JObject.Parse(new MetaTools(Client(true)).ListAvailableTargets());
        Assert.Empty((JArray)result["targets"]!);
        Assert.Equal(ClientSandbox.Message, (string?)result["note"]);
        AssertSandboxBlock(result["sandbox"]);
    }

    [Fact]
    public async Task FC_TGT_07_target_tool_returns_client_sandboxed_error_without_sending()
    {
        var text = await new QueryTools(Client(true)).ListOpenDocuments();
        var result = JObject.Parse(text);
        Assert.False(result.Value<bool>("ok"));
        Assert.Equal(InventorErrorCodes.CLIENT_SANDBOXED, (string?)result["error"]?["code"]);
        Assert.Equal(ClientSandbox.Message, (string?)result["error"]?["message"]);
        Assert.Contains("The command was not sent.", (string?)result["error"]?["message"]);

        var mcp = AgentOutputGuard.Apply("inventor_list_open_documents",
            new CallToolResult { Content = new[] { new TextContentBlock { Text = text } } }, new InventorMcpConfig());
        Assert.True(mcp.IsError);
    }

    [Fact]
    public void FC_TGT_07_resolve_and_switch_carry_the_sandbox_block_in_the_error_data()
    {
        var client = Client(true);
        var resolve = Assert.Throws<InventorGatewayException>(() => client.ResolveTarget());
        Assert.Equal(InventorErrorCodes.CLIENT_SANDBOXED, resolve.Code);
        AssertSandboxBlock(resolve.ResponseData?["sandbox"]);
        Assert.Empty((JArray)resolve.ResponseData!["candidates"]!);

        var sw = Assert.Throws<InventorGatewayException>(() => client.SwitchTarget("pid:1"));
        Assert.Equal(InventorErrorCodes.CLIENT_SANDBOXED, sw.Code);
    }

    [Fact]
    public async Task FC_TGT_07_health_reports_client_sandboxed_and_the_sandbox_block()
    {
        var result = JObject.Parse(await new QueryTools(Client(true)).Health());
        Assert.False(result.Value<bool>("ok"));
        Assert.False(result.Value<bool>("host_connected"));
        Assert.Equal(InventorErrorCodes.CLIENT_SANDBOXED, (string?)result["error"]?["code"]);
        AssertSandboxBlock(result["sandbox"]);
    }

    [Fact]
    public async Task FC_TGT_07_not_sandboxed_keeps_no_target_unchanged()
    {
        var client = Client(false);
        var result = JObject.Parse(await new QueryTools(client).ListOpenDocuments());
        Assert.Equal(InventorErrorCodes.NO_TARGET, (string?)result["error"]?["code"]);
        Assert.Empty(JArray.Parse(new MetaTools(client).ListAvailableTargets()));
        var health = JObject.Parse(await new QueryTools(client).Health());
        Assert.Equal(InventorErrorCodes.NO_TARGET, (string?)health["error"]?["code"]);
        Assert.Null(health["sandbox"]);
    }

    [Fact]
    public async Task FC_TGT_07_sandboxed_with_a_live_target_behaves_normally()
    {
        File.WriteAllText(Path.Combine(_root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, InventorYear = 2027,
            ProcessId = Environment.ProcessId, HostApp = "Inventor", Transport = "pipe",
            PipeName = _pipe, AuthToken = "private-test-token", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        }));
        var client = Client(true);

        var list = JArray.Parse(new MetaTools(client).ListAvailableTargets());
        Assert.Single(list);

        using var pipe = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receive = Task.Run(async () =>
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var envelope = JObject.Parse((await reader.ReadLineAsync(timeout.Token))!);
            Assert.Equal("list_open_documents", envelope.Value<string>("command"));
            await writer.WriteLineAsync(new JObject
            {
                ["id"] = envelope["id"], ["ok"] = true, ["data"] = new JObject { ["documents"] = new JArray() }, ["meta"] = new JObject(),
            }.ToString(Formatting.None));
        }, timeout.Token);
        var result = JObject.Parse(await new QueryTools(client).ListOpenDocuments(ct: timeout.Token));
        await receive;
        Assert.NotNull(result["documents"]);
        Assert.Null(result["error"]);
    }

    // ---- startup inside an AppContainer (FC-TGT-07) ----

    private string AcLocal => Path.Combine(_root, "Packages", "repro71", "AC");

    private static Bimwright.Setup.RuntimeLayout Denied() =>
        throw new UnauthorizedAccessException(@"Access to the path 'C:\Users\x\AppData\Local\Packages\repro71' is denied.");

    [Fact]
    public async Task FC_TGT_07_sandboxed_layout_denial_falls_back_so_config_loads_and_tools_return_client_sandboxed()
    {
        var layout = ServerLayout.Resolve(Denied, () => true, AcLocal);
        Assert.True(layout.SandboxFallback);
        var dir = Path.Combine(AcLocal, "Bimwright", "ipt-mcp");
        Assert.Equal(dir, layout.DataRoot);
        Assert.Equal(dir, layout.RuntimeRoot);
        Assert.Equal(Path.Combine(AcLocal, "Bimwright", "ipt-mcp-calls.jsonl"), layout.GatewayLogPath);

        var config = InventorMcpConfig.Load(new[] { "--timeout-ms", "1000" }, layout);
        Assert.Equal(dir, config.DescriptorDirectory);
        Assert.Equal(Path.Combine(dir, "baked"), config.BakeDirectory);

        // The descriptor dir alone is evidence (Packages\<pkg>\AC), so no token seam is needed here.
        var client = new PluginClient(config) { AppContainerProbe = () => false };
        Assert.False(client.GatewayIdentity.Value<bool>("managed"));

        var call = JObject.Parse(await new QueryTools(client).ListOpenDocuments());
        Assert.Equal(InventorErrorCodes.CLIENT_SANDBOXED, (string?)call["error"]?["code"]);

        var health = JObject.Parse(await new QueryTools(client).Health());
        Assert.Equal(InventorErrorCodes.CLIENT_SANDBOXED, (string?)health["error"]?["code"]);
        Assert.Equal(new[] { "local_app_data_under_packages_ac" }, health["sandbox"]!["evidence"]!.Values<string>());
        Assert.Equal(dir, (string?)health["sandbox"]!["data_dir"]);

        var list = JObject.Parse(new MetaTools(client).ListAvailableTargets());
        Assert.Equal(ClientSandbox.Message, (string?)list["note"]);
    }

    [Fact]
    public void FC_TGT_07_layout_denial_outside_a_sandbox_still_throws()
    {
        Assert.Throws<UnauthorizedAccessException>(() => ServerLayout.Resolve(Denied, () => false, AcLocal));
        Assert.Throws<UnauthorizedAccessException>(() => ServerLayout.Start(Denied, () => false));
    }

    [Fact]
    public void FC_TGT_07_sandbox_only_excuses_access_denied_not_a_reparse_point()
    {
        static Bimwright.Setup.RuntimeLayout Reparse() => throw new InvalidOperationException("setup_reparse_point");
        Assert.Equal("setup_reparse_point",
            Assert.Throws<InvalidOperationException>(() => ServerLayout.Resolve(Reparse, () => true, AcLocal)).Message);
        Assert.Throws<InvalidOperationException>(() => ServerLayout.Start(Reparse, () => true));
    }

    [Fact]
    public void FC_TGT_07_sandboxed_start_denial_returns_no_layout_and_a_working_layout_is_used_unchanged()
    {
        Assert.Null(ServerLayout.Start(Denied, () => true));

        var real = Bimwright.Setup.RuntimeLayout.Resolve(_root,
            System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, "ipt-mcp");
        var resolved = ServerLayout.Resolve(() => real, () => true, AcLocal);
        Assert.Same(real, resolved.Layout);
        Assert.Equal(real.RuntimeRoot, resolved.RuntimeRoot);
    }

    private void AssertSandboxBlock(JToken? sandbox)
    {
        Assert.NotNull(sandbox);
        Assert.True(sandbox!.Value<bool>("detected"));
        Assert.Equal("appcontainer", (string?)sandbox["kind"]);
        Assert.Contains("token_is_appcontainer", sandbox["evidence"]!.Values<string>());
        Assert.Equal(_root, (string?)sandbox["data_dir"]);
    }
}
