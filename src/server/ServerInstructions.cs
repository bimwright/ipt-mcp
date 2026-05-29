namespace Bimwright.Inventor.Server;

public static class ServerInstructions
{
    public const string Text =
        "inventor-mcp - MCP gateway for Autodesk Inventor 2022-2027. " +
        "Tools are prefixed inventor_*. Work with parts (ipt), assemblies (iam), sketches, " +
        "extrude/revolve/fillet/chamfer features, parameters, iProperties, mass properties, " +
        "and export to STEP/STL/DXF. Lengths are in millimeters. " +
        "Multi-instance: if more than one Inventor may be open, call inventor_list_available_targets " +
        "then inventor_switch_target. Versions are 4-digit years (2022..2027). " +
        "inventor_send_code is DISABLED unless the server is started with --enable-send-code " +
        "(or BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1) AND the add-in opts in via " +
        "BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1.";
}
