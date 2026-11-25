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
        "ipt-mcp - MCP gateway for Autodesk Inventor 2022-2027. Tools are prefixed inventor_*. " +
        "Use whenever the user works with Inventor, .ipt part files, .iam assembly files, or .idw/.dwg drawings: " +
        "part, sketch, extrude, assembly constraints, BOM, interference, parameter, iproperty, " +
        "iLogic, iPart, sheet metal, drawings and STEP/STL/DXF export. Lengths are in mm. " +
        "The gateway binds only when exactly one Inventor is live. On AMBIGUOUS_TARGET call " +
        "inventor_list_available_targets, ask the user which model, then inventor_switch_target(\"pid:<n>\"). " +
        "On TARGET_LOST confirm the model and switch explicitly. Then call inventor_health(check_updates=true) once " +
        "and follow its description. Before the first write, confirm the active document with the user and call " +
        "inventor_switch_target(\"pid:<n>\", verify=true). Under --read-only (no switch_target) start with --target pid:<n>. " +
        "HOST_BUSY/TARGET_BUSY: not started; resend once Inventor is free. " +
        "TIMEOUT: outcome unknown; inspect before retrying a write. HOST_CRASHED: Inventor died, unsaved edits are " +
        "lost; do not resend, tell the user. When the job is verified complete, failed or cancelled, call " +
        "inventor_report_task_result once (not for chat-only answers; idle time is not completion). " +
        "Tool index: open/new/save/close documents, get_document_state, checkpoints; create_sketch, draw_*; " +
        "extrude, revolve, fillet, hole, build_solids; place_occurrence, add_constraint; " +
        "get/set_parameter, get/set_iproperty; export_*; ilogic_*, sheet_fabrication_*; " +
        "baked tools; send_code only when no typed tool fits. " +
        SafetyAndPermissions;

    /// <summary>FC-SRV-06 / R-C safety block; always the last part of <see cref="Text"/> (INSTR-SAFETY-BLOCK).</summary>
    public const string SafetyAndPermissions =
        "Safety & permissions: prefer read tools; preview (dry_run) destructive tools. " +
        "Before overwriting, deleting or discarding anything not created in this session, name the exact targets, " +
        "get the user's confirmation, then pass those literal paths or names. If a permission check or auto mode denies a call, do not repeat it through inventor_batch_execute, " +
        "a baked tool, iLogic or inventor_send_code (full local trust); report the denial to the user. " +
        "Work on files in the user's project folder and give full paths.";
}
