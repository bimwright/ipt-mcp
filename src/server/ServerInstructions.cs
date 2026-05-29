namespace Bimwright.Inventor.Server;

public static class ServerInstructions
{
    public const string Text =
        "inventor-mcp - MCP gateway for Autodesk Inventor 2022-2027. " +
        "Tools are prefixed inventor_*. Use whenever the user works with Inventor, " +
        ".ipt part files, .iam assembly files, or .idw/.dwg drawings. " +
        "Capabilities: create and open parts and assemblies; list open documents and get document info; " +
        "create and edit 2D sketches (lines, circles, rectangles, constraints) on a part; " +
        "build solid features from sketch profiles - extrude, revolve, fillet, chamfer, hole; " +
        "create work geometry such as a work plane, work axis, and work point; " +
        "read and write model parameters and user parameters; " +
        "read and write iProperties (iproperty: title, author, part number, description, custom properties); " +
        "compute mass properties (mass, volume, surface area, center of gravity, bounding box) and material; " +
        "export the model to STEP (step), STL (stl), and DXF (dxf, sketch or sheet-metal flat pattern). " +
        "Lengths are in millimeters (the Inventor API internal length unit is centimeters; the server converts). " +
        "Multi-instance: if more than one Inventor may be open, call inventor_list_available_targets " +
        "then inventor_switch_target to select the target session by year, pid, or descriptor id; " +
        "inventor_get_current_target reports the selected target. Versions are 4-digit years (2022..2027). " +
        "ToolBaker: inventor_list_baked_tools and inventor_run_baked_tool govern reusable baked tools; " +
        "read-only mode hides write tools but keeps query, target switching, and read-only baked tool inspection. " +
        "inventor_send_code is DISABLED unless the server is started with --enable-send-code " +
        "(or BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1) AND the add-in opts in via " +
        "BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1.";
}
