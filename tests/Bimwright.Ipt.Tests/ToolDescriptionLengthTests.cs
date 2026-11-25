using System.ComponentModel;
using System.Reflection;
using Bimwright.Ipt.Server;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

public sealed class ToolDescriptionLengthTests
{
    // Claude Code truncates a preloaded tool description at 4,096 characters.
    private const int MaxDescriptionLength = 4096;

    // inventor_send_code already exceeded the limit before INSTR-MOVE-TO-TOOL (it embeds SendCodeTaskGuide and
    // SendCodeHelperGuide). Bringing it under 4,096 needs its own reviewed change; until then it may not grow.
    private const int SendCodeDescriptionCeiling = 7537;

    private static List<(string Name, int Length)> Descriptions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods())
            .Select(m => (Tool: m.GetCustomAttribute<McpServerToolAttribute>(), Desc: m.GetCustomAttribute<DescriptionAttribute>()))
            .Where(x => x.Tool is not null)
            .Select(x => (x.Tool!.Name!, x.Desc?.Description.Length ?? 0))
            .ToList();

    // INSTR-MOVE-TO-TOOL: moving detail out of the instructions must not push a tool description past the limit.
    [Fact]
    public void Every_tool_description_except_send_code_fits_in_4096_characters_INSTR_MOVE_TO_TOOL()
    {
        var all = Descriptions();
        Assert.True(all.Count > 100, "expected the full tool catalog, found " + all.Count);
        var tooLong = all.Where(d => d.Name != "inventor_send_code" && d.Length > MaxDescriptionLength)
            .Select(d => d.Name + "=" + d.Length).ToList();
        Assert.True(tooLong.Count == 0, "Descriptions over 4096 characters: " + string.Join(", ", tooLong));
    }

    [Fact]
    public void Send_code_description_does_not_grow_past_its_known_ceiling_INSTR_MOVE_TO_TOOL()
    {
        var length = Descriptions().Single(d => d.Name == "inventor_send_code").Length;
        Assert.True(length <= SendCodeDescriptionCeiling,
            "inventor_send_code description is " + length + " characters; ceiling " + SendCodeDescriptionCeiling + ".");
    }
}
