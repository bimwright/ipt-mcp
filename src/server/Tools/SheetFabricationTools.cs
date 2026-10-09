using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Sheet Fabrication authoring tools (toolset <c>sheet_fabrication</c>, all write). Thin MCP wrappers that
/// serialize parameters and round-trip a wire command to the active Inventor add-in. Lengths are
/// mm and angles degrees; the add-in converts to Inventor's internal centimetres/radians.
/// </summary>
[McpServerToolType]
[Toolset("sheet_fabrication")]
public sealed class SheetFabricationTools
{
    private readonly PluginClient _client;
    public SheetFabricationTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_new_sheet_fabrication_part", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Create and activate a new sheet fabrication part document. template: optional explicit .ipt path — always wins (use it when several locales/templates exist); otherwise the first '*Sheet Metal*.ipt' under Inventor's templates path is used (the filename is localized, e.g. 'Sheet Metal (mm).ipt'). Returns the new document title, the template path actually used and is_sheet_fabrication.")]
    public Task<string> NewSheetFabricationPart(string? template = null, CancellationToken ct = default)
        => Call("new_sheet_fabrication_part", new JObject { ["template"] = template }, ct);

    [McpServerTool(Name = "inventor_sheet_fabrication_face", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Build a sheet fabrication face (the base plate) from a closed sketch profile. sketch: sketch name or 'Sketch:N' profile spec. Optional name renames the feature. Returns feature_name plus fresh face_count/edge_count — topology ids shift on every feature, so re-list faces/edges before reusing ids. Requires a sheet fabrication part document.")]
    public Task<string> SheetFabricationFace(string sketch, string? name = null, string? document = null, CancellationToken ct = default)
    {
        var p = new JObject { ["sketch"] = sketch };
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        return Call("sheet_fabrication_face", p, ct);
    }

    [McpServerTool(Name = "inventor_sheet_fabrication_flange", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Bend a flange up from sheet-boundary edges. edges: non-empty array of edge ids ('edge:E' or 'body:B/edge:E' — from inventor_list_edges). height: mm number or expression ('25 mm', 'flange_h'). angle_deg: 0 < a ≤ 180, default 90. bend_radius_mm: optional per-flange style override. name renames the feature. The applied height is read back and rewritten once if Inventor dropped it. Returns feature_name, height_expression and fresh topology counts.")]
    public Task<string> SheetFabricationFlange(string[] edges, System.Text.Json.JsonElement height,
        double angle_deg = 90, double? bend_radius_mm = null, string? name = null, string? document = null, CancellationToken ct = default)
    {
        if (edges is null || edges.Length == 0)
            return Task.FromResult(Err("edges must be a non-empty array of edge ids"));
        var p = new JObject
        {
            ["edges"] = new JArray(edges),
            ["height"] = JToken.Parse(height.GetRawText()),
            ["angle_deg"] = angle_deg,
        };
        if (bend_radius_mm is { } br) p["bend_radius_mm"] = br;
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        return Call("sheet_fabrication_flange", p, ct);
    }

    [McpServerTool(Name = "inventor_sheet_fabrication_cut", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Punch a closed-profile cutout through the sheet. sketch: sketch name or 'Sketch:N' profile spec. cut_across_bends (default false) lets the cut span bends; cut_normal_to_flat (default false) keeps the cut wall normal to the flat pattern — what CNC tooling wants. direction=positive|negative (default positive). name renames the feature. Returns feature_name and fresh topology counts.")]
    public Task<string> SheetFabricationCut(string sketch, bool cut_across_bends = false, bool cut_normal_to_flat = false,
        string direction = "positive", string? name = null, string? document = null, CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sketch"] = sketch,
            ["cut_across_bends"] = cut_across_bends,
            ["cut_normal_to_flat"] = cut_normal_to_flat,
            ["direction"] = direction,
        };
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        return Call("sheet_fabrication_cut", p, ct);
    }

    [McpServerTool(Name = "inventor_flat_pattern", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Create the sheet fabrication flat pattern if missing (a document-state change, hence a write tool), then report it. face: optional base-face id ('face:F' or 'body:B/face:F') — without it Inventor chooses. Returns has_flat_pattern, created (false when the flat pattern already existed — nothing changed) and flat_length_mm/flat_width_mm — the blank size before export_dxf source=flat_pattern.")]
    public Task<string> FlatPattern(string? face = null, string? document = null, CancellationToken ct = default)
    {
        var p = new JObject();
        if (!string.IsNullOrWhiteSpace(face)) p["face"] = face;
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        return Call("sheet_fabrication_flat_pattern", p, ct);
    }

    [McpServerTool(Name = "inventor_set_sheet_fabrication_thickness", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Override the sheet thickness on a sheet fabrication part. thickness: mm number or expression ('2.5 mm', 'plate_thk'). Clears use_sheet_fabrication_style_thickness so the part stops inheriting the style, then sets the Thickness parameter expression (culture-invariant — no locale hacks). Returns the applied expression, thickness_mm and fresh topology counts.")]
    public Task<string> SetSheetFabricationThickness(System.Text.Json.JsonElement thickness, string? document = null, CancellationToken ct = default)
    {
        var p = new JObject { ["thickness"] = JToken.Parse(thickness.GetRawText()) };
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        return Call("sheet_fabrication_set_thickness", p, ct);
    }

    [McpServerTool(Name = "inventor_unwrap_try", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Trial-unwrap a set of faces into a flat surface body, measure it, then roll back unless keep=true — probes manufacturability without committing a feature. faces: non-empty array of face ids ('face:F' or 'body:B/face:F' — from inventor_list_faces). alignment=origin|xy|xz|yz (default origin). auto_face_chain chains tangent faces; merge_result_body merges into the source body. Works on any part, not only sheet fabrication. On failure the response carries Inventor's own last error message.")]
    public Task<string> UnwrapTry(string[] faces, string alignment = "origin",
        bool auto_face_chain = false, bool merge_result_body = false, bool keep = false,
        string? document = null, CancellationToken ct = default)
    {
        if (faces is null || faces.Length == 0)
            return Task.FromResult(Err("faces must be a non-empty array of face ids"));
        var p = new JObject
        {
            ["faces"] = new JArray(faces),
            ["alignment"] = alignment,
            ["auto_face_chain"] = auto_face_chain,
            ["merge_result_body"] = merge_result_body,
            ["keep"] = keep,
        };
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        return Call("unwrap_try", p, ct);
    }

    private static string Err(string message)
        => ToolResponse.Error("INVALID_ARGUMENT", message);

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
