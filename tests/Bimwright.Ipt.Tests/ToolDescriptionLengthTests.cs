using System.ComponentModel;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

public sealed class ToolDescriptionLengthTests
{
    // Claude Code truncates a preloaded tool description at 4,096 characters.
    private const int MaxDescriptionLength = 4096;

    private static List<(string Name, int Length)> Descriptions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods())
            .Select(m => (Tool: m.GetCustomAttribute<McpServerToolAttribute>(), Desc: m.GetCustomAttribute<DescriptionAttribute>()))
            .Where(x => x.Tool is not null)
            .Select(x => (x.Tool!.Name!, x.Desc?.Description.Length ?? 0))
            .ToList();

    // INSTR-MOVE-TO-TOOL: every tool description, inventor_send_code included, fits in the limit.
    [Fact]
    public void Every_tool_description_fits_in_4096_characters_INSTR_MOVE_TO_TOOL()
    {
        var all = Descriptions();
        Assert.True(all.Count > 100, "expected the full tool catalog, found " + all.Count);
        var tooLong = all.Where(d => d.Length > MaxDescriptionLength).Select(d => d.Name + "=" + d.Length).ToList();
        Assert.True(tooLong.Count == 0, "Descriptions over 4096 characters: " + string.Join(", ", tooLong));
    }

    // The send_code detail that left the tool description lives on its parameters, so nothing was dropped.
    [Fact]
    public void Send_code_parameters_carry_the_script_guidance_moved_out_of_the_description()
    {
        var parameters = typeof(CodeTools).GetMethod(nameof(CodeTools.SendCode))!.GetParameters()
            .ToDictionary(p => p.Name!, p => p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "");
        Assert.Contains(SendCodeHelperGuide.Text, parameters["code"]);
        Assert.Contains("COM interop tips", parameters["code"]);
        Assert.Contains("System.IO.Path.{GetFileName", parameters["code"]);
        Assert.Contains("inventor_save_code_module", parameters["modules"]);
        Assert.Contains("600000", parameters["timeout_ms"]);
        Assert.Contains("SilentOperation", parameters["silent"]);
        Assert.Contains("512 KiB", parameters["data"]);
        Assert.Contains("bake_hint", parameters["data"]);
        Assert.Contains("diagnostics", parameters["compile_only"]);
        Assert.Contains("TASK_RESULT_REQUIRED", parameters["result_contract"]);
    }
}
