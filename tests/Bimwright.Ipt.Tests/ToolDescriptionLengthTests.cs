using System.ComponentModel;
using System.Reflection;
using System.Text;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;

namespace Bimwright.Ipt.Tests;

public sealed class ToolDescriptionLengthTests
{
    // Claude Code truncates a preloaded tool description at 4,096 characters.
    private const int MaxDescriptionLength = 4096;
    private const int MaxParameterDescriptionBytes = 2048;

    // INSTR-MOVE-TO-TOOL: every tool description, inventor_send_code included, fits in 4,096 chars and UTF-8 bytes.
    [Fact]
    public void Every_tool_description_fits_in_4096_characters_INSTR_MOVE_TO_TOOL()
    {
        var all = ServerInstructionsTests.AllToolMethods().ToList();
        Assert.True(all.Count >= 150, "expected the full tool catalog, found " + all.Count);
        var tooLongChars = all.Where(d => d.Description.Length > MaxDescriptionLength)
            .Select(d => d.Name + "=" + d.Description.Length).ToList();
        Assert.True(tooLongChars.Count == 0, "Descriptions over 4096 characters: " + string.Join(", ", tooLongChars));
        var tooLongBytes = all.Where(d => Encoding.UTF8.GetByteCount(d.Description) > MaxDescriptionLength)
            .Select(d => d.Name + "=" + Encoding.UTF8.GetByteCount(d.Description)).ToList();
        Assert.True(tooLongBytes.Count == 0, "Descriptions over 4096 UTF-8 bytes: " + string.Join(", ", tooLongBytes));
    }

    // Keep parameter descriptions bounded so moving detail off a tool description does not recreate truncation in inputSchema.
    [Fact]
    public void Every_tool_parameter_description_fits_in_2048_utf8_bytes()
    {
        var tooLong = ServerInstructionsTests.AllToolMethods()
            .SelectMany(t => t.Method.GetParameters().Select(p => (
                Key: t.Name + "." + p.Name,
                Bytes: Encoding.UTF8.GetByteCount(p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? ""))))
            .Where(x => x.Bytes > MaxParameterDescriptionBytes)
            .Select(x => x.Key + "=" + x.Bytes)
            .ToList();
        Assert.True(tooLong.Count == 0, "Parameter descriptions over 2048 UTF-8 bytes: " + string.Join(", ", tooLong));
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
        Assert.Contains("size_hint", parameters["code"]);
        Assert.Contains("inventor_save_code_module", parameters["modules"]);
        Assert.Contains("600000", parameters["timeout_ms"]);
        Assert.Contains("SilentOperation", parameters["silent"]);
        Assert.Contains("512 KiB", parameters["data"]);
        Assert.Contains("bake_hint", parameters["data"]);
        Assert.Contains("diagnostics", parameters["compile_only"]);
        Assert.Contains("TASK_RESULT_REQUIRED", parameters["result_contract"]);
    }
}
