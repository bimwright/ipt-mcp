using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Security;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ReleaseContractTests
{
    [Fact]
    public void Explicit_disable_overrides_enable_and_invalid_retention_uses_default()
    {
        var config = InventorMcpConfig.Load(["--enable-send-code", "--disable-send-code", "--enable-call-log", "--disable-call-log", "--spill-retention-hours", "0"]);
        Assert.False(config.EnableSendCode);
        Assert.False(config.EnableCallLog);
        Assert.Equal(36, config.SpillRetentionHours);
    }

    [Fact]
    public void Every_tool_has_complete_metadata_and_permissions_except_send_code()
    {
        var types = Program.ResolveToolTypesForRegistration(new InventorMcpConfig { Toolsets = { "all" } });
        var catalog = ToolCatalog.Build(types);
        Assert.Equal(111, catalog.Count);
        foreach (var method in types.SelectMany(t => t.GetMethods()))
        {
            var attr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attr?.Name == null) continue;
            var named = method.CustomAttributes.Single(a => a.AttributeType == typeof(McpServerToolAttribute)).NamedArguments;
            if (attr.Name != "inventor_send_code")
                foreach (var hint in new[] { "ReadOnly", "Destructive", "Idempotent", "OpenWorld" })
                    Assert.Contains(named, a => a.MemberName == hint);
            else Assert.DoesNotContain(named, a => a.MemberName != "Name");
            var metadata = ToolCatalog.ForCommand(attr.Name.Substring(9), 1200)!;
            Assert.False(string.IsNullOrWhiteSpace(metadata.Description));
            Assert.False(string.IsNullOrWhiteSpace(metadata.Toolset));
            Assert.Equal(1200, metadata.TimeoutMs);
        }
    }

    [Fact]
    public void Parameter_query_is_not_compacted_as_a_completed_write()
    {
        var result = AgentOutputGuard.Apply("inventor_list_parameters", Payload(new JObject { ["parameters"] = new string('.', 1100000) }), new InventorMcpConfig());
        Assert.True(result.IsError);
        Assert.Contains("RESPONSE_TOO_LARGE", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public void Payload_failure_is_an_MCP_error_even_below_the_size_budget()
    {
        var result = AgentOutputGuard.Apply("inventor_send_code", Payload(new JObject { ["ok"] = false, ["error"] = "compile failure" }), new InventorMcpConfig());
        Assert.True(result.IsError);
    }

    [Fact]
    public void Completed_write_keeps_truthful_effects_under_a_custom_budget()
    {
        var result = AgentOutputGuard.Apply("inventor_extrude", Payload(new JObject {
            ["ok"] = true, ["created"] = true, ["name"] = "ReleaseFixture", ["detail"] = new string('.', 8000)
        }), new InventorMcpConfig { OutputBudgetBytes = 4096 });
        var data = JObject.Parse(((TextContentBlock)result.Content[0]).Text);
        Assert.True(data.Value<bool>("created"));
        Assert.Equal("ReleaseFixture", data.Value<string>("name"));
        Assert.True(data.Value<bool>("response_compacted"));
        Assert.Contains("do not replay", data.Value<string>("size_warning"));
        Assert.True(AgentOutputGuard.Measure(result) <= 4096);
    }

    [Theory]
    [InlineData("inventor_send_code")]
    [InlineData("inventor_run_baked_tool")]
    public void Script_spill_is_redacted_and_never_claims_a_known_mutation(string tool)
    {
        var root = Path.Combine(Path.GetTempPath(), "ipt-spill-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = InventorMcpConfig.Load(["--local-app-data", root, "--output-warning-bytes", "512", "--output-strong-warning-bytes", "1024", "--output-budget-bytes", "4096"]);
            var result = AgentOutputGuard.Apply(tool, Payload(new JObject {
                ["ok"] = true, ["result"] = new string('.', 9000), ["stdout"] = "password='fixture-private-value'"
            }), config);
            var data = JObject.Parse(((TextContentBlock)result.Content[0]).Text);
            var file = data.Value<string>("response_file")!;
            Assert.StartsWith(root, file);
            Assert.Equal(JTokenType.Null, data["mutation_applied"]!.Type);
            Assert.Equal(1, data["spill_schema"]!.Value<int>("version"));
            Assert.True(data.Value<bool>("readback_required"));
            Assert.Contains("Do not re-run", data.Value<string>("size_warning"));
            Assert.DoesNotContain("fixture-private-value", File.ReadAllText(file));
            Assert.True(AgentOutputGuard.Measure(result) <= config.OutputBudgetBytes);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Guard_off_retains_the_transport_cap()
    {
        var payload = Payload(new JObject { ["parameters"] = new string('.', 8000) });
        var config = new InventorMcpConfig { EnableOutputGuard = false, OutputBudgetBytes = 4096, MaxResponseBytes = 16000 };
        Assert.Same(payload, AgentOutputGuard.Apply("inventor_list_parameters", payload, config));
        config.MaxResponseBytes = 4096;
        var fenced = AgentOutputGuard.Apply("inventor_list_parameters", payload, config);
        Assert.True(fenced.IsError);
        Assert.Contains("RESPONSE_TOO_LARGE", ((TextContentBlock)fenced.Content[0]).Text);
    }

    [Fact]
    public void Every_catalog_entry_classifies_the_toast_like_the_permission_hint()
    {
        var catalog = ToolCatalog.Build(Program.ResolveToolTypesForRegistration(new InventorMcpConfig()));
        foreach (var entry in catalog)
        {
            var kind = Bimwright.Ipt.Shared.Views.Toast.ToolActivityClassifier.Classify(entry.Key, entry.Value.ReadOnly);
            Assert.Equal(entry.Value.ReadOnly, kind == Bimwright.Ipt.Shared.Views.Toast.ToolActivityKind.Read);
        }
    }

    [Fact]
    public void Inspection_does_not_create_the_bake_database()
    {
        var path = Path.Combine(Path.GetTempPath(), "ipt-inspection-" + Guid.NewGuid().ToString("N"));
        var tools = new ToolBakerTools(new InventorMcpConfig { BakeDirectory = path, ReadOnly = true });
        Assert.Empty(JObject.Parse(tools.ListBakedTools())["tools"]!);
        Assert.Empty(JObject.Parse(tools.ListBakeSuggestions())["suggestions"]!);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Nested_module_bodies_are_hashed_before_plugin_logging()
    {
        var body = "return 42;";
        var result = CallLogPrivacy.Redact(new JObject { ["modules"] = new JArray(new JObject { ["code"] = body }), ["auth_token"] = "fixture" });
        Assert.DoesNotContain(body, result.ToString());
        Assert.Equal(SendCodeSource.Hash(body), result["modules"]![0]!["code"]!["sha256"]);
        Assert.Equal("***", result["auth_token"]);
    }

    private static CallToolResult Payload(JObject data) => new() {
        Content = new[] { new TextContentBlock { Text = data.ToString(Newtonsoft.Json.Formatting.None) } }
    };
}
