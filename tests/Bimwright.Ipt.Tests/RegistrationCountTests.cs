using System;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Phase 3 review-gate guard: locks the public tool count at exactly <b>59</b> MCP tools when
/// every toolset is enabled (<c>--toolsets all --enable-send-code</c>) and pins the read-only subset.
/// The 59 breaks down as: 3 meta + 36 functional (incl. <c>inventor_health</c>) + 1 send_code +
/// 6 ToolBaker + 13 assembly/feature/export additions = 59. The read-only registration keeps only meta + query (QueryTools) +
/// assembly_query (AssemblyQueryTools) + read-only ToolBaker, and drops every write/export/code/toolbaker_write type.
/// </summary>
public sealed class RegistrationCountTests
{
    private static Type[] Types(InventorMcpConfig cfg)
        => Program.ResolveToolTypesForRegistration(cfg).ToArray();

    /// <summary>Every MCP tool name (the <c>[McpServerTool(Name=...)]</c>) exposed under a config.</summary>
    private static string[] ToolNames(InventorMcpConfig cfg)
        => Types(cfg)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                          .Cast<McpServerToolAttribute>().FirstOrDefault()?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();

    private static InventorMcpConfig AllEnabled() => new InventorMcpConfig
    {
        Toolsets = { "all" },
        EnableSendCode = true,   // surface the `code` toolset
        EnableToolBaker = true,  // default, but explicit for clarity
    };

    [Fact]
    public void All_toolsets_with_send_code_register_exactly_59_tools()
    {
        var names = ToolNames(AllEnabled());

        // No duplicate MCP names may collide.
        var distinct = names.Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(distinct.Length, names.Length);

        Assert.Equal(59, names.Length);
    }

    [Fact]
    public void The_59_tools_match_the_frozen_surface()
    {
        var names = new HashSet<string>(ToolNames(AllEnabled()), StringComparer.Ordinal);

        var expected = new[]
        {
            // meta (3)
            "inventor_list_available_targets", "inventor_get_current_target", "inventor_switch_target",
            // core + document (10): health + 9 doc
            "inventor_health", "inventor_list_open_documents", "inventor_get_document_info",
            "inventor_new_part", "inventor_new_assembly", "inventor_open_document",
            "inventor_save_document", "inventor_close_document", "inventor_set_units", "inventor_set_material",
            // parameters (4)
            "inventor_list_parameters", "inventor_get_parameter", "inventor_set_parameter", "inventor_create_parameter",
            // properties (3)
            "inventor_get_iproperty", "inventor_set_iproperty", "inventor_get_mass_properties",
            // sketch (9)
            "inventor_create_sketch", "inventor_project_geometry", "inventor_draw_line", "inventor_draw_circle",
            "inventor_draw_rectangle", "inventor_draw_arc", "inventor_add_sketch_dimension",
            "inventor_add_sketch_constraint", "inventor_close_sketch",
            // feature (9)
            "inventor_extrude", "inventor_revolve", "inventor_fillet", "inventor_chamfer",
            "inventor_create_work_plane", "inventor_create_work_axis",
            "inventor_hole", "inventor_circular_pattern", "inventor_rectangular_pattern",
            // export (6)
            "inventor_capture_view", "inventor_export_step", "inventor_export_stl", "inventor_export_dxf",
            "inventor_view_fit", "inventor_set_view_orientation",
            // code (1)
            "inventor_send_code",
            // toolbaker (6)
            "inventor_list_baked_tools", "inventor_list_bake_suggestions", "inventor_create_bake_issue_draft",
            "inventor_run_baked_tool", "inventor_accept_bake_suggestion", "inventor_dismiss_bake_suggestion",
            // assembly (3 write)
            "inventor_place_occurrence", "inventor_add_constraint", "inventor_create_imate",
            // assembly_query (5 read-only)
            "inventor_list_interfaces", "inventor_check_interference", "inventor_measure_min_distance",
            "inventor_get_assembly_bom", "inventor_list_constraints",
        };

        Assert.Equal(59, expected.Length);
        foreach (var e in expected)
            Assert.True(names.Contains(e), $"missing expected tool: {e}");
        // and nothing extra beyond the 59 expected
        foreach (var n in names)
            Assert.True(expected.Contains(n), $"unexpected extra tool: {n}");
    }

    [Fact]
    public void Health_is_present_and_read_only_survivable()
    {
        // Present when all enabled.
        Assert.Contains("inventor_health", ToolNames(AllEnabled()));

        // Survives --read-only because it lives in QueryTools and the handler is read-only.
        var ro = new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true, EnableSendCode = true };
        Assert.Contains("inventor_health", ToolNames(ro));
    }

    [Fact]
    public void ReadOnly_registration_keeps_meta_query_assemblyquery_and_readonly_toolbaker_types()
    {
        var cfg = new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true, EnableSendCode = true };
        var types = Types(cfg);
        var names = ToolNames(cfg);

        // Kept: meta (MetaTools), query (QueryTools), assembly_query (AssemblyQueryTools), read-only toolbaker (ToolBakerTools).
        Assert.Contains(typeof(MetaTools), types);
        Assert.Contains(typeof(QueryTools), types);
        Assert.Contains(typeof(AssemblyQueryTools), types);
        Assert.Contains(typeof(ToolBakerTools), types);
        Assert.Equal(4, types.Length);

        // Dropped: every write/export/code/toolbaker_write owner.
        Assert.DoesNotContain(typeof(DocumentTools), types);
        Assert.DoesNotContain(typeof(ParameterTools), types);
        Assert.DoesNotContain(typeof(PropertyTools), types);
        Assert.DoesNotContain(typeof(SketchTools), types);
        Assert.DoesNotContain(typeof(FeatureTools), types);
        Assert.DoesNotContain(typeof(ExportTools), types);
        Assert.DoesNotContain(typeof(CodeTools), types);
        Assert.DoesNotContain(typeof(ToolBakerWriteTools), types);
        Assert.DoesNotContain(typeof(AssemblyTools), types);

        foreach (var writeTool in new[]
                 {
                     "inventor_new_part", "inventor_new_assembly", "inventor_open_document",
                     "inventor_save_document", "inventor_close_document", "inventor_set_units",
                     "inventor_set_material", "inventor_place_occurrence", "inventor_add_constraint",
                     "inventor_create_imate"
                 })
            Assert.DoesNotContain(writeTool, names);
    }

    [Fact]
    public void Default_config_registers_58_tools_without_send_code()
    {
        // Default (no --enable-send-code) drops the single `code` tool, leaving 58.
        var names = ToolNames(new InventorMcpConfig());
        Assert.False(names.Contains("inventor_send_code"), "send_code must be off by default");
        Assert.Equal(58, names.Length);
    }
}

