using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Bimwright.Ipt.Server;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

public sealed class ServerInstructionsTests
{
    private static IEnumerable<(string Name, string Description)> AllTools() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods())
            .Select(m => (Tool: m.GetCustomAttribute<McpServerToolAttribute>(), Desc: m.GetCustomAttribute<DescriptionAttribute>()))
            .Where(x => x.Tool is not null)
            .Select(x => (x.Tool!.Name!, x.Desc?.Description ?? ""));

    // INSTR-MAX-2KB: the text is one constant; no startup flag composes a different string
    // (McpProtocolSmokeTests checks the served text for the default and --read-only launches).
    [Fact]
    public void Instructions_fit_in_2048_utf8_bytes_INSTR_MAX_2KB()
    {
        Assert.True(Encoding.UTF8.GetByteCount(ServerInstructions.Text) <= 2048,
            "ServerInstructions.Text is " + Encoding.UTF8.GetByteCount(ServerInstructions.Text) + " UTF-8 bytes; the cap is 2048.");
    }

    // INSTR-SAFETY-BLOCK, FC-SRV-06, BW-PERM-001 R-C: the safety block is present and is the last part of the text.
    [Fact]
    public void Instructions_end_with_the_safety_and_permissions_block_FC_SRV_06()
    {
        Assert.StartsWith("Safety & permissions:", ServerInstructions.SafetyAndPermissions);
        Assert.EndsWith(ServerInstructions.SafetyAndPermissions, ServerInstructions.Text);
        foreach (var rule in new[] { "prefer read tools", "dry_run", "not created in this session", "confirmation",
                     "inventor_batch_execute", "baked tool", "inventor_send_code", "report the denial", "project folder", "full paths" })
            Assert.Contains(rule, ServerInstructions.SafetyAndPermissions);
    }

    // INSTR-ORDER: product keywords, then target -> health -> report, then the tool index, then the safety block.
    [Fact]
    public void Instructions_keep_keywords_routing_index_and_safety_in_order_INSTR_ORDER()
    {
        var text = ServerInstructions.Text;
        int At(string s) { var i = text.IndexOf(s, StringComparison.Ordinal); Assert.True(i >= 0, "missing: " + s); return i; }
        var order = new[]
        {
            At("Autodesk Inventor 2022-2027"), At("iproperty"), At("export"),
            At("exactly one Inventor is live"), At("inventor_health(check_updates=true)"), At("inventor_report_task_result"),
            At("Tool index:"), At("Safety & permissions:"),
        };
        Assert.Equal(order.OrderBy(i => i), order);
        foreach (var rule in new[] { "AMBIGUOUS_TARGET", "TARGET_LOST", "verify=true", "Lengths are in mm", "--read-only" })
            Assert.Contains(rule, text);
    }

    // INSTR-NO-DUP-SENDCODE: the routing guide lives only in the inventor_send_code description.
    [Fact]
    public void Instructions_do_not_repeat_the_send_code_task_guide_INSTR_NO_DUP_SENDCODE()
    {
        Assert.DoesNotContain(SendCodeTaskGuide.Text.Trim(), ServerInstructions.Text);
        Assert.DoesNotContain("Typed tools first", ServerInstructions.Text);
        Assert.Contains(SendCodeTaskGuide.Text, AllTools().Single(t => t.Name == "inventor_send_code").Description);
    }

    // INSTR-MOVE-TO-TOOL: update-check and target-handling detail moved to the tool that owns it.
    [Fact]
    public void Tool_specific_detail_lives_in_the_tool_description_INSTR_MOVE_TO_TOOL()
    {
        var tools = AllTools().ToDictionary(t => t.Name, t => t.Description);
        foreach (var moved in new[] { "update.notification", "bundle_update_proposal_allowed", "protected_components", "client_verification" })
        {
            Assert.DoesNotContain(moved, ServerInstructions.Text);
            Assert.Contains(moved, tools["inventor_health"]);
        }
        Assert.Contains("AMBIGUOUS_TARGET", tools["inventor_list_available_targets"]);
        Assert.Contains("TARGET_LOST", tools["inventor_get_current_target"]);
        Assert.Contains("DOCUMENT_UNCONFIRMED", tools["inventor_switch_target"]);
        Assert.Contains("PAIRING_REQUIRED", tools["inventor_switch_target"]);
        Assert.Contains("inventor_get_document_state", tools["inventor_save_document"]);
    }

    [Fact]
    public void Every_full_tool_name_in_the_instructions_is_a_real_tool()
    {
        var names = AllTools().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(ServerInstructions.Text, @"inventor_[a-z_]+[a-z]"))
            Assert.Contains(m.Value, names);
    }
}
