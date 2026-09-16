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
public sealed class QueryTools
{
    private readonly PluginClient _client;
    public QueryTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_health"),
     Description("Probe the active Inventor add-in target: reports inventor_year, process_id, whether a document is open, the active document type, and sta_busy/pending_commands (the STA work queue — check it before retrying a timed-out send_code). Read-only; answers even while the STA thread is jammed.")]
    public Task<string> Health(CancellationToken ct = default)
        => Call("health", new JObject(), ct);

    [McpServerTool(Name = "inventor_list_open_documents"),
     Description("List all open Inventor documents: title, full path, document type, and which one is active.")]
    public Task<string> ListOpenDocuments(CancellationToken ct = default)
        => Call("list_open_documents", new JObject(), ct);

    [McpServerTool(Name = "inventor_get_document_info"),
     Description("Get the active Inventor document's title, full path, and document type.")]
    public Task<string> GetDocumentInfo(CancellationToken ct = default)
        => Call("get_document_info", new JObject(), ct);

    [McpServerTool(Name = "inventor_list_bodies"),
     Description("List the active part's solid bodies: {id:'body:N', name, volume_mm3, bbox_mm{min,max}, face_count, created_by (producing feature name), visible} plus total/truncated. Use ids/names with extrude affected_bodies. max_items caps the list (default 200). Read-only.")]
    public Task<string> ListBodies(int max_items = 200, CancellationToken ct = default)
        => Call("list_bodies", new JObject { ["max_items"] = max_items }, ct);

    [McpServerTool(Name = "inventor_list_features"),
     Description("List the active part's features in tree order: {name, type, health, suppressed, body_names[]} plus total/truncated. health is the Inventor health status (UpToDate, OutOfDate, InError, …) — check it before assuming a feature worked; include_health=false omits it. max_items caps the list (default 200). Read-only.")]
    public Task<string> ListFeatures(int max_items = 200, bool include_health = true, CancellationToken ct = default)
        => Call("list_features", new JObject { ["max_items"] = max_items, ["include_health"] = include_health }, ct);

    [McpServerTool(Name = "inventor_probe_brep"),
     Description("Survey the active part's B-rep for port mouths: planar faces carrying an inner-loop circular edge (hole/pipe openings). Reports per face: body, face_index, normal (unit vector corrected for IsParamReversed), center_mm, port_diameter_mm (2 × smallest inner-loop radius), and every full-circle edge on the face (radius_mm, center_mm, inner_loop) so concentric flange rims are visible — a face with several openings is one port; check circles[] for the rest. Full circles only (arcs/slots excluded); a cylindrical boss on a face looks identical and is also reported. body scopes to one body ('1'/'body:N'/name); min/max_diameter_mm filter; max_items caps (default 200), truncated reports overflow. Read-only.")]
    public Task<string> ProbeBrep(string? body = null, double? min_diameter_mm = null, double? max_diameter_mm = null, int max_items = 200, CancellationToken ct = default)
    {
        var p = new JObject { ["max_items"] = max_items };
        if (!string.IsNullOrWhiteSpace(body)) p["body"] = body;
        if (min_diameter_mm is { } mn) p["min_diameter_mm"] = mn;
        if (max_diameter_mm is { } mx) p["max_diameter_mm"] = mx;
        return Call("probe_brep", p, ct);
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
