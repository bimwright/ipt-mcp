using System;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Locks the built surface at 101 all-enabled tools: the existing 88 plus 13 drawing tools.
/// Code-off exposes 97; read-only retains the drawing query and drops drawing writes.
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
    public void All_toolsets_with_send_code_register_exactly_101_tools()
    {
        var names = ToolNames(AllEnabled());

        // No duplicate MCP names may collide.
        var distinct = names.Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(distinct.Length, names.Length);

        Assert.Equal(101, names.Length);
    }

    [Fact]
    public void The_101_tools_match_the_frozen_surface()
    {
        var names = new HashSet<string>(ToolNames(AllEnabled()), StringComparer.Ordinal);

        var expected = new[]
        {
            // drawing (13)
            "inventor_get_drawing_info", "inventor_new_drawing", "inventor_add_sheet", "inventor_set_title_block",
            "inventor_add_drawing_view", "inventor_add_section_view", "inventor_edit_drawing_view",
            "inventor_add_drawing_dimension", "inventor_add_balloon", "inventor_export_drawing", "inventor_capture_sheet", "inventor_add_drawing_note", "inventor_add_drawing_table",
            // meta (3)
            "inventor_list_available_targets", "inventor_get_current_target", "inventor_switch_target",
            // query (7) + document (7)
            "inventor_report_task_result", "inventor_health", "inventor_list_open_documents", "inventor_get_document_info",
            "inventor_list_bodies", "inventor_list_features", "inventor_probe_brep",
            "inventor_new_part", "inventor_new_assembly", "inventor_open_document",
            "inventor_save_document", "inventor_close_document", "inventor_set_units", "inventor_set_material",
            "inventor_save_all", "inventor_open_documents", "inventor_close_documents",
            // parameters (4)
            "inventor_list_parameters", "inventor_get_parameter", "inventor_set_parameter", "inventor_create_parameter",
            // properties (4)
            "inventor_get_iproperty", "inventor_set_iproperty", "inventor_get_mass_properties", "inventor_list_iproperty_sets",
            // sketch (10)
            "inventor_create_sketch", "inventor_project_geometry", "inventor_draw_line", "inventor_draw_circle",
            "inventor_draw_rectangle", "inventor_draw_arc", "inventor_add_sketch_dimension",
            "inventor_add_sketch_constraint", "inventor_draw_text", "inventor_close_sketch",
            // feature (15)
            "inventor_extrude", "inventor_revolve", "inventor_fillet", "inventor_chamfer",
            "inventor_create_work_plane", "inventor_create_work_axis",
            "inventor_hole", "inventor_circular_pattern", "inventor_rectangular_pattern", "inventor_combine", "inventor_batch_execute",
            "inventor_loft", "inventor_sweep", "inventor_create_work_point", "inventor_create_bim_connector",
            "inventor_create_part",
            // export (9)
            "inventor_capture_view", "inventor_export_step", "inventor_export_stl", "inventor_export_dxf",
            "inventor_view_fit", "inventor_set_view_orientation", "inventor_set_camera", "inventor_export_sat",
            "inventor_derive_envelope", "inventor_set_view_state",
            // code (1)
            "inventor_send_code", "inventor_save_code_module", "inventor_list_code_modules", "inventor_delete_code_module",
            // toolbaker (6)
            "inventor_list_baked_tools", "inventor_list_bake_suggestions", "inventor_create_bake_issue_draft",
            "inventor_run_baked_tool", "inventor_accept_bake_suggestion", "inventor_dismiss_bake_suggestion",
            // assembly (3 write)
            "inventor_place_occurrence", "inventor_add_constraint", "inventor_create_imate",
            "inventor_place_occurrences", "inventor_delete_occurrences", "inventor_set_occurrence_state",
            "inventor_set_appearance", "inventor_reset_appearance",
            // assembly_query (5 read-only)
            "inventor_list_interfaces", "inventor_check_interference", "inventor_measure_min_distance",
            "inventor_get_assembly_bom", "inventor_list_constraints", "inventor_list_occurrences",
        };

        Assert.Equal(101, expected.Length);
        foreach (var e in expected)
            Assert.True(names.Contains(e), $"missing expected tool: {e}");
        // and nothing extra beyond the 101 expected
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
        Assert.Contains(typeof(DrawingQueryTools), types);
        Assert.DoesNotContain(typeof(DrawingTools), types);
        Assert.Equal(5, types.Length);

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
    public void Default_config_registers_97_tools_without_send_code()
    {
        // Default (no --enable-send-code) drops the 4 `code` tools (send_code + modules), leaving 97.
        var names = ToolNames(new InventorMcpConfig());
        Assert.False(names.Contains("inventor_send_code"), "send_code must be off by default");
        Assert.Equal(97, names.Length);
    }
}
