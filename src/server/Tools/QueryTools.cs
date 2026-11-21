using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Read-only target/document probes. This class is registered by the <c>query</c> toolset and remains
/// visible in <c>--read-only</c> mode.
/// </summary>
[McpServerToolType]
[Toolset("query")]
public sealed class QueryTools
{
    private readonly PluginClient _client;
    private readonly ReleaseUpdateCheck _updates;
    public QueryTools(PluginClient client) : this(client, new ReleaseUpdateCheck()) { }
    internal QueryTools(PluginClient client, ReleaseUpdateCheck updates)
    { _client = client; _updates = updates; }

    [McpServerTool(Name = "inventor_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true),
     Description("At session start select the intended target, then call inventor_health(check_updates=true) once. Routine calls stay offline by default; not_checked/unknown never means up to date. Inspect update.components and report update.notification code/message according to agent_action. UPDATE_CHECK_UNAVAILABLE describes the external lookup_reason, not a local identity fault; no installation authorization is requested. LOCAL_VERSION_AHEAD is informational. Preserve protected_components and explain protected_component_reasons; unobserved_components require connection/runtime observation, not a warning about a known loaded build. Read lookup_reason, protection/unobserved fields and the full message before summarizing; notification.components is only the primary affected set. comparison_reason=release_revision_unavailable reports missing external release evidence even with RELEASE_IDENTITY_UNVERIFIED; stop the update workflow, not otherwise healthy Inventor work. Follow update_guidance.mode: only prepare_update, with bundle_update_proposal_allowed=true, contains installation steps and needs installation authorization (reuse existing permission). Other modes contain only check/review steps. No add-in returns server identity with ok:false and host_connected:false. Matching revisions do not verify artifact bytes, compatibility or publisher trust; never downgrade an ahead_of_release component or automatically replace a development build. After reconnect, compare fresh initialize serverInfo and tools/list runtime metadata/schema/annotations with client_verification. Read-only; never downloads packages, installs, saves documents or restarts processes.")]
    public async Task<string> Health(bool check_updates = false, CancellationToken ct = default)
    {
        JObject data;
        var connected = false;
        try
        {
            data = await _client.SendAsync("health", new JObject(), ct) as JObject
                ?? throw new InventorGatewayException(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.API_ERROR,
                    "The add-in returned an invalid health response.");
            connected = true;
        }
        catch (InventorGatewayException ex)
        {
            data = JObject.Parse(ToolResponse.Error(ex));
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException or Newtonsoft.Json.JsonException)
        {
            data = JObject.Parse(ToolResponse.Error(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.TARGET_UNAVAILABLE,
                "Health probe failed: " + ex.Message));
        }
        var gateway = _client.GatewayIdentity;
        var plugin = connected ? data["setup_plugin"] as JObject : null;
        HealthRuntimeInfo.Enrich(data, gateway, connected);
        data["update"] = check_updates
            ? await _updates.CheckAsync(gateway, plugin, ct)
            : ReleaseUpdateCheck.NotChecked(gateway, plugin);
        data["update_guidance"] = HealthRuntimeInfo.UpdateGuidance((JObject)data["update"]!);
        return ToolResponse.Serialize(data);
    }

    [McpServerTool(Name = "inventor_report_task_result", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Report an agent task's final outcome to the user: a summary toast in Inventor and a report saved under %LOCALAPPDATA%\\Bimwright\\ipt-mcp\\reports (read back with inventor_get_report_history). " +
                 "Call once after verifying the whole requested job, when it changed a model or found something the user should check; never after each tool, merely because tools went idle, or for a chat-only answer. Responses of model writes carry _report until you do. " +
                 "task_id must be unique per agent/job (1-80 chars; resending it from this session replaces that report); outcome is completed, failed or cancelled; summary is a truthful single-line result (1-120 chars); body is optional Markdown detail, max 32 KiB UTF-8. " +
                 "The report records the write tools this session ran since the last report. Saved even when Inventor is not reachable (toast_shown=false). Agent-reported, not independently verified. Does not change the model.")]
    public async Task<string> ReportTaskResult(
        [Description("Unique per agent/job, single line, 1-80 characters.")] string task_id,
        [Description("completed, failed or cancelled.")] string outcome,
        [Description("One truthful line, at most 120 characters; details go in body.")] string summary,
        [Description("Optional Markdown detail (what changed, what to check), max 32 KiB UTF-8.")] string? body = null,
        CancellationToken ct = default)
    {
        var invalid = Bimwright.Ipt.Shared.Contracts.ReportLimits.Validate(task_id, outcome, summary, body);
        if (invalid != null) return ToolResponse.Error(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.INVALID_ARGUMENT, invalid);

        JObject? card = null;
        InventorGatewayException? cardError = null;
        try { card = await _client.SendAsync("report_task_result", new JObject { ["task_id"] = task_id, ["outcome"] = outcome, ["summary"] = summary }, ct) as JObject; }
        catch (InventorGatewayException ex) { cardError = ex; }

        var (calls, truncated) = _client.Reports.TakeCalls();
        var report = new JObject
        {
            ["schema"] = 1,
            ["task_id"] = task_id.Trim(), ["outcome"] = outcome.Trim(), ["summary"] = summary.Trim(), ["body"] = body,
            ["agent_reported"] = true,
            ["source"] = new JObject { ["server_session"] = ServerLogger.SessionId, ["target_id"] = _client.SelectedTargetId },
            ["calls"] = calls, ["calls_truncated"] = truncated,
        };
        string? saveError = null;
        (string Id, bool Replaced, string Path) saved = default;
        try { saved = _client.ReportStore.Save(report); }
        catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException) { saveError = ex.Message; }

        if (saveError != null && cardError != null)
            return ToolResponse.Error(cardError.Code, cardError.Message + " The report was not saved either: " + saveError);
        var result = card ?? new JObject { ["task_id"] = task_id.Trim(), ["outcome"] = outcome.Trim(), ["summary"] = summary.Trim(), ["agent_reported"] = true };
        result["report_saved"] = saveError == null;
        if (saveError == null)
        {
            result["report_id"] = saved.Id;
            result["replaced"] = saved.Replaced;
            result["call_count"] = (report["calls"] as JArray)?.Count ?? 0;
        }
        else result["save_error"] = saveError;
        if (cardError != null)
        {
            result["toast_shown"] = false;
            result["toast_error"] = new JObject { ["code"] = cardError.Code, ["message"] = cardError.Message };
        }
        return ToolResponse.Serialize(result);
    }

