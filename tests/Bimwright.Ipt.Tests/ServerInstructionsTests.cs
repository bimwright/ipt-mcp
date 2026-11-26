using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Bimwright.Ipt.Server;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

public sealed class ServerInstructionsTests
{
    internal static IEnumerable<(string Name, string Description, MethodInfo Method)> AllToolMethods() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods())
            .Select(m => (Method: m, Tool: m.GetCustomAttribute<McpServerToolAttribute>(), Desc: m.GetCustomAttribute<DescriptionAttribute>()))
            .Where(x => x.Tool is not null)
            .Select(x => (x.Tool!.Name!, x.Desc?.Description ?? "", x.Method));

    private static IEnumerable<(string Name, string Description)> AllTools() =>
        AllToolMethods().Select(t => (t.Name, t.Description));

    // INSTR-MAX-2KB: the text is one constant; no startup flag composes a different string
    // (McpProtocolSmokeTests checks the served text for the default and --read-only launches).
    [Fact]
    public void Instructions_fit_in_2048_utf8_bytes_INSTR_MAX_2KB()
    {
        var bytes = Encoding.UTF8.GetByteCount(ServerInstructions.Text);
        Assert.True(bytes <= 2048,
            "ServerInstructions.Text is " + bytes + " UTF-8 bytes; the cap is 2048.");
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
            At("Auto-bind requires a unique target"), At("inventor_list_available_targets"), At("inventor_switch_target"),
            At("inventor_health(check_updates=true)"), At("inventor_report_task_result"),
            At("Tool index:"), At("Safety & permissions:"),
        };
        Assert.Equal(order.OrderBy(i => i), order);
        foreach (var rule in new[]
                 {
                     "AMBIGUOUS_TARGET", "TARGET_LOST", "verify=true", "Lengths are in mm",
                     "HOST_BUSY", "TARGET_BUSY", "TIMEOUT", "HOST_CRASHED",
                     // Release-contract invariants verified by tests/stdio/release-contract.py
                     "inventor_send_code is enabled by default", "--disable-send-code", "--read-only",
                 })
            Assert.Contains(rule, text);
        Assert.DoesNotContain("PLUGIN_ENABLE_SEND_CODE", text);
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
        Assert.Contains("host_busy", tools["inventor_health"]);
        Assert.Contains("AMBIGUOUS_TARGET", tools["inventor_list_available_targets"]);
        Assert.Contains("TARGET_LOST", tools["inventor_get_current_target"]);
        Assert.Contains("DOCUMENT_UNCONFIRMED", tools["inventor_switch_target"]);
        Assert.Contains("PAIRING_REQUIRED", tools["inventor_switch_target"]);
        Assert.Contains("HOST_CRASHED", tools["inventor_switch_target"]);
        Assert.Contains("inventor_get_document_state", tools["inventor_save_document"]);
        Assert.Contains("Activity toasts", tools["inventor_report_task_result"]);
        Assert.Contains("BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1", tools["inventor_send_code"]);
        // SendCode-Context: accepted code shape and default namespaces remain in the tool description.
        Assert.Contains("Pass a C# script body with statements and return", tools["inventor_send_code"]);
        Assert.Contains("Imports: System, System.Collections.Generic, System.Linq, Inventor.", tools["inventor_send_code"]);
    }

    [Fact]
    public void Every_full_tool_name_and_index_prefix_in_the_instructions_matches_real_tools()
    {
        var names = AllTools().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(ServerInstructions.Text, @"inventor_[a-z0-9_]+[a-z0-9]"))
            Assert.Contains(m.Value, names);
        var indexStart = ServerInstructions.Text.IndexOf("Tool index:", StringComparison.Ordinal);
        var indexEnd = ServerInstructions.Text.IndexOf("Safety & permissions:", StringComparison.Ordinal);
        var index = ServerInstructions.Text[indexStart..indexEnd];
        foreach (Match m in Regex.Matches(index, @"\b([a-z]+(?:_[a-z]+)*_)\*"))
        {
            var prefix = "inventor_" + m.Groups[1].Value;
            Assert.Contains(names, n => n.StartsWith(prefix, StringComparison.Ordinal));
        }
    }
}
