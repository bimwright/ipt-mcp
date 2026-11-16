using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Server.Prompts;

/// <summary>
/// MCP prompts (rvt-mcp RevitPrompts parity): starting points a user picks in the client. Always registered; a prompt
/// costs no model context until it is used. Read-only gateways get read-only wording, since switch_target and every
/// write tool are hidden there.
/// </summary>
[McpServerPromptType]
public sealed class InventorPrompts
{
    private readonly InventorMcpConfig _config;
    public InventorPrompts(InventorMcpConfig config) => _config = config;

    [McpServerPrompt(Name = "inventor_getting_started"),
     Description("Read-only orientation for the connected Inventor: target, versions, open documents and their save state. Safe first command.")]
    public string GettingStarted() =>
        "Orient yourself in Inventor without changing anything.\n\n"
        + "1. Call inventor_list_available_targets. If several Inventor sessions run, ask me which one to use.\n"
        + (_config.ReadOnly
            ? "2. This gateway is read-only: the session comes from --target, or the only running Inventor. Call inventor_get_current_target.\n"
            : "2. Select it with inventor_switch_target(target=\"pid:<n>\", verify=true) and tell me which document it confirmed.\n")
        + "3. Call inventor_health(check_updates=true) once and report the server and add-in versions and any update notice.\n"
        + "4. Call inventor_list_open_documents, then inventor_get_document_state for the active document: mode, unsaved changes, dirty references and whether saving would convert an older format.\n"
        + "5. Summarize what is open and what I should know before working. Do not open, save or change anything.";

    [McpServerPrompt(Name = "inventor_change"),
     Description("Make a requested model change safely: survey, agree the smallest scope, confirm, optionally checkpoint, change, read back and report. Read-only gateways survey and propose only.")]
    public string Change([Description("The requested change, with the part, assembly or drawing and the reason when known")] string change)
    {
        var request = string.IsNullOrWhiteSpace(change) ? "Not specified. Ask me what to change before doing anything." : change;
        if (_config.ReadOnly)
            return "This gateway is read-only. Survey and propose only; do not write.\n\n"
                + $"Requested change: {request}\n\n"
                + "1. Read the affected document with the query tools (inventor_get_document_info, inventor_list_parameters, inventor_list_features, inventor_get_assembly_bom as relevant).\n"
                + "2. Propose the smallest concrete change: which document, which parameters or features, which values, and what else depends on them.\n"
                + "3. Say that a gateway without --read-only is needed to make it.";
        return $"Requested change: {request}\n\n"
            + "1. Confirm the target: inventor_switch_target(target=\"pid:<n>\", verify=true). Check with me that the confirmed document is the one to change.\n"
            + "2. Survey before writing: read the parameters, features or occurrences involved and what depends on them. Call inventor_get_document_state.\n"
            + "3. Propose the smallest concrete change (document, items, old and new values) and wait for my confirmation.\n"
            + "4. If the change is risky or hard to undo and the document is a saved local file, offer inventor_save_checkpoint first; create one only if I agree.\n"
            + "5. Make the change with the dedicated tools; use inventor_send_code only when no tool covers it. Never retry a timed-out write before checking its outcome.\n"
            + "6. Read back the changed values or geometry and compare them with the proposal.\n"
            + "7. Do not save unless I ask. When saving, follow inventor_get_document_state; inventor_save_document refuses what the policy forbids.\n"
            + "8. Call inventor_report_task_result once with the outcome, a one-line summary and a body listing what changed and what to check.";
    }

    [McpServerPrompt(Name = "inventor_save"),
     Description("Save the active document following its save policy: check state, ask before format conversion or a new path, save, verify.")]
    public string Save() =>
        _config.ReadOnly
            ? "This gateway is read-only and cannot save. Call inventor_get_document_state and tell me what would be saved and which rules apply."
            : "Save the active Inventor document safely.\n\n"
              + "1. Call inventor_get_document_state and read mode, is_modified, dirty_references, needs_migrating and policy.rules.\n"
              + "2. Never-saved document or template: ask me for a new file path; never choose one yourself. Read-only file: stop and tell me.\n"
              + "3. If needs_migrating is true, tell me the file will be converted from the older Inventor format and that older versions cannot open it; pass allow_format_upgrade=true only after I agree.\n"
              + "4. For an assembly, say which dirty references will be saved with it (inventor_save_all saves them all; a drawing save does not save its models).\n"
              + "5. Save with inventor_save_document (or inventor_save_all). If it refuses, report data.reason; do not work around it.\n"
              + "6. Call inventor_get_document_state again and confirm is_modified is false.";
}
