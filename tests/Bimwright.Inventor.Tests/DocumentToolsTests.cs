using System;
using System.Linq;
using System.Reflection;
using Bimwright.Inventor.Server;
using Bimwright.Inventor.Server.Tools;
using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Tests;

/// <summary>
/// WS3-A snapshot: <see cref="DocumentTools"/> exposes exactly the expected document/query MCP tool
/// names. <c>DocumentTools</c> is registered under both the <c>query</c> and <c>document</c> toolsets,
/// so it carries the read-only query methods AND the mutating document methods. Under <c>--read-only</c>
/// the whole class survives (because <c>query</c> is read-only), but the write tools are simply not a
/// separate type — so this test pins the per-type surface and the registration behaviour.
/// </summary>
public sealed class DocumentToolsTests
{
    private static readonly string[] ExpectedTools =
    {
        "inventor_health",
        "inventor_list_open_documents",
        "inventor_get_document_info",
        "inventor_new_part",
        "inventor_new_assembly",
        "inventor_open_document",
        "inventor_save_document",
        "inventor_close_document",
        "inventor_set_units",
        "inventor_set_material",
    };

    private static string[] ToolNamesOf(Type t)
        => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                          .Cast<McpServerToolAttribute>().FirstOrDefault()?.Name)
            .Where(n => n is not null).Select(n => n!).ToArray();

    private static string[] ToolNames(InventorMcpConfig cfg)
        => Program.ResolveToolTypesForRegistration(cfg)
            .SelectMany(ToolNamesOf).ToArray();

    [Fact]
    public void DocumentTools_exposes_exactly_the_expected_tools()
    {
        var names = ToolNamesOf(typeof(DocumentTools));
        Assert.Equal(ExpectedTools.Length, names.Length);
        foreach (var expected in ExpectedTools)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void Default_config_registers_all_document_tools()
    {
        var names = ToolNames(new InventorMcpConfig());
        foreach (var expected in ExpectedTools)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void ReadOnly_keeps_read_only_query_tools_but_DocumentTools_is_query_owned()
    {
        // DocumentTools is reachable via the read-only `query` toolset, so the read-only tools
        // (list_open_documents, get_document_info) remain registered under --read-only.
        var names = ToolNames(new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true });
        Assert.Contains("inventor_health", names);
        Assert.Contains("inventor_list_open_documents", names);
        Assert.Contains("inventor_get_document_info", names);
    }

    [Fact]
    public void Document_write_tools_disappear_when_only_a_non_document_toolset_is_selected()
    {
        // Selecting only `parameters` must not pull in DocumentTools.
        var names = ToolNames(new InventorMcpConfig { Toolsets = { "parameters" } });
        Assert.DoesNotContain("inventor_new_part", names);
        Assert.DoesNotContain("inventor_set_material", names);
    }
}
