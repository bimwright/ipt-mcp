using System.IO.Pipes;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class HealthToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ipt-health-" + Guid.NewGuid().ToString("N"));
    private readonly string _pipe = "IptHealthTest-" + Guid.NewGuid().ToString("N");
    private readonly ReleaseUpdateCheckTests.Handler _http = new();
    private readonly HttpClient _httpClient;
    private readonly QueryTools _tools;

    public HealthToolsTests()
    {
        Directory.CreateDirectory(_root);
        _httpClient = new HttpClient(_http);
        _tools = new QueryTools(new PluginClient(new InventorMcpConfig
        {
            LocalAppDataRoot = _root, DescriptorDirectory = _root, TimeoutMs = 1000,
        }), new ReleaseUpdateCheck(_httpClient));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task No_target_preserves_server_identity_and_host_failure_without_network_or_install_writes()
    {
        var result = JObject.Parse(await _tools.Health());
        Assert.False(result.Value<bool>("ok"));
        Assert.False(result.Value<bool>("host_connected"));
        Assert.Equal("NO_TARGET", (string?)result["error"]?["code"]);
        Assert.Equal(Program.ServerVersion, (string?)result["mcp"]?["server_version"]);
        Assert.Null(result["mcp"]?["plugin_version"]?.Value<string>());
        Assert.NotEmpty(result["mcp"]!["server_build_id"]!.Value<string>()!);
        Assert.Equal("unknown", (string?)result["mcp"]?["version_alignment"]);
        Assert.NotEmpty(result["setup_gateway"]!["artifact_sha256"]!.Value<string>()!);
        Assert.Equal("not_checked", (string?)result["update"]?["status"]);
        Assert.Equal(JTokenType.Null, result["update"]!["notification"]!.Type);
        Assert.Equal("not_checked", (string?)result["update"]?["components"]?["plugin"]?["status"]);
        Assert.Equal("not_observable_by_server", (string?)result["client_verification"]?["status"]);
        Assert.Null(result["update"]?["update_available"]?.Value<bool?>());
        Assert.False(result["update_guidance"]!.Value<bool>("automatic_update_supported"));
        Assert.Equal(0, _http.Calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task Gateway_snapshot_is_captured_once_and_response_mutations_cannot_change_it()
    {
        var first = JObject.Parse(await _tools.Health());
        var snapshot = first["setup_gateway"]!.DeepClone();
        first["setup_gateway"]!["assembly_informational_version"] = "0.0.0+cached";
        var next = JObject.Parse(await _tools.Health());
        Assert.True(JToken.DeepEquals(snapshot, next["setup_gateway"]));
        Assert.Equal("loaded_assembly", (string?)next["setup_gateway"]?["identity_basis"]);
        Assert.Equal("on_disk_at_identity_capture", (string?)next["setup_gateway"]?["artifact_hash_basis"]);
    }

    [Theory]
    [InlineData(false, "check_only")]
    [InlineData(true, "retry_check")]
    public async Task Offline_and_failed_lookup_guidance_has_no_installation_commands(bool checkUpdates, string mode)
    {
        _http.Status = System.Net.HttpStatusCode.Forbidden;
        var health = JObject.Parse(await _tools.Health(checkUpdates));
        var guidance = Assert.IsType<JObject>(health["update_guidance"]);
        Assert.Equal(mode, guidance.Value<string>("mode"));
        Assert.False(guidance.Value<bool>("requires_user_authorization"));
        Assert.DoesNotContain("install.ps1", guidance["steps"]!.ToString());
        Assert.DoesNotContain("Run the installer", guidance["steps"]!.ToString());
    }

    [Fact]
    public async Task Observed_development_pair_gets_review_guidance_without_installation_commands()
    {
        _http.Body = """{"tag_name":"v0.2.1","draft":false,"prerelease":false}""";
        var health = await Probe(new JObject { ["sta_busy"] = false, ["setup_plugin"] = Identity("0.2.1+development") }, checkUpdates: true);
        Assert.Equal("review", (string?)health["update_guidance"]?["mode"]);
        Assert.DoesNotContain("install.ps1", health["update_guidance"]!["steps"]!.ToString());
        Assert.DoesNotContain("Run the installer", health["update_guidance"]!["steps"]!.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connected_health_preserves_document_and_busy_state_with_plugin_version(bool busy)
    {
        var identity = Identity("0.1.0+" + new string('b', 40));
        var result = await Probe(new JObject
        {
            ["inventor_year"] = 2027, ["process_id"] = Environment.ProcessId,
            ["has_active_document"] = busy ? null : true, ["document_type"] = busy ? null : "PartDocument",
            ["sta_busy"] = busy, ["pending_commands"] = busy ? 2 : 0,
            ["modal_dialog"] = new JObject { ["open"] = false }, ["setup_plugin"] = identity,
        });
        Assert.True(result.Value<bool>("host_connected"));
        Assert.Equal(busy, result.Value<bool>("sta_busy"));
        Assert.Equal(busy ? 2 : 0, result.Value<int>("pending_commands"));
        Assert.Equal(busy ? JTokenType.Null : JTokenType.Boolean, result["has_active_document"]!.Type);
        Assert.True(JToken.DeepEquals(identity, result["setup_plugin"]));
        Assert.Equal("0.1.0", (string?)result["mcp"]?["plugin_version"]);
        Assert.Equal("mismatch", (string?)result["mcp"]?["version_alignment"]);
        Assert.Equal(new string('b', 40), (string?)result["mcp"]?["plugin_build_id"]);
        Assert.Equal(0, _http.Calls);
    }

    [Fact]
    public async Task A_legacy_plugin_without_identity_reports_unknown_instead_of_inventing_a_version()
    {
        var result = await Probe(new JObject { ["has_active_document"] = false, ["sta_busy"] = false });
        Assert.True(result.Value<bool>("host_connected"));
        Assert.Null(result["mcp"]?["plugin_version"]?.Value<string>());
        Assert.Equal("unknown", (string?)result["mcp"]?["version_alignment"]);
        Assert.Equal("unknown", (string?)result["mcp"]?["revision_alignment"]);
    }

    [Fact]
    public async Task An_unreachable_target_keeps_gateway_identity_and_reports_a_failed_host_probe()
    {
        WriteDescriptor();
        var result = JObject.Parse(await _tools.Health());
        Assert.False(result.Value<bool>("ok"));
        Assert.False(result.Value<bool>("host_connected"));
        Assert.Equal("TARGET_UNAVAILABLE", (string?)result["error"]?["code"]);   // no pipe instance: nothing was sent
        Assert.Equal(Program.ServerVersion, (string?)result["mcp"]?["server_version"]);
    }

    [Fact]
    public async Task Explicit_release_check_works_without_a_host_and_does_not_hide_no_target()
    {
        var result = JObject.Parse(await _tools.Health(check_updates: true));
        Assert.Equal(2, _http.Calls);
        Assert.Equal("checked", (string?)result["update"]?["status"]);
        Assert.False(result.Value<bool>("host_connected"));
        Assert.Equal("NO_TARGET", (string?)result["error"]?["code"]);
        Assert.Equal("unknown", (string?)result["update"]?["components"]?["plugin"]?["status"]);
    }

    [Fact]
    public async Task Release_check_compares_the_replying_plugin_not_the_installed_file_or_gateway_version()
    {
        var result = await Probe(new JObject { ["sta_busy"] = false, ["setup_plugin"] = Identity("0.1.0+" + new string('b', 40)) }, checkUpdates: true);
        Assert.Equal(2, _http.Calls);
        Assert.Equal("0.1.0", (string?)result["update"]?["components"]?["plugin"]?["current_version"]);
        Assert.Equal("update_available", (string?)result["update"]?["components"]?["plugin"]?["status"]);
        Assert.Equal("mismatch", (string?)result["update"]?["components"]?["plugin"]?["build_status"]);
        Assert.Equal("NEW_RELEASE_AVAILABLE", (string?)result["update"]?["notification"]?["code"]);
        Assert.Equal(new[] { "server", "plugin" }, result["update"]!["notification"]!["components"]!.Values<string>());
        Assert.Equal("prepare_update", (string?)result["update_guidance"]?["mode"]);
        Assert.Contains("install.ps1 -WhatIf", result["update_guidance"]!["steps"]!.ToString());
        Assert.Contains("exact confirmed release tag", result["update_guidance"]!["steps"]!.ToString());
        Assert.Contains(".zip.sha256", result["update_guidance"]!["steps"]!.ToString());
        Assert.Contains("before extraction", result["update_guidance"]!["steps"]!.ToString());
        Assert.Contains("Require manifest.json", result["update_guidance"]!["steps"]!.ToString());
    }

    [Fact]
    public async Task A_release_API_failure_does_not_hide_a_successful_host_probe()
    {
        _http.Status = System.Net.HttpStatusCode.Forbidden;
        var result = await Probe(new JObject { ["sta_busy"] = false, ["has_active_document"] = false }, checkUpdates: true);
        Assert.True(result.Value<bool>("host_connected"));
        Assert.Null(result["error"]);
        Assert.Equal("unknown", (string?)result["update"]?["status"]);
        Assert.Null(result["update"]?["update_available"]?.Value<bool?>());
        Assert.Equal(1, _http.Calls);
    }

    [Theory]
    [InlineData("0.2.1", "0.2.1", "match", "unknown")]
    [InlineData("0.2.1+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "0.2.1+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "match", "match")]
    [InlineData("0.2.1+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "0.2.1+bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "match", "mismatch")]
    [InlineData("0.2.1", "0.2.0", "mismatch", "unknown")]
    public void Same_semver_does_not_hide_different_revisions_or_missing_build_evidence(
        string server, string plugin, string versionAlignment, string revisionAlignment)
    {
        var health = new JObject { ["setup_plugin"] = Identity(plugin) };
        HealthRuntimeInfo.Enrich(health, Identity(server), true);
        Assert.Equal(versionAlignment, (string?)health["mcp"]?["version_alignment"]);
        Assert.Equal(revisionAlignment, (string?)health["mcp"]?["revision_alignment"]);
        Assert.NotNull(health["mcp"]?["server_build_id"]);
    }

    private static JObject Identity(string version) => new()
    {
        ["assembly_informational_version"] = version, ["module_version_id"] = Guid.NewGuid().ToString("D"),
        ["artifact_sha256"] = new string('c', 64), ["publisher_verified"] = false,
    };

    private void WriteDescriptor()
        => File.WriteAllText(Path.Combine(_root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, InventorYear = 2027,
            ProcessId = Environment.ProcessId, HostApp = "Inventor", Transport = "pipe",
            PipeName = _pipe, AuthToken = "private-test-token", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        }));

    private async Task<JObject> Probe(JObject data, bool checkUpdates = false)
    {
        WriteDescriptor();
        using var pipe = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receive = Task.Run(async () =>
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var envelope = JObject.Parse((await reader.ReadLineAsync(timeout.Token))!);
            Assert.Equal("health", envelope.Value<string>("command"));
            Assert.Empty((JObject)envelope["params"]!); // The GitHub option never travels to the host.
            await writer.WriteLineAsync(new JObject
            {
                ["id"] = envelope["id"], ["ok"] = true, ["data"] = data, ["meta"] = new JObject(),
            }.ToString(Formatting.None));
        }, timeout.Token);
        var result = JObject.Parse(await _tools.Health(checkUpdates, timeout.Token));
        await receive;
        Assert.DoesNotContain("private-test-token", result.ToString());
        return result;
    }
}
