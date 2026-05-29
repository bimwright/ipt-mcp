using System;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// WS3-A snapshot: <see cref="ParameterTools"/> (toolset <c>parameters</c>) exposes exactly the four
/// parameter tools, the read-only ones survive default registration, and the whole write-capable
/// <c>parameters</c> toolset is dropped under <c>--read-only</c>.
/// </summary>
public sealed class ParameterToolsTests
{
    private static readonly string[] ExpectedTools =
    {
        "inventor_list_parameters",
        "inventor_get_parameter",
        "inventor_set_parameter",
        "inventor_create_parameter",
    };

    private static string[] ToolNamesOf(Type t)
        => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                          .Cast<McpServerToolAttribute>().FirstOrDefault()?.Name)
            .Where(n => n is not null).Select(n => n!).ToArray();

    private static string[] ToolNames(InventorMcpConfig cfg)
        => Program.ResolveToolTypesForRegistration(cfg).SelectMany(ToolNamesOf).ToArray();

    [Fact]
    public void ParameterTools_exposes_exactly_the_expected_tools()
    {
        var names = ToolNamesOf(typeof(ParameterTools));
        Assert.Equal(ExpectedTools.Length, names.Length);
        foreach (var expected in ExpectedTools)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void Default_config_registers_all_parameter_tools()
    {
        var names = ToolNames(new InventorMcpConfig());
        foreach (var expected in ExpectedTools)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void ReadOnly_drops_the_parameters_toolset_including_read_only_param_tools()
    {
        // `parameters` is a WriteCapable toolset, so the entire class (incl. the read-only
        // list/get tools) is hidden under --read-only — by design.
        var types = Program.ResolveToolTypesForRegistration(
            new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true }).ToArray();
        Assert.DoesNotContain(typeof(ParameterTools), types);

        var names = types.SelectMany(ToolNamesOf).ToArray();
        Assert.DoesNotContain("inventor_list_parameters", names);
        Assert.DoesNotContain("inventor_set_parameter", names);
    }
}
