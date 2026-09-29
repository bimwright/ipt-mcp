using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToolActivityTests
{
    [Theory]
    [InlineData("extrude", false, ToolActivityKind.Write)]
    [InlineData("get_parameter", true, ToolActivityKind.Read)]
    [InlineData("frobnicate", null, ToolActivityKind.Read)]   // unknown command: nothing was changed
    public void Classify_uses_the_handler_read_only_flag(string command, bool? isReadOnly, ToolActivityKind expected)
        => Assert.Equal(expected, ToolActivityClassifier.Classify(command, isReadOnly));

    [Theory]
    [InlineData("send_code", ToolActivityKind.Write, true, "MCP · Script")]
    [InlineData("batch_execute", ToolActivityKind.Write, true, "MCP · Script")]
    [InlineData("run_baked_tool", ToolActivityKind.Write, true, "MCP · Script")]
    [InlineData("apply_bake", ToolActivityKind.Write, true, "MCP · Script")]
    [InlineData("capture_view", ToolActivityKind.Read, true, "MCP · Snapshot")]
    [InlineData("export_step", ToolActivityKind.Write, true, "MCP · Export")]
    [InlineData("extrude", ToolActivityKind.Write, true, "MCP · Modified")]
    [InlineData("list_parameters", ToolActivityKind.Read, true, "MCP · Query")]
    [InlineData("extrude", ToolActivityKind.Write, false, "MCP · Failed")]
    [InlineData(null, ToolActivityKind.Read, true, "MCP · Query")]
    public void Category(string? command, ToolActivityKind kind, bool success, string expected)
        => Assert.Equal(expected, ToolActivityClassifier.Category(command, kind, success));

    [Theory]
    [InlineData("export_step", "Export STEP")]
    [InlineData("get_iproperty", "Get iProperty")]
    [InlineData("list_iproperty_sets", "List iProperty Sets")]
    [InlineData("create_imate", "Create iMate")]
    [InlineData("create_bim_connector", "Create BIM Connector")]
    [InlineData("get_assembly_bom", "Get Assembly BOM")]
    [InlineData("send_code", "Send Code")]
    [InlineData("export_dxf", "Export DXF")]
    [InlineData("", "Unknown command")]
    [InlineData(null, "Unknown command")]
    [InlineData("___", "___")]
    public void Format(string? command, string expected)
        => Assert.Equal(expected, ToolNameFormatter.Format(command));
}
