using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Write-capable document tools. Read-only document/query probes live in <see cref="QueryTools"/> so
/// <c>--read-only</c> can hide this entire toolset by type.
/// </summary>
[McpServerToolType]
[Toolset("document")]
public sealed class DocumentTools
{
    private readonly PluginClient _client;
    public DocumentTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_new_part", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Create a new part document (.ipt). Optional template path; omit to use the default standard part template.")]
    public Task<string> NewPart(string? template = null, CancellationToken ct = default)
        => Call("new_part", new JObject { ["template"] = template }, ct);

    [McpServerTool(Name = "inventor_new_assembly", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Create a new assembly document (.iam). Optional template path; omit to use the default standard assembly template.")]
    public Task<string> NewAssembly(string? template = null, CancellationToken ct = default)
        => Call("new_assembly", new JObject { ["template"] = template }, ct);

    [McpServerTool(Name = "inventor_open_document", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Open an existing Inventor document from a full file path and make it the active document. visible=false loads it without a window (for batch reads/edits via the document parameter of other tools). Runs silently by default (silent=true: Inventor answers its own prompts with their defaults instead of showing a blocking dialog).")]
    public Task<string> OpenDocument(string path, bool visible = true, bool silent = true, CancellationToken ct = default)
        => Call("open_document", new JObject { ["path"] = path, ["visible"] = visible, ["silent"] = silent }, ct);

    [McpServerTool(Name = "inventor_save_document", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Save a document: the active one, or document (full path or name of an open/loaded document), following the inventor_get_document_state policy, which this tool enforces. " +
                 "Without path it saves in place (Inventor keeps OldVersions): refused for a never-saved document (DOCUMENT_NEVER_SAVED), a template (TEMPLATE_SAVE_AS_REQUIRED), a read-only file (FILE_READ_ONLY) or a file missing on disk; saved=false with reason no_changes when nothing changed; an older-format file needs allow_format_upgrade=true after the user approved. " +
                 "With path it saves as a new local file of the document's type (.ipt/.iam/.idw/.dwg/.ipn) in an existing folder; an existing file is never overwritten (SAVE_AS_TARGET_EXISTS). Use a path the user gave; never choose one yourself. Refusals carry data.reason and save nothing. " +
                 "Runs silently by default (silent=true auto-answers prompts such as 'save referenced documents?' with their defaults so the call cannot hang on a hidden dialog).")]
    public Task<string> SaveDocument(string? path = null, string? document = null, bool silent = true,
        [Description("Allow an in-place save to convert an older-format file to this Inventor's format, only after the user approved (default false).")] bool allow_format_upgrade = false,
        CancellationToken ct = default)
        => Call("save_document", new JObject { ["path"] = path, ["document"] = document, ["silent"] = silent, ["allow_format_upgrade"] = allow_format_upgrade }, ct);

    [McpServerTool(Name = "inventor_close_document", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Close a document: the active one, or document (full path or name of an open document). save=true saves before closing; save=false (default) discards unsaved changes. Runs silently by default.")]
    public Task<string> CloseDocument(bool save = false, string? document = null, bool silent = true, CancellationToken ct = default)
        => Call("close_document", new JObject { ["save"] = save, ["document"] = document, ["silent"] = silent }, ct);

    [McpServerTool(Name = "inventor_set_units", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Set the active document's length unit. length_unit is a unit name such as mm, cm, m, in, or ft.")]
    public Task<string> SetUnits(string lengthUnit, CancellationToken ct = default)
        => Call("set_units", new JObject { ["length_unit"] = lengthUnit }, ct);

    [McpServerTool(Name = "inventor_set_material", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Assign a material to the active part document by material name (must exist in the document's material library). Optional document: full path or name of a document already open or loaded in Inventor (e.g. a part referenced by the open assembly) — never opened or activated; omit for the active document.")]
    public Task<string> SetMaterial(string materialName, string? document = null, CancellationToken ct = default)
        => Call("set_material", new JObject { ["material_name"] = materialName, ["document"] = document }, ct);

    [McpServerTool(Name = "inventor_save_all", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Update a root document (default active; root = path or name of an open document) and save it together with every dirty document it references, silently (no hidden 'Save' dialog can block the call). Reports each file that is not clean: saved | read_only | error, plus counts. update=false skips the rebuild; dry_run=true lists what would be saved.")]
    public Task<string> SaveAll(string? root = null, bool update = true, bool dry_run = false, bool silent = true, CancellationToken ct = default)
        => Call("save_all", new JObject { ["root"] = root, ["update"] = update, ["dry_run"] = dry_run, ["silent"] = silent }, ct);

    [McpServerTool(Name = "inventor_open_documents", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Open several documents in one call, silently. visible=false (default) loads them without windows so other tools can target them with their document parameter; visible=true opens windows. Reports opened | already_open | error per path (max 200).")]
    public Task<string> OpenDocuments(string[] paths, bool visible = false, bool silent = true, CancellationToken ct = default)
        => Call("open_documents", new JObject { ["paths"] = new JArray(paths), ["visible"] = visible, ["silent"] = silent }, ct);

    [McpServerTool(Name = "inventor_close_documents", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Close documents by path/name (documents=[...]) or every visible document (all=true; keep_active=true keeps the active one). save=true saves each first; save=false discards changes. Silent. Reports closed | saved_closed | error per document.")]
    public Task<string> CloseDocuments(string[]? documents = null, bool all = false, bool keep_active = true, bool save = false,
        bool silent = true, CancellationToken ct = default)
        => Call("close_documents", new JObject
        {
            ["documents"] = documents is null ? null : new JArray(documents), ["all"] = all, ["keep_active"] = keep_active,
            ["save"] = save, ["silent"] = silent,
        }, ct);

    [McpServerTool(Name = "inventor_save_checkpoint", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("With user approval, before risky work or at handover, copy the confirmed active local file into \"<name>.checkpoints\\\" next to it (rvt-mcp revit_save_checkpoint parity). If it has unsaved changes it is saved first, overwriting the working file; that save cannot be undone with inventor_undo (Inventor keeps OldVersions; a part/assembly save also saves dirty references). The copy is verified (size + SHA-256) and indexed. An assembly or drawing checkpoint holds only that file, not its referenced parts (warning REFERENCES_NOT_CAPTURED). Refused for unsaved, template or read-only documents. allow_format_upgrade=true only after the user approved converting an older-format file. A timeout may leave it running: check inventor_manage_checkpoints(action=\"list\") before retrying. Example: inventor_save_checkpoint(label=\"Before re-dimensioning flanges\", reason=\"User approved\", milestone=false).")]
    public async Task<string> SaveCheckpoint(
        [Description("Checkpoint label, 1-80 characters.")] string label,
        [Description("Optional reason, up to 500 characters.")] string? reason = null,
        [Description("Keep this checkpoint when pruning, not counted toward keep_last (default false).")] bool milestone = false,
        [Description("Allow a save that converts an older-format file to this Inventor's format, only after explicit user approval (default false).")] bool allow_format_upgrade = false,
        [Description("Seconds to wait, 1-900 (default 300).")] int timeout_seconds = 300,
        CancellationToken ct = default)
    {
        if (timeout_seconds < 1 || timeout_seconds > 900) return ToolResponse.Error("INVALID_ARGUMENT", "timeout_seconds must be between 1 and 900.");
        return await CallWithData("save_checkpoint", new JObject { ["label"] = label, ["reason"] = reason, ["milestone"] = milestone, ["allow_format_upgrade"] = allow_format_upgrade }, timeout_seconds, ct);
    }

    [McpServerTool(Name = "inventor_manage_checkpoints", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Checkpoints of the confirmed active local file (rvt-mcp revit_manage_checkpoints parity): action=list shows them; action=prune frees space and cannot be undone: run dry_run=true, show the plan to the user, then apply with dry_run=false and its plan_hash only after approval (milestones are kept); action=restore copies checkpoint_id to \"<name>-restored-<id>.<ext>\" next to the file and never overwrites the working file. Example: inventor_manage_checkpoints(action=\"prune\", keep_last=5, dry_run=true).")]
    public async Task<string> ManageCheckpoints(
        [Description("list, prune, or restore.")] string action,
        [Description("Non-milestone checkpoints to keep when pruning, 1-100 (default 5).")] int keep_last = 5,
        [Description("For prune, preview without deleting (default true).")] bool dry_run = true,
        [Description("plan_hash of the user-approved prune preview; required when dry_run=false.")] string? plan_hash = null,
        [Description("Checkpoint id to restore (from action=list).")] string? checkpoint_id = null,
        [Description("Seconds to wait, 1-900 (default 300).")] int timeout_seconds = 300,
        CancellationToken ct = default)
    {
        if (timeout_seconds < 1 || timeout_seconds > 900) return ToolResponse.Error("INVALID_ARGUMENT", "timeout_seconds must be between 1 and 900.");
        return await CallWithData("manage_checkpoints", new JObject { ["action"] = action, ["keep_last"] = keep_last, ["dry_run"] = dry_run, ["plan_hash"] = plan_hash, ["checkpoint_id"] = checkpoint_id }, timeout_seconds, ct);
    }

    /// <summary>Like <see cref="Call"/>, but a failure keeps the handler's data (saved_first, reason, partial prune results).</summary>
    private async Task<string> CallWithData(string command, JObject p, int timeoutSeconds, CancellationToken ct)
    {
        try { return ToolResponse.Serialize(await _client.SendAsync(command, p, ct, timeoutSeconds * 1000)); }
        catch (InventorGatewayException ex) { return ToolResponse.Error(ex); }
    }

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            // Keep the handler's data (e.g. save_document data.reason and mode).
            return ToolResponse.Error(ex);
        }
    }
}
