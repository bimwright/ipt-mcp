using Bimwright.Ipt.Server;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class AgentOutputGuardTests
{
    internal static CallToolResult Result(JObject data) => new() { Content = new[] { new TextContentBlock { Text = ToolResponse.Serialize(data) } } };
    internal static JObject Data(CallToolResult result) => JObject.Parse(((TextContentBlock)result.Content[0]).Text);

    [Theory]
    [InlineData("inventor_get_drawing_info", true)]
    [InlineData("inventor_list_bodies", true)]
    [InlineData("inventor_add_drawing_dimension", false)]
    [InlineData("inventor_create_part", false)]
    public void Oversized_results_use_final_MCP_bytes_and_preserve_write_effects(string name, bool readOnly)
    {
        var result = Result(new JObject { ["ok"] = true, ["created_count"] = 1, ["document"] = "Drawing", ["blob"] = new string('界', 400000) });
        var guarded = AgentOutputGuard.Apply(name, result, new InventorMcpConfig());
        var data = Data(guarded);
        Assert.True(AgentOutputGuard.Measure(guarded) < 1024 * 1024);
        Assert.Equal(readOnly, guarded.IsError);
        Assert.Equal(!readOnly, data.Value<bool>("ok"));
        if (readOnly) Assert.Contains("max_items", (string?)data["error"]!["message"]);
        else { Assert.Equal(1, data.Value<int>("created_count")); Assert.True(data.Value<bool>("response_compacted")); Assert.Contains("do not replay", data.Value<string>("size_warning")); }
    }

    [Fact]
    public void Guard_off_and_custom_thresholds_apply_without_losing_transport_cap()
    {
        var result = Result(new JObject { ["ok"] = true, ["blob"] = new string('.', 70000) });
        Assert.Same(result, AgentOutputGuard.Apply("inventor_get_drawing_info", result, new InventorMcpConfig { EnableOutputGuard = false }));
        var cfg = new InventorMcpConfig { OutputWarningBytes = 512, OutputStrongWarningBytes = 1024, OutputBudgetBytes = 4096 };
        Assert.True(AgentOutputGuard.Apply("inventor_get_drawing_info", Result(new JObject { ["blob"] = new string('.', 5000) }), cfg).IsError);
        Assert.Equal(2, AgentOutputGuard.Apply("inventor_get_drawing_info", Result(new JObject { ["blob"] = new string('.', 1500) }), cfg).Content.Count);
        cfg.EnableOutputGuard = false; cfg.MaxResponseBytes = 4096;
        Assert.True(AgentOutputGuard.Apply("inventor_get_drawing_info", Result(new JObject { ["blob"] = new string('.', 5000) }), cfg).IsError);
    }

    [Fact]
    public void Partial_write_failure_keeps_completed_count_and_failed_outcome()
    {
        var result = Result(new JObject { ["ok"] = false, ["completed_count"] = 1, ["failed_count"] = 1, ["error"] = "Second file failed.", ["blob"] = new string('.', 1100000) });
        var guarded = AgentOutputGuard.Apply("inventor_export_drawing", result, new InventorMcpConfig());
        Assert.True(guarded.IsError); Assert.Equal(1, Data(guarded).Value<int>("completed_count"));
        Assert.Equal("Second file failed.", Data(guarded).Value<string>("error"));
    }

    [Fact]
    public void Structured_content_cannot_bypass_final_budget()
    {
        var result = Result(new JObject { ["ok"] = true });
        using var json = System.Text.Json.JsonDocument.Parse("{\"blob\":\"" + new string('.', 1100000) + "\"}");
        result.StructuredContent = json.RootElement.Clone();
        var guarded = AgentOutputGuard.Apply("inventor_get_drawing_info", result, new InventorMcpConfig());
        Assert.True(guarded.IsError); Assert.Null(guarded.StructuredContent);
    }

    [Fact]
    public void JSON_and_CLI_options_preserve_precedence_and_reject_invalid_thresholds()
    {
        var file = Path.GetTempFileName();
        try {
            File.WriteAllText(file, "{\"enableOutputGuard\":true,\"outputWarningBytes\":512,\"outputStrongWarningBytes\":1024,\"outputBudgetBytes\":4096}");
            var cfg = InventorMcpConfig.Load(new[] { "--config", file, "--disable-output-guard", "--output-budget-bytes", "8192" });
            Assert.False(cfg.EnableOutputGuard); Assert.Equal(8192, cfg.OutputBudgetBytes); Assert.Equal(512, cfg.OutputWarningBytes);
            Assert.Throws<ArgumentException>(() => InventorMcpConfig.Load(new[] { "--output-budget-bytes", "10" }));
        } finally { File.Delete(file); }
    }
}