    [McpServerTool(Name = "inventor_get_report_history", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Read saved agent reports (inventor_report_task_result) from %LOCALAPPDATA%\\Bimwright\\ipt-mcp\\reports, newest first. Without report_id: a page of summaries (offset >= 0, limit 1-50, default 10) and the total. " +
                 "With report_id (32 hex characters): that report in full, with its body and the write tools recorded since the report before it. Read-only local data; no Inventor connection required. Example: inventor_get_report_history(limit=5).")]
    public string GetReportHistory(string? report_id = null, int offset = 0, int limit = 10)
    {
        if (!string.IsNullOrEmpty(report_id))
        {
            if (!Reports.ReportStore.IsReportId(report_id))
                return ToolResponse.Error(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.INVALID_ARGUMENT, "report_id must be 32 hexadecimal characters.");
            return _client.ReportStore.Get(report_id) is { } report
                ? ToolResponse.Serialize(report)
                : ToolResponse.Error(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.INVALID_ARGUMENT, "No saved report has this report_id (the newest 500 are kept).");
        }
        if (offset < 0 || limit is < 1 or > 50)
            return ToolResponse.Error(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.INVALID_ARGUMENT, "offset must be >= 0 and limit 1-50.");
        var (items, total) = _client.ReportStore.List(offset, limit);
        return ToolResponse.Serialize(new JObject { ["total"] = total, ["offset"] = offset, ["reports"] = items, ["agent_reported"] = true });
    }

