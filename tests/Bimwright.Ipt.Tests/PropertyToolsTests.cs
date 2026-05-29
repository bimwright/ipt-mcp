using System;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// WS3-A snapshot: <see cref="PropertyTools"/> (toolset <c>properties</c>) exposes exactly the iProperty
/// + mass tools, all are registered by default, and the write-capable <c>properties</c> toolset is
/// dropped under <c>--read-only</c>.
/// </summary>
public sealed class PropertyToolsTests
{
    private static readonly string[] ExpectedTools =
    {
        "inventor_get_iproperty",
        "inventor_set_iproperty",
        "inventor_get_mass_properties",
    };

    private static string[] ToolNamesOf(Type t)
        => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                          .Cast<McpServerToolAttribute>().FirstOrDefault()?.Name)
            .Where(n => n is not null).Select(n => n!).ToArray();

    private static string[] ToolNames(InventorMcpConfig cfg)
        => Program.ResolveToolTypesForRegistration(cfg).SelectMany(ToolNamesOf).ToArray();

    [Fact]
    public void PropertyTools_exposes_exactly_the_expected_tools()
    {
        var names = ToolNamesOf(typeof(PropertyTools));
        Assert.Equal(ExpectedTools.Length, names.Length);
        foreach (var expected in ExpectedTools)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void Default_config_registers_all_property_tools()
    {
        var names = ToolNames(new InventorMcpConfig());
        foreach (var expected in ExpectedTools)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void ReadOnly_drops_the_properties_toolset()
    {
        var types = Program.ResolveToolTypesForRegistration(
            new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true }).ToArray();
        Assert.DoesNotContain(typeof(PropertyTools), types);

        var names = types.SelectMany(ToolNamesOf).ToArray();
        Assert.DoesNotContain("inventor_get_iproperty", names);
        Assert.DoesNotContain("inventor_get_mass_properties", names);
    }
}
