using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public class DocumentStatePolicyTests
{
    private static readonly string?[] Templates = { @"C:\Users\Public\Documents\Autodesk\Inventor 2027\Templates", null, "" };

    [Theory]
    [InlineData(null, null, null, "unsaved")]
    [InlineData("", false, true, "unsaved")]
    [InlineData(@"D:\Work\Part1.ipt", false, true, "local_file")]
    [InlineData(@"D:\Work\Part1.ipt", null, null, "local_file")]
    [InlineData(@"D:\Work\Part1.ipt", true, true, "read_only")]
    [InlineData(@"D:\Work\Part1.ipt", false, false, "read_only")]
    [InlineData(@"C:\Users\Public\Documents\Autodesk\Inventor 2027\Templates\en-US\Metric\Standard (mm).ipt", false, true, "template")]
    [InlineData(@"C:\Users\Public\Documents\Autodesk\Inventor 2027\Templates2\x.ipt", false, true, "local_file")]
    public void Mode_classifies_the_document(string? path, bool? readOnly, bool? modifiable, string expected)
        => Assert.Equal(expected, DocumentStatePolicy.Mode(path, readOnly, modifiable, Templates));

    [Fact]
    public void Template_wins_over_read_only_so_the_agent_is_told_not_to_overwrite_it()
        => Assert.Equal("template", DocumentStatePolicy.Mode(Templates[0] + @"\x.idw", true, false, Templates));

    [Theory]
    [InlineData("local_file", new[] { "inventor_get_document_state", "inventor_save_document", "inventor_save_all", "inventor_close_document" })]
    [InlineData("unsaved", new[] { "inventor_get_document_state", "inventor_save_document" })]
    [InlineData("template", new[] { "inventor_get_document_state", "inventor_save_document" })]
    [InlineData("read_only", new[] { "inventor_get_document_state" })]
    public void Policy_lists_the_allowed_tools_per_mode(string mode, string[] allowed)
    {
        var policy = DocumentStatePolicy.Policy(mode, isDrawing: false, needsMigrating: false, "2027", 2027);
        Assert.Equal(mode, (string?)policy["mode"]);
        Assert.Equal(allowed, policy["allowed"]!.Values<string>());
        Assert.NotEmpty(policy["rules"]!);
    }

    [Fact]
    public void Unsaved_and_template_never_let_the_agent_choose_a_path()
    {
        foreach (var mode in new[] { "unsaved", "template" })
            Assert.Contains(DocumentStatePolicy.Policy(mode, false, false, null, 2027)["rules"]!.Values<string>(),
                r => r!.Contains("path the user gave") && r.Contains("never choose the path yourself"));
    }

    [Fact]
    public void Drawing_and_model_rules_differ_on_dirty_references()
    {
        var drawing = string.Join(" ", DocumentStatePolicy.Policy("local_file", true, false, "2027", 2027)["rules"]!.Values<string>());
        var model = string.Join(" ", DocumentStatePolicy.Policy("local_file", false, false, "2027", 2027)["rules"]!.Values<string>());
        Assert.Contains("does not save its dirty referenced models", drawing);
        Assert.Contains("also saves its dirty referenced documents", model);
    }

    [Fact]
    public void Migration_adds_an_ask_first_rule_except_for_read_only()
    {
        var rules = DocumentStatePolicy.Policy("local_file", false, true, "2024", 2027)["rules"]!.Values<string>();
        Assert.Contains(rules, r => r!.Contains("Inventor 2027 format") && r.Contains("(2024)") && r.Contains("Ask the user before saving"));
        Assert.DoesNotContain(DocumentStatePolicy.Policy("read_only", false, true, "2024", 2027)["rules"]!.Values<string>(),
            r => r!.Contains("format"));
    }

    [Fact]
    public void Unknown_mode_is_rejected()
        => Assert.Throws<ArgumentException>(() => DocumentStatePolicy.Policy("cloud", false, false, null, 2027));
}
