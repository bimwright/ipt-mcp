namespace Bimwright.Ipt.Server;

/// <summary>
/// MCP server instructions. Kept at or under 2,048 UTF-8 bytes (INSTR-MAX-2KB) so every client reads the
/// whole text, including the safety block at the end. Order (INSTR-ORDER): product keywords, target -> health
/// -> report, short tool index, then <see cref="SafetyAndPermissions"/> last (FC-SRV-06). Detail that belongs
/// to one tool lives in that tool's description, not here (INSTR-MOVE-TO-TOOL).
/// </summary>
public static class ServerInstructions
{
    public const string Text =
        "ipt-mcp - Autodesk Inventor 2022-2027 MCP gateway (tools: inventor_*). " +
        "For .ipt parts, .iam assemblies, .idw/.dwg drawings: part, sketch, extrude, constraints, BOM, " +
        "interference, parameter, iproperty, mass, iLogic, iPart, sheet metal, STEP/STL/DXF export. Lengths are in mm. " +
        "Binds only when exactly one Inventor is live. On AMBIGUOUS_TARGET call inventor_list_available_targets, " +
        "ask user, then inventor_switch_target(\"pid:<n>\"). On TARGET_LOST confirm, switch explicitly. " +
        "Then call inventor_health(check_updates=true) once and follow its guidance. " +
        "Before first write, confirm active doc and call inventor_switch_target(\"pid:<n>\", verify=true). " +
        "Under --read-only (no switch_target) use --target pid:<n>. " +
        "HOST_BUSY/TARGET_BUSY: not started; resend when free. TIMEOUT: unknown outcome; inspect before retrying writes. " +
        "HOST_CRASHED: unsaved edits lost; do not resend, tell user. " +
        "When verified done, failed or cancelled, call inventor_report_task_result once (not chat-only). " +
        "Tool index: open/new/save/close docs, get_document_state, checkpoints, undo; create_sketch, draw_*; " +
        "revolve, fillet, hole, create_work_*, build_solids; place_occurrence, add_constraint; " +
        "list_bodies/features, probe_brep, measure_min_distance, get_mass_properties; " +
        "set_camera, capture_view/sheet; get/set_parameter/iproperty, excel; add_drawing_*; " +
        "ilogic_*, sheet_fabrication_*, export_*; baked; send_code if no typed tool fits. " +
        "inventor_send_code is enabled by default; --disable-send-code or --read-only removes it. " +
        SafetyAndPermissions;

    /// <summary>FC-SRV-06 / BW-PERM-001 R-C safety block; always the last part of <see cref="Text"/> (INSTR-SAFETY-BLOCK).</summary>
    public const string SafetyAndPermissions =
        "Safety & permissions: prefer read tools; preview (dry_run) destructive tools. " +
        "Before overwriting, deleting or discarding anything not created in this session, name the exact targets, " +
        "get the user's confirmation, then pass those literal paths or names. If a permission check or auto mode denies a call, do not repeat it through inventor_batch_execute, " +
        "a baked tool, iLogic or inventor_send_code (full local trust); report the denial to the user. " +
        "Work on files in the user's project folder and give full paths.";
}
