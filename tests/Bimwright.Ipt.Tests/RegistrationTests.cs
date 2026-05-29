using System;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Asserts the server's toolset→type registration map (Program.ResolveToolTypesForRegistration)
/// behaves per the Frozen Integration Contracts: code off by default, meta + toolbaker on,
/// read-only drops every WriteCapable toolset but keeps meta/query/toolbaker (and the
/// switch_target meta tool), and an explicit --toolsets selection registers only that set.
/// </summary>
public sealed class RegistrationTests
{
    private static Type[] Types(InventorMcpConfig cfg)
        => Program.ResolveToolTypesForRegistration(cfg).ToArray();

    /// <summary>All MCP tool names exposed under the given config.</summary>
    private static string[] ToolNames(InventorMcpConfig cfg)
        => Types(cfg)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                          .Cast<McpServerToolAttribute>().FirstOrDefault()?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();

    [Fact]
    public void Default_registration_excludes_code_toolset()
    {
        var cfg = new InventorMcpConfig();
        var types = Types(cfg);

        Assert.Contains(typeof(MetaTools), types);
        Assert.DoesNotContain(typeof(CodeTools), types);   // code off by default
        Assert.Contains(typeof(ToolBakerTools), types);
    }

    [Fact]
    public void Default_registration_loaded_from_empty_args_matches()
    {
        // Load(new string[0]) must agree with a default-constructed config.
        var types = Types(InventorMcpConfig.Load(Array.Empty<string>()));

        Assert.Contains(typeof(MetaTools), types);
        Assert.DoesNotContain(typeof(CodeTools), types);
    }

    [Fact]
    public void Query_and_document_toolsets_register_separate_tool_classes_once()
    {
        var types = Types(new InventorMcpConfig());
        Assert.Single(types, t => t == typeof(QueryTools));
        Assert.Single(types, t => t == typeof(DocumentTools));
    }

    [Fact]
    public void ReadOnly_removes_write_toolsets_but_keeps_meta_query_toolbaker()
    {
        var cfg = new InventorMcpConfig
        {
            Toolsets = { "all" },
            ReadOnly = true,
            EnableSendCode = true,   // even when enabled, read-only must still drop code
        };
        var types = Types(cfg);

        // Kept: meta (MetaTools), query (QueryTools), read-only toolbaker (ToolBakerTools).
        Assert.Contains(typeof(MetaTools), types);
        Assert.Contains(typeof(QueryTools), types);
        Assert.Contains(typeof(ToolBakerTools), types);

        // Dropped: every WriteCapable toolset's owner that has no read-only toolset.
        Assert.DoesNotContain(typeof(DocumentTools), types);
        Assert.DoesNotContain(typeof(CodeTools), types);
        Assert.DoesNotContain(typeof(ToolBakerWriteTools), types);
        Assert.DoesNotContain(typeof(ParameterTools), types);
        Assert.DoesNotContain(typeof(PropertyTools), types);
        Assert.DoesNotContain(typeof(SketchTools), types);
        Assert.DoesNotContain(typeof(FeatureTools), types);
        Assert.DoesNotContain(typeof(ExportTools), types);
    }

    [Fact]
    public void ReadOnly_keeps_switch_target_meta_tool()
    {
        var cfg = new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true };
        var names = ToolNames(cfg);

        Assert.Contains("inventor_switch_target", names);
        Assert.Contains("inventor_list_available_targets", names);
        Assert.Contains("inventor_get_current_target", names);
    }

    [Fact]
    public void Toolsets_sketch_registers_only_the_minimal_set()
    {
        var cfg = new InventorMcpConfig { Toolsets = { "sketch" } };
        var types = Types(cfg);

        // Exactly SketchTools — an explicit toolset selection does NOT auto-add meta.
        Assert.Contains(typeof(SketchTools), types);
        Assert.DoesNotContain(typeof(MetaTools), types);
        Assert.DoesNotContain(typeof(DocumentTools), types);
        Assert.DoesNotContain(typeof(FeatureTools), types);
        Assert.DoesNotContain(typeof(CodeTools), types);
        Assert.Single(types);
    }
}
