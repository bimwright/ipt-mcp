using System;
using System.Linq;
using System.Reflection;
using Bimwright.Inventor.Server;
using Bimwright.Inventor.Server.Tools;
using Bimwright.Inventor.Shared.ToolBaker;
using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Tests;

/// <summary>
/// WS3-C platform-layer tests (server-side, no Inventor):
///   (1) `code` toolset OFF by default → CodeTools not registered; with --enable-send-code it IS;
///   (2) --read-only hides toolbaker_write (run/accept/dismiss) + export + code, but keeps read-only
///       toolbaker (list_baked_tools / list_bake_suggestions / create_bake_issue_draft);
///   (3) BakedToolDispatchAuthorizer denies the recursion set and allows a sample read command;
///   (4) BakeCompilerPolicy rejects a banned-API snippet, accepts a safe one.
/// </summary>
public sealed class PlatformToolsTests
{
    private static Type[] Types(InventorMcpConfig cfg)
        => Program.ResolveToolTypesForRegistration(cfg).ToArray();

    private static string[] ToolNames(InventorMcpConfig cfg)
        => Types(cfg)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                          .Cast<McpServerToolAttribute>().FirstOrDefault()?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();

    // ---- (1) send_code opt-in ----

    [Fact]
    public void CodeToolset_off_by_default()
    {
        var types = Types(new InventorMcpConfig());
        Assert.DoesNotContain(typeof(CodeTools), types);
        Assert.DoesNotContain("inventor_send_code", ToolNames(new InventorMcpConfig()));
    }

    [Fact]
    public void CodeToolset_registered_when_send_code_enabled_and_selected()
    {
        // `code` requires BOTH the EnableSendCode gate AND being in the requested toolset set
        // (DefaultOn excludes `code`, so "all" or an explicit `code` selection is needed).
        var cfg = new InventorMcpConfig { Toolsets = { "all" }, EnableSendCode = true };
        Assert.Contains(typeof(CodeTools), Types(cfg));
        Assert.Contains("inventor_send_code", ToolNames(cfg));
    }

    [Fact]
    public void CodeToolset_absent_when_selected_but_gate_off()
    {
        // Selecting `code` without the EnableSendCode gate must NOT register it.
        var cfg = new InventorMcpConfig { Toolsets = { "code" } };
        Assert.DoesNotContain(typeof(CodeTools), Types(cfg));
    }

    [Fact]
    public void EnableSendCode_flag_parsed_from_cli()
    {
        var cfg = InventorMcpConfig.Load(new[] { "--enable-send-code", "--toolsets", "all" });
        Assert.True(cfg.EnableSendCode);
        Assert.Contains(typeof(CodeTools), Types(cfg));
    }

    // ---- (2) read-only filtering ----

    [Fact]
    public void ReadOnly_hides_write_platform_and_export_keeps_readonly_toolbaker()
    {
        var cfg = new InventorMcpConfig
        {
            Toolsets = { "all" },
            ReadOnly = true,
            EnableSendCode = true, // even enabled, read-only must drop code
        };
        var types = Types(cfg);
        var names = ToolNames(cfg);

        // Hidden in read-only.
        Assert.DoesNotContain(typeof(CodeTools), types);
        Assert.DoesNotContain(typeof(ToolBakerWriteTools), types);
        Assert.DoesNotContain(typeof(ExportTools), types);
        Assert.DoesNotContain("inventor_run_baked_tool", names);
        Assert.DoesNotContain("inventor_accept_bake_suggestion", names);
        Assert.DoesNotContain("inventor_dismiss_bake_suggestion", names);
        Assert.DoesNotContain("inventor_capture_view", names);
        Assert.DoesNotContain("inventor_export_step", names);
        Assert.DoesNotContain("inventor_send_code", names);

        // Kept: read-only ToolBaker inspection/draft tools.
        Assert.Contains(typeof(ToolBakerTools), types);
        Assert.Contains("inventor_list_baked_tools", names);
        Assert.Contains("inventor_list_bake_suggestions", names);
        Assert.Contains("inventor_create_bake_issue_draft", names);
    }

    [Fact]
    public void Default_surface_exposes_export_and_both_toolbaker_classes_but_not_code()
    {
        var names = ToolNames(new InventorMcpConfig());
        // export + read + write toolbaker present by default
        Assert.Contains("inventor_capture_view", names);
        Assert.Contains("inventor_export_step", names);
        Assert.Contains("inventor_export_stl", names);
        Assert.Contains("inventor_export_dxf", names);
        Assert.Contains("inventor_list_baked_tools", names);
        Assert.Contains("inventor_run_baked_tool", names);
        // code is off by default
        Assert.DoesNotContain("inventor_send_code", names);
    }

    // ---- (3) baked-tool dispatch authorizer ----

    [Theory]
    [InlineData("send_code")]
    [InlineData("batch_execute")]
    [InlineData("run_baked_tool")]
    [InlineData("apply_bake")]
    [InlineData("accept_bake_suggestion")]
    [InlineData("dismiss_bake_suggestion")]
    [InlineData("list_baked_tools")]
    public void DispatchAuthorizer_denies_recursion_and_platform_commands(string command)
    {
        Assert.False(BakedToolDispatchAuthorizer.IsAllowed(command));
    }

    [Theory]
    [InlineData("list_parameters")]
    [InlineData("get_parameter")]
    [InlineData("get_mass_properties")]
    [InlineData("get_document_info")]
    public void DispatchAuthorizer_allows_readonly_query_commands(string command)
    {
        Assert.True(BakedToolDispatchAuthorizer.IsAllowed(command));
    }

    [Fact]
    public void DispatchAuthorizer_denies_unknown_and_empty()
    {
        Assert.False(BakedToolDispatchAuthorizer.IsAllowed("set_parameter")); // a write command, not allowed
        Assert.False(BakedToolDispatchAuthorizer.IsAllowed("extrude"));
        Assert.False(BakedToolDispatchAuthorizer.IsAllowed(""));
        Assert.False(BakedToolDispatchAuthorizer.IsAllowed("   "));
    }

    // ---- (4) bake compiler / banned-API source policy ----

    [Theory]
    [InlineData("System.IO.File.Delete(\"x\");")]
    [InlineData("System.IO.Directory.Delete(\"x\");")]
    [InlineData("System.Diagnostics.Process.Start(\"calc\");")]
    [InlineData("var v = System.Environment.GetEnvironmentVariable(\"PATH\");")]
    [InlineData("var c = new System.Net.Http.HttpClient();")]
    [InlineData("var s = new System.Net.Sockets.Socket(default, default, default);")]
    public void CompilerPolicy_rejects_banned_api_snippet(string snippet)
    {
        var result = BakeCompilerPolicy.ValidateSource(snippet);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void CompilerPolicy_accepts_safe_snippet()
    {
        var result = BakeCompilerPolicy.ValidateSource("var count = app.Documents.Count; Console.WriteLine(count);");
        Assert.True(result.Ok);
        Assert.Null(result.Error);
    }
}