    [McpServerTool(Name = "inventor_list_open_documents", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List the documents Inventor has in memory (open windows and documents loaded as assembly references): title, full path, document type, is_active, dirty (unsaved changes), visible (has a window). filter keeps documents whose path or title contains the text; dirty_only=true keeps only unsaved ones — e.g. before inventor_save_all.")]
    public Task<string> ListOpenDocuments(string? filter = null, bool dirty_only = false, CancellationToken ct = default)
        => Call("list_open_documents", new JObject { ["filter"] = filter, ["dirty_only"] = dirty_only }, ct);

    [McpServerTool(Name = "inventor_get_document_state", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Check a document before saving or ending work (rvt-mcp revit_get_document_state parity): mode (local_file, unsaved, template, read_only), is_modified, dirty_references (dirty documents a save would or would not include), modifiable, is_read_only, exists_on_disk, saved_in_version, needs_migrating (saving upgrades the file format), file_save_counter, document_id, plus the save policy {mode, rules, allowed} to follow and the active project's old_versions_to_keep. Default: the active document; document = full path or name of an open/loaded document (never opened automatically); scope=all lists every open document (policy stays for the active one). Works with no document open. Example: inventor_get_document_state() before inventor_save_document.")]
    public Task<string> GetDocumentState(string? document = null, string scope = "active", CancellationToken ct = default)
        => Call("get_document_state", new JObject { ["document"] = document, ["scope"] = scope }, ct);

    [McpServerTool(Name = "inventor_get_document_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get a document's title, full path, document type and dirty flag — the active document, or document (full path or name of an open/loaded document; never opened automatically). references=true adds the direct file references [{path, missing}] (Document.File.ReferencedFileDescriptors).")]
    public Task<string> GetDocumentInfo(string? document = null, bool references = false, CancellationToken ct = default)
        => Call("get_document_info", new JObject { ["document"] = document, ["references"] = references }, ct);

    [McpServerTool(Name = "inventor_list_bodies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List the active part's solid bodies: {id:'body:N', name, volume_mm3, bbox_mm{min,max}, face_count, created_by (producing feature name), visible} plus total/truncated. Use ids/names with extrude affected_bodies. max_items caps the list (default 200). Read-only. Optional document: full path or name of a document already open or loaded in Inventor (e.g. a part referenced by the open assembly) — never opened or activated; omit for the active document.")]
    public Task<string> ListBodies(int max_items = 200, string? document = null, CancellationToken ct = default)
        => Call("list_bodies", new JObject { ["max_items"] = max_items, ["document"] = document }, ct);

    [McpServerTool(Name = "inventor_list_features", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List the active part's features in tree order: {name, type, health, suppressed, body_names[]} plus total/truncated. health is the Inventor health status (UpToDate, OutOfDate, InError, …) — check it before assuming a feature worked; include_health=false omits it. max_items caps the list (default 200). Read-only. Optional document: full path or name of a document already open or loaded in Inventor (e.g. a part referenced by the open assembly) — never opened or activated; omit for the active document.")]
    public Task<string> ListFeatures(int max_items = 200, bool include_health = true, string? document = null, CancellationToken ct = default)
        => Call("list_features", new JObject { ["max_items"] = max_items, ["include_health"] = include_health, ["document"] = document }, ct);

    [McpServerTool(Name = "inventor_list_faces", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List the target part's faces: {id:'body:B/face:F', surface_type (plane|cylinder|cone|sphere|torus|bspline|elliptical_cylinder|elliptical_cone|…), area_mm2, bbox_mm, normal (planar faces only, unit vector corrected for IsParamReversed)}. ids round-trip into face:F / body:B/face:F refs (work planes, unwrap_try). body scopes to one body ('1'/'body:N'/name); surface_type filters; max_items caps (default 200), truncated reports overflow. Read-only. Optional document: a document already open or loaded in Inventor — never opened or activated; omit for the active document.")]
    public Task<string> ListFaces(string? body = null, string? surface_type = null, int max_items = 200, string? document = null, CancellationToken ct = default)
    {
        var p = new JObject { ["max_items"] = max_items };
        if (!string.IsNullOrWhiteSpace(body)) p["body"] = body;
        if (!string.IsNullOrWhiteSpace(surface_type)) p["surface_type"] = surface_type;
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        return Call("list_faces", p, ct);
    }

    [McpServerTool(Name = "inventor_list_edges", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List the target part's edges: {id:'body:B/edge:E', curve_type (line|line_segment|circle|circular_arc|ellipse_full|elliptical_arc|bspline|polyline|unknown), length_mm, radius_mm (circular edges only)}. ids round-trip into edge:E / body:B/edge:E refs (fillet, chamfer, create_work_axis, inventor_sheet_fabrication_flange). body scopes to one body ('1'/'body:N'/name); curve_type filters; max_items caps (default 200), truncated reports overflow. Read-only. Optional document: a document already open or loaded in Inventor — never opened or activated; omit for the active document.")]
    public Task<string> ListEdges(string? body = null, string? curve_type = null, int max_items = 200, string? document = null, CancellationToken ct = default)
    {
        var p = new JObject { ["max_items"] = max_items };
        if (!string.IsNullOrWhiteSpace(body)) p["body"] = body;
        if (!string.IsNullOrWhiteSpace(curve_type)) p["curve_type"] = curve_type;
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        return Call("list_edges", p, ct);
    }

    [McpServerTool(Name = "inventor_probe_brep", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Survey the active part's B-rep for port mouths: planar faces carrying an inner-loop circular edge (hole/pipe openings). Reports per face: body, face_index, normal (unit vector corrected for IsParamReversed), center_mm, port_diameter_mm (2 × smallest inner-loop radius), and every full-circle edge on the face (radius_mm, center_mm, inner_loop) so concentric flange rims are visible — a face with several openings is one port; check circles[] for the rest. Full circles only (arcs/slots excluded); a cylindrical boss on a face looks identical and is also reported. body scopes to one body ('1'/'body:N'/name); min/max_diameter_mm filter; max_items caps (default 200), truncated reports overflow. Read-only.")]
    public Task<string> ProbeBrep(string? body = null, double? min_diameter_mm = null, double? max_diameter_mm = null, int max_items = 200, CancellationToken ct = default)
    {
        var p = new JObject { ["max_items"] = max_items };
        if (!string.IsNullOrWhiteSpace(body)) p["body"] = body;
        if (min_diameter_mm is { } mn) p["min_diameter_mm"] = mn;
        if (max_diameter_mm is { } mx) p["max_diameter_mm"] = mx;
        return Call("probe_brep", p, ct);
    }

    [McpServerTool(Name = "inventor_list_sketch_points", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List 2D sketch points with stable opaque point_ref values, sketch XY coordinates in mm, and roles such as circle_center, arc_center, line_start and line_end. Returns origin_ref without projecting or modifying the sketch. Use these references with inventor_add_sketch_point_dimension; refresh after Save As or stale-reference errors. sketch_name is required; document optionally selects an open part. max_items=1..1000, default 200; truncated signals more points.")]
    public Task<string> ListSketchPoints(string sketch_name, string? document = null, int max_items = 200, CancellationToken ct = default)
    {
        var p = new JObject { ["sketch_name"] = sketch_name, ["max_items"] = max_items };
        if (document is not null) p["document"] = document;
        return Call("list_sketch_points", p, ct);
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
            return ToolResponse.Error(ex.Code, ex.Message);
        }
    }
}
