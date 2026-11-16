namespace Bimwright.Ipt.Server;

public static class ServerInstructions
{
    public const string Text =
        "ipt-mcp - MCP gateway for Autodesk Inventor 2022-2027. " +
        "Tools are prefixed inventor_*. Use whenever the user works with Inventor, " +
        ".ipt part files, .iam assembly files, or .idw/.dwg drawings. " +
        "At the start of each Inventor session, select the intended target if needed, then call inventor_health(check_updates=true) once " +
        "to verify host connectivity, document/modal/STA state and the running MCP server/add-in versions and build identities. " +
        "If network access is unavailable or disallowed, use the offline default and keep the update status not_checked/unknown. " +
        "Later routine health probes stay offline by default. Inspect both update.components, including release_mismatch and unknown. " +
        "When update.notification is present, report its code/message and follow agent_action: propose the release ZIP only if " +
        "bundle_update_proposal_allowed=true and update_guidance.mode=prepare_update; other modes contain only check/review steps. " +
        "UPDATE_CHECK_UNAVAILABLE reports an external lookup_reason, not a runtime fault or request for installation authorization. " +
        "Report LOCAL_VERSION_AHEAD neutrally; preserve protected_components and explain protected_component_reasons. " +
        "unobserved_components need connection/runtime observation, not a corruption warning. " +
        "Read lookup_reason, protection/unobserved fields and the full message before summarizing; notification.components is only the primary affected set. " +
        "comparison_reason=release_revision_unavailable reports missing external release evidence, even with RELEASE_IDENTITY_UNVERIFIED; stop the update workflow, not otherwise healthy Inventor work. " +
        "A review/identity warning does not mean a new version exists. Installation authorization applies only to a permitted update proposal. " +
        "Never claim not_checked/unknown is up to date, recommend a downgrade or automatically replace a development build. " +
        "A response with host_connected=false or ok=false is not a successful host connection. " +
        "Version/revision alignment is observed identity, not proof of compatibility or publisher trust. " +
        "The optional release check reports server and loaded plugin separately against one release; inspect both components. " +
        "After an upgrade/reconnect, verify fresh initialize serverInfo and tools/list (health runtime metadata, schema and annotations) " +
        "against health.client_verification on that same connection. Only the client can verify its cache refresh; unavailable client evidence stays unverified. " +
        "Capabilities: create and open parts and assemblies; list open documents and get document info; " +
        "create and edit 2D sketches (lines, circles, rectangles, constraints) on a part; " +
        "build solid features from sketch profiles - extrude, revolve, fillet, chamfer, hole, combine, circular_pattern, rectangular_pattern, batch_execute; " +
        "create work geometry such as a work plane and work axis; " +
        "place components (IPT/IAM) in assembly, constrain them (mate/flush/insert/angle) using named refs/proxies, create iMates on parts; " +
        "query assembly relationships, BOM, degrees of freedom, interference (clash analysis), and min distance; " +
        "inspect part bodies, features, and B-rep port mouths (list_bodies, list_features, probe_brep); " +
        "zoom-fit, orient, and position camera views (eye/target/up/extents) and capture view images; " +
        "read and write model parameters and user parameters; " +
        "read and write iProperties (iproperty: title, author, part number, description, custom properties); " +
        "compute mass properties (mass, volume, surface area, center of gravity, bounding box) and material; " +
        "export the model to STEP (step), STL (stl), and DXF (dxf, sketch or sheet fabrication flat pattern). " +
        "Lengths are in millimeters (the Inventor API internal length unit is centimeters; the server converts). " +
        "Multi-instance: the gateway binds automatically only when exactly one Inventor is live. With several, calls return " +
        "AMBIGUOUS_TARGET with candidates; call inventor_list_available_targets, ask the user which model if unclear, then " +
        "inventor_switch_target with pid:<n> or id:<target_id>. The selection is never replaced automatically: TARGET_LOST " +
        "means it exited, so confirm the model and switch explicitly. inventor_get_current_target reports the selected target. " +
        "Before the first write, check the active document with the user and call inventor_switch_target(target=\"pid:<n>\", verify=true); " +
        "writes return TARGET_UNCONFIRMED until then. DOCUMENT_CHANGED or DOCUMENT_UNCONFIRMED: the write was not started because " +
        "another document is active; confirm the intended document again, or pass document=<path or name>. Opening or creating a document " +
        "moves the confirmation to it. PAIRING_REQUIRED: update the server and add-in together; reads still work. " +
        "HOST_CRASHED means Inventor died inside that call: edits since the last save are lost; do not resend it, tell the user " +
        "and confirm before switching to another Inventor. " +
        "HOST_BUSY means Inventor could not start the command (a modal dialog waits for the user, or earlier work kept it busy): " +
        "it was not started and will not run later; ask the user to answer the dialog, check inventor_health host_busy, then send it again. " +
        "TIMEOUT means the command started and its outcome is unknown: inspect before retrying a write. " +
        "TARGET_BUSY means the add-in did not accept the connection (nothing was sent): wait for the long call to finish, then retry. " +
        "Documents: call inventor_get_document_state before saving and follow its policy (never choose a save path yourself; ask before a save that migrates the file format); inventor_save_document enforces it: refusals carry data.reason and save nothing, and Save-As never overwrites a file. " +
        "Checkpoints: inventor_save_checkpoint only with user approval before risky work; prune or restore with inventor_manage_checkpoints (prune needs the previewed plan_hash). " +
        "Versions are 4-digit years (2022..2027). " +
        "ToolBaker: inventor_list_baked_tools and inventor_run_baked_tool govern reusable baked tools; " +
        "read-only mode hides write tools and inventor_switch_target (use --target pid:<n> at startup) but keeps query, list/get target, and read-only baked tool inspection. " +
        "Activity toasts group tool calls; they do not indicate that a whole job is finished. " +
        "If inventor_report_task_result is available, call it once when the requested job is verified complete, failed or cancelled, " +
        "with a unique agent/job task_id and a truthful concise summary. Never infer success from idle time or a single successful tool. " +
        SendCodeTaskGuide.Text +
        "inventor_send_code is enabled by default. Use --disable-send-code to disable it; " +
        "--read-only and toolset filters always hide code execution. The add-in requires no extra opt-in; " +
        "BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1 is an optional host-side kill switch.";
}
