using System;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// WS3-B golden snapshot: asserts <see cref="SketchTools"/> exposes exactly the 9 sketch
/// <c>inventor_*</c> tools and <see cref="FeatureTools"/> exactly the 6 feature tools, that every one
/// is a write tool (so all are dropped under <c>--read-only</c>), and that they register under the
/// <c>sketch</c>/<c>feature</c> toolsets. Handler bodies are type-checked by the inv25 build, not here.
/// </summary>
public sealed class SketchFeatureToolsTests
{
    private static readonly string[] ExpectedSketchTools =
    {
        "inventor_create_sketch",
        "inventor_project_geometry",
        "inventor_draw_line",
        "inventor_draw_circle",
        "inventor_draw_rectangle",
        "inventor_draw_arc",
        "inventor_add_sketch_dimension",
        "inventor_add_sketch_constraint",
        "inventor_close_sketch",
    };

    private static readonly string[] ExpectedFeatureTools =
    {
        "inventor_extrude",
        "inventor_revolve",
        "inventor_fillet",
        "inventor_chamfer",
        "inventor_create_work_plane",
        "inventor_create_work_axis",
        "inventor_hole",
        "inventor_circular_pattern",
        "inventor_rectangular_pattern",
    };

    private static string[] ToolNamesOf(Type toolType)
        => toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                          .Cast<McpServerToolAttribute>().FirstOrDefault()?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();

    private static string[] ToolNames(InventorMcpConfig cfg)
        => Program.ResolveToolTypesForRegistration(cfg)
            .SelectMany(ToolNamesOf)
            .ToArray();

    [Fact]
    public void SketchTools_exposes_exactly_the_nine_expected_tools()
    {
        var names = ToolNamesOf(typeof(SketchTools));
        Assert.Equal(9, names.Length);
        Assert.Equal(ExpectedSketchTools.OrderBy(x => x), names.OrderBy(x => x));
    }

    [Fact]
    public void FeatureTools_exposes_exactly_the_nine_expected_tools()
    {
        var names = ToolNamesOf(typeof(FeatureTools));
        Assert.Equal(9, names.Length);
        Assert.Equal(ExpectedFeatureTools.OrderBy(x => x), names.OrderBy(x => x));
    }

    [Fact]
    public void Sketch_and_feature_register_under_their_toolsets()
    {
        var sketch = ToolNames(new InventorMcpConfig { Toolsets = { "sketch" } });
        Assert.Equal(ExpectedSketchTools.OrderBy(x => x), sketch.OrderBy(x => x));

        var feature = ToolNames(new InventorMcpConfig { Toolsets = { "feature" } });
        Assert.Equal(ExpectedFeatureTools.OrderBy(x => x), feature.OrderBy(x => x));
    }

    [Fact]
    public void All_sketch_and_feature_tools_are_write_and_dropped_under_read_only()
    {
        // With everything enabled but read-only on, none of the 15 write tools should be exposed.
        var cfg = new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true };
        var names = ToolNames(cfg);

        foreach (var t in ExpectedSketchTools.Concat(ExpectedFeatureTools))
            Assert.DoesNotContain(t, names);
    }

    [Fact]
    public void Default_registration_includes_all_fifteen_write_tools()
    {
        var names = ToolNames(new InventorMcpConfig());
        foreach (var t in ExpectedSketchTools.Concat(ExpectedFeatureTools))
            Assert.Contains(t, names);
    }
}
