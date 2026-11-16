using System.Text.RegularExpressions;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Prompts;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

/// <summary>MCP prompts (rvt-mcp parity) only name tools that exist, and read-only prompts only read-only tools.</summary>
public sealed class InventorPromptsTests
{
    private static IEnumerable<string> Texts(bool readOnly)
    {
        var prompts = new InventorPrompts(new InventorMcpConfig { ReadOnly = readOnly });
        yield return prompts.GettingStarted();
        yield return prompts.Change("Make the flange 8 mm thick");
        yield return prompts.Change("");
        yield return prompts.Save();
    }

    private static IReadOnlyDictionary<string, ToolMetadata> Catalog() =>
        ToolCatalog.Build(Program.ResolveToolTypesForRegistration(new InventorMcpConfig { EnableSendCode = true, Toolsets = { "all" } }));

    private static IEnumerable<string> Tools(string text) =>
        Regex.Matches(text, @"inventor_[a-z_]+").Select(m => m.Value.Substring("inventor_".Length)).Distinct();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prompts_name_only_existing_tools(bool readOnly)
    {
        var catalog = Catalog();
        foreach (var text in Texts(readOnly))
            foreach (var tool in Tools(text))
                Assert.True(catalog.ContainsKey(tool), "unknown tool inventor_" + tool);
    }

    [Fact]
    public void Read_only_prompts_never_ask_for_a_write_tool()
    {
        var catalog = Catalog();
        foreach (var text in Texts(readOnly: true))
            foreach (var tool in Tools(text))
                Assert.True(catalog[tool].ReadOnly, "read-only prompt names write tool inventor_" + tool);
    }

    [Fact]
    public void The_change_prompt_confirms_before_writing_and_reports_after()
    {
        var text = new InventorPrompts(new InventorMcpConfig()).Change("Make the flange 8 mm thick");
        Assert.Contains("Make the flange 8 mm thick", text);
        Assert.Contains("verify=true", text);
        Assert.Contains("wait for my confirmation", text);
        Assert.Contains("inventor_report_task_result", text);
        Assert.Contains("Ask me what to change", new InventorPrompts(new InventorMcpConfig()).Change(" "));
    }
}
