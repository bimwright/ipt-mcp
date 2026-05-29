using System;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Server-side document/query MCP tool registration. Query tools must remain visible in read-only
/// mode, while mutating document tools must disappear by name.
/// </summary>
public sealed class DocumentToolsTests
{
    private static readonly string[] ExpectedQueryTools =
    {
        "inventor_health",
        "inventor_list_open_documents",
        "inventor_get_document_info",
    };

    private static readonly string[] ExpectedDocumentWriteTools =
    {
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
        Assert.Equal(ExpectedDocumentWriteTools.Length, names.Length);
        foreach (var expected in ExpectedDocumentWriteTools)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void QueryTools_exposes_exactly_the_expected_tools()
    {
        var names = ToolNamesOf(typeof(QueryTools));
        Assert.Equal(ExpectedQueryTools.Length, names.Length);
        foreach (var expected in ExpectedQueryTools)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void Default_config_registers_all_document_tools()
    {
        var names = ToolNames(new InventorMcpConfig());
        foreach (var expected in ExpectedQueryTools.Concat(ExpectedDocumentWriteTools))
            Assert.Contains(expected, names);
    }

    [Fact]
    public void ReadOnly_keeps_query_tools_and_drops_document_write_tool_names()
    {
        var names = ToolNames(new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true });
        foreach (var expected in ExpectedQueryTools)
            Assert.Contains(expected, names);
        foreach (var writeTool in ExpectedDocumentWriteTools)
            Assert.DoesNotContain(writeTool, names);
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
