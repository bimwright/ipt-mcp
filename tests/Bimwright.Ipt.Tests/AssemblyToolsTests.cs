using System;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>Declared assembly surface: 9 write + 6 read-only tools, toolset wiring.</summary>
public sealed class AssemblyToolsTests
{
    private static string[] ToolNamesOf(Type toolType)
        => toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                          .Cast<McpServerToolAttribute>().FirstOrDefault()?.Name)
            .Where(n => n is not null).Select(n => n!).ToArray();

    [Fact]
    public void AssemblyTools_exposes_exactly_the_nine_write_tools()
    {
        var names = ToolNamesOf(typeof(AssemblyTools));
        Assert.Equal(new[] { "inventor_add_constraint", "inventor_create_design_view", "inventor_create_imate", "inventor_delete_occurrences",
                             "inventor_place_occurrence", "inventor_place_occurrences", "inventor_reset_appearance",
                             "inventor_set_appearance", "inventor_set_occurrence_state" },
                     names.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void AssemblyQueryTools_exposes_exactly_the_five_readonly_tools()
    {
        var names = ToolNamesOf(typeof(AssemblyQueryTools));
        Assert.Equal(new[] { "inventor_check_interference", "inventor_get_assembly_bom",
                             "inventor_list_constraints", "inventor_list_interfaces",
                             "inventor_list_occurrences", "inventor_measure_min_distance" },
                     names.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Assembly_toolsets_map_and_readonly_split()
    {
        var all = new InventorMcpConfig { Toolsets = { "all" }, EnableSendCode = true };
        var types = Program.ResolveToolTypesForRegistration(all);
        Assert.Contains(typeof(AssemblyTools), types);
        Assert.Contains(typeof(AssemblyQueryTools), types);

        var ro = new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true, EnableSendCode = true };
        var roTypes = Program.ResolveToolTypesForRegistration(ro);
        Assert.DoesNotContain(typeof(AssemblyTools), roTypes);
        Assert.Contains(typeof(AssemblyQueryTools), roTypes);
    }
}
