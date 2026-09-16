using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

public class HoleFaceDto
{
    [JsonPropertyName("kind")]
    [Description("Must be 'planar'")]
    public string Kind { get; set; } = "planar";

    [JsonPropertyName("normal")]
    [Description("Normal direction (+X, -X, +Y, -Y, +Z, -Z)")]
    public string Normal { get; set; } = "";

    [JsonPropertyName("extreme")]
    [Description("Extreme position (max or min)")]
    public string Extreme { get; set; } = "max";

    [JsonPropertyName("near_mm")]
    [Description("Optional tie-break point [x, y, z] in mm")]
    public double[]? NearMm { get; set; }
}

public class HoleTappedDto
{
    [JsonPropertyName("designation")]
    [Description("Thread designation, e.g. 'M6x1'")]
    public string Designation { get; set; } = "";

    [JsonPropertyName("class")]
    [Description("Thread class, default '6H'")]
    public string Class { get; set; } = "6H";

    [JsonPropertyName("right_handed")]
    [Description("Right handed thread, default true")]
    public bool RightHanded { get; set; } = true;

    [JsonPropertyName("full_depth")]
    [Description("Full thread depth, default true")]
    public bool FullDepth { get; set; } = true;

    [JsonPropertyName("thread_depth_mm")]
    [Description("Thread depth in mm, required if full_depth=false")]
    public double? ThreadDepthMm { get; set; }
}

/// <summary>
/// Feature and work-feature tools (toolset <c>feature</c>, all write). Thin MCP wrappers that
/// serialize typed parameters and round-trip a wire command to the active Inventor add-in. All length
/// inputs are in <b>mm</b> and angles in <b>degrees</b>; the add-in handler converts to Inventor's
/// internal centimetres/radians.
/// </summary>
[McpServerToolType]
public sealed class FeatureTools
{
    private readonly PluginClient _client;
    public FeatureTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_extrude"),
     Description("Extrude the profile of a named sketch. distance: number (mm) or an expression string ('40 mm', 'plate_thk'). operation=join|cut|intersect|new_body; direction=positive|negative|symmetric. name renames the created feature (with new_body the body is named '<name>_body'). affected_bodies (['1','body:2','BodyName']) scopes join/cut on multi-body parts (invalid with new_body). Requires an active part document. Returns the feature name and body volume mm^3.")]
    public Task<string> Extrude(string sketchName, System.Text.Json.JsonElement distance, string operation = "join", string direction = "positive", string? name = null, string[]? affected_bodies = null, CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sketch_name"] = sketchName,
            ["distance_mm"] = JToken.Parse(distance.GetRawText()),
            ["operation"] = operation,
            ["direction"] = direction,
        };
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        if (affected_bodies is { Length: > 0 }) p["affected_bodies"] = new JArray(affected_bodies);
        return Call("extrude", p, ct);
    }

    [McpServerTool(Name = "inventor_combine"),
     Description("Boolean solid bodies in the active part: base_body + tool_bodies (['1','body:2','BodyName']) with operation=join|cut|intersect. keep_tool_bodies (default false) retains tool bodies. name renames the feature. Returns the result body_names — names can change across a combine, so use the response, don't assume pre-combine names survive. Requires ≥2 solid bodies.")]
    public Task<string> Combine(string base_body, string[] tool_bodies, string operation = "join", bool keep_tool_bodies = false, string? name = null, CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["base_body"] = base_body,
            ["tool_bodies"] = new JArray(tool_bodies),
            ["operation"] = operation,
        };
        if (keep_tool_bodies) p["keep_tool_bodies"] = true;
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        return Call("combine", p, ct);
    }

    [McpServerTool(Name = "inventor_batch_execute"),
     Description("Run up to 20 commands in one Inventor transaction (a single undo step). commands=[{command, params}] using WIRE command names — unprefixed snake_case like extrude, create_work_plane, create_sketch, draw_rectangle, close_sketch, list_bodies (the wire names inside each tool's params, not inventor_* names). Stops at the first error and rolls the batch back unless continue_on_error=true. send_code/run_baked_tool/apply_bake/batch_execute and document-lifecycle commands (new_part/new_assembly/open_document/close_document/save_document) are not allowed inside. Per-step ok is wire-level — data payloads with their own ok/health fields (e.g. constraint health) still need checking. Returns per-step {index, ok, data|error}, executed count, rolled_back.")]
    public Task<string> BatchExecute(System.Text.Json.JsonElement commands, bool continue_on_error = false, CancellationToken ct = default)
    {
        if (commands.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Task.FromResult(Err("commands must be an array of {command, params}"));
        return Call("batch_execute", new JObject
        {
            ["commands"] = JToken.Parse(commands.GetRawText()),
            ["continue_on_error"] = continue_on_error,
        }, ct, timeoutMs: 120_000);   // 20 steps share one budget — raise over the 30 s default
    }

    [McpServerTool(Name = "inventor_loft"),
     Description("Loft an ordered list of sketch profiles into a feature. profiles: sketch names in loft order ('SketchName' or 'SketchName:N' for the Nth profile of a multi-profile sketch), at least 2. operation=join|cut|intersect|new_body (with new_body the body is named '<name>_body'). centerline: optional sketch name whose first curve drives a centerline loft. merge_tangent_faces (default true), closed (default false, periodic loft). name renames the feature. Returns feature name, section count and feature-body volume mm^3.")]
    public Task<string> Loft(string[] profiles, string operation = "join", string? centerline = null,
        bool merge_tangent_faces = true, bool closed = false, string? name = null, CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["profiles"] = new JArray(profiles),
            ["operation"] = operation,
            ["merge_tangent_faces"] = merge_tangent_faces,
            ["closed"] = closed,
        };
        if (!string.IsNullOrWhiteSpace(centerline)) p["centerline"] = centerline;
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        return Call("loft", p, ct);
    }

    [McpServerTool(Name = "inventor_sweep"),
     Description("Sweep a sketch profile along a sketch path. profile: section sketch ('SketchName' or 'SketchName:N'). path: path sketch name — its first curve is used and connected segments chain automatically (path_entity_count in the response shows the resolved segment count). operation=join|cut|intersect|new_body (with new_body the body is named '<name>_body'). orientation=normal_to_path|parallel. name renames the feature. Returns feature name, path entity count and feature-body volume mm^3.")]
    public Task<string> Sweep(string profile, string path, string operation = "join",
        string orientation = "normal_to_path", string? name = null, CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["profile"] = profile,
            ["path"] = path,
            ["operation"] = operation,
            ["orientation"] = orientation,
        };
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        return Call("sweep", p, ct);
    }

    [McpServerTool(Name = "inventor_revolve"),
     Description("Revolve the profile of a named sketch about an axis (axis_id = a sketch line entity id or an origin axis XAxis|YAxis|ZAxis). angle in degrees; operation=join|cut|intersect. Returns the new feature name.")]
    public Task<string> Revolve(string sketchName, string axisId, double angle, string operation = "join", CancellationToken ct = default)
        => Call("revolve", new JObject
        {
            ["sketch_name"] = sketchName,
            ["axis_id"] = axisId,
            ["angle_deg"] = angle,
            ["operation"] = operation,
        }, ct);

    [McpServerTool(Name = "inventor_fillet"),
     Description("Add a constant-radius edge fillet. radius in mm. Pick edges either with edgeIds ([\"1\",\"body:2/edge:3\"]) or an edges value — a plain id array or a selector object {kind:'circular', radius_mm?, radius_tol_mm?, center_mm?, center_tol_mm?, on_body?, adjacent_surface_types?}; edges wins when both are given. Circular edges are filtered by radius, circle center, body, and the surface types of BOTH adjacent faces (plane|cylinder|cone|torus|sphere|bspline|elliptical_cylinder|elliptical_cone). Returns feature_name and matched_edges so you can verify what was filleted.")]
    public Task<string> Fillet(double radius, string[]? edgeIds = null, System.Text.Json.JsonElement? edges = null, CancellationToken ct = default)
    {
        var p = new JObject { ["radius_mm"] = radius };
        if (edgeIds is { Length: > 0 }) p["edge_ids"] = new JArray(edgeIds);
        if (edges is { } el)
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.Array && el.ValueKind != System.Text.Json.JsonValueKind.Object)
                return Task.FromResult(Err("edges must be an array of edge ids or a selector object"));
            p["edges"] = JToken.Parse(el.GetRawText());
        }
        return Call("fillet", p, ct);
    }

    [McpServerTool(Name = "inventor_chamfer"),
     Description("Add an equal-distance edge chamfer over the given model edge_ids. distance in mm. Returns the new chamfer feature name.")]
    public Task<string> Chamfer(string[] edgeIds, double distance, CancellationToken ct = default)
        => Call("chamfer", new JObject { ["edge_ids"] = new JArray(edgeIds), ["distance_mm"] = distance }, ct);

    [McpServerTool(Name = "inventor_create_work_plane"),
     Description("Create a work plane. type=offset (refs=[plane_or_face_id], offset mm) | three_points (refs=[3 point ids]) | tangent (refs=[face_id, plane_id]) | fixed (origin/x_axis/y_axis as {x,y,z} or [x,y,z] — origin in mm, axes are direction vectors auto-normalized; refs unused). Optional name/visible apply to every type. Returns the new work-plane name.")]
    public Task<string> CreateWorkPlane(string type, string[]? refs = null, double? offset = null,
        System.Text.Json.JsonElement? origin = null, System.Text.Json.JsonElement? x_axis = null,
        System.Text.Json.JsonElement? y_axis = null, string? name = null, bool? visible = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["type"] = type,
            ["offset_mm"] = offset.HasValue ? new JValue(offset.Value) : JValue.CreateNull(),
        };
        if (refs is { Length: > 0 }) p["refs"] = new JArray(refs);
        if (origin is { } o) p["origin"] = JToken.Parse(o.GetRawText());
        if (x_axis is { } x) p["x_axis"] = JToken.Parse(x.GetRawText());
        if (y_axis is { } y) p["y_axis"] = JToken.Parse(y.GetRawText());
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        if (visible.HasValue) p["visible"] = visible.Value;
        return Call("create_work_plane", p, ct);
    }

    [McpServerTool(Name = "inventor_create_work_point"),
     Description("Create a fixed work point at position ({x,y,z} or [x,y,z], mm). construction (default false) marks it a construction point — note Inventor does not allow naming construction points (name_applied=false reports that). name/visible optional. Returns the work-point name.")]
    public Task<string> CreateWorkPoint(System.Text.Json.JsonElement position, bool construction = false,
        string? name = null, bool? visible = null, CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["position"] = JToken.Parse(position.GetRawText()),
            ["construction"] = construction,
        };
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        if (visible is { } v) p["visible"] = v;
        return Call("create_work_point", p, ct);
    }

    [McpServerTool(Name = "inventor_create_bim_connector"),
     Description("Author a BIM pipe connector on a circular port edge. geometry is an edge ref ('body:1/edge:3' — e.g. from inventor_probe_brep). kind='pipe' only for now. Optional: name, nominal_diameter_mm, system_type (domestic_cold|domestic_hot|sanitary|hydronic_supply|hydronic_return|fire_protection|other), flow_direction (in|out|bidirectional), connection_type (threaded|flanged|welded|glued|compression|other), description. Returns the connector name.")]
    public Task<string> CreateBimConnector(string geometry, string kind = "pipe", string? name = null,
        double? nominal_diameter_mm = null, string? system_type = null, string? flow_direction = null,
        string? connection_type = null, string? description = null, CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["geometry"] = geometry,
            ["kind"] = kind,
        };
        if (!string.IsNullOrWhiteSpace(name)) p["name"] = name;
        if (nominal_diameter_mm is { } nd) p["nominal_diameter_mm"] = nd;
        if (!string.IsNullOrWhiteSpace(system_type)) p["system_type"] = system_type;
        if (!string.IsNullOrWhiteSpace(flow_direction)) p["flow_direction"] = flow_direction;
        if (!string.IsNullOrWhiteSpace(connection_type)) p["connection_type"] = connection_type;
        if (!string.IsNullOrWhiteSpace(description)) p["description"] = description;
        return Call("create_bim_connector", p, ct);
    }

    [McpServerTool(Name = "inventor_create_work_axis"),
     Description("Create a work axis. type=two_points (refs=[2 point ids]) | edge (refs=[edge_id]) | plane_intersection (refs=[2 plane ids]) | normal_to_face_through_point (refs=[face_id, point_id]). Returns the new work-axis name.")]
    public Task<string> CreateWorkAxis(string type, string[] refs, CancellationToken ct = default)
        => Call("create_work_axis", new JObject { ["type"] = type, ["refs"] = new JArray(refs) }, ct);

    [McpServerTool(Name = "inventor_hole"),
     Description("Create holes on the ACTIVE PART: pick a planar face with the deterministic selector (face_normal +X|-X|+Y|-Y|+Z|-Z, face_extreme max|min, optional face_near_mm), give hole centers as a nested array of coordinates [[x1,y1,z1],[x2,y2,z2],...] lying ON that face plane, diameter_mm and kind=drilled|counterbore|countersink. through=true OR depth_mm (exclusive). Optional tap metadata: tapped_designation (e.g. 'M6x1') marks the hole tapped. Returns feature_names + hole_count.")]
    public Task<string> Hole(
        HoleFaceDto face,
        double[][] points_mm,
        double diameter_mm,
        string kind = "drilled",
        bool through = true,
        double? depth_mm = null,
        double? cbore_diameter_mm = null,
        double? cbore_depth_mm = null,
        double? csink_diameter_mm = null,
        double csink_angle_deg = 82,
        HoleTappedDto? tapped = null,
        CancellationToken ct = default)
    {
        if (face is null) return Task.FromResult(Err("face selector is required"));
        if (points_mm is null || points_mm.Length == 0)
            return Task.FromResult(Err("points_mm must contain at least one point"));
        foreach (var pt in points_mm)
        {
            if (pt is null || pt.Length != 3)
                return Task.FromResult(Err("each point in points_mm must be a 3-element array [x,y,z]"));
        }
        if (through && depth_mm is not null)
            return Task.FromResult(Err("through=true and depth_mm are mutually exclusive"));
        if (!through && depth_mm is null)
            return Task.FromResult(Err("either through=true or depth_mm is required"));

        var ptsArr = new JArray();
        foreach (var pt in points_mm)
        {
            ptsArr.Add(new JArray(pt[0], pt[1], pt[2]));
        }

        var faceObj = new JObject { ["kind"] = face.Kind, ["normal"] = face.Normal, ["extreme"] = face.Extreme };
        if (face.NearMm is not null)
        {
            if (face.NearMm.Length != 3) return Task.FromResult(Err("face.near_mm must be [x,y,z]"));
            faceObj["near_mm"] = new JArray(face.NearMm);
        }

        var p = new JObject
        {
            ["face"] = faceObj, ["points_mm"] = ptsArr, ["diameter_mm"] = diameter_mm, ["kind"] = kind,
            ["through"] = through, ["depth_mm"] = depth_mm,
            ["cbore_diameter_mm"] = cbore_diameter_mm, ["cbore_depth_mm"] = cbore_depth_mm,
            ["csink_diameter_mm"] = csink_diameter_mm, ["csink_angle_deg"] = csink_angle_deg,
        };

        if (tapped is not null)
        {
            p["tapped_designation"] = tapped.Designation;
            p["tapped_class"] = tapped.Class;
            p["tapped_right_handed"] = tapped.RightHanded;
            p["tapped_full_depth"] = tapped.FullDepth;
            p["tapped_thread_depth_mm"] = tapped.ThreadDepthMm;
        }

        return Call("hole", p, ct);
    }

    [McpServerTool(Name = "inventor_circular_pattern"),
     Description("Circular-pattern part features around a named axis of the ACTIVE PART (work axis name or origin 'X Axis'|'Y Axis'|'Z Axis'). count instances over angle_deg (default full 360). Returns pattern feature name.")]
    public Task<string> CircularPattern(
        string[] feature_names,
        string axis,
        int count,
        double angle_deg = 360,
        bool natural_direction = true,
        CancellationToken ct = default)
        => Call("circular_pattern", new JObject
        {
            ["feature_names"] = new JArray(feature_names), ["axis"] = axis,
            ["count"] = count, ["angle_deg"] = angle_deg, ["natural_direction"] = natural_direction,
        }, ct);

    [McpServerTool(Name = "inventor_rectangular_pattern"),
     Description("Rectangular-pattern part features along one or two named axes of the ACTIVE PART (work axis or origin axis names). count1/spacing_mm1 along dir1; optional dir2/count2/spacing_mm2. Returns pattern feature name.")]
    public Task<string> RectangularPattern(
        string[] feature_names,
        string dir1,
        int count1,
        double spacing_mm1,
        string? dir2 = null,
        int? count2 = null,
        double? spacing_mm2 = null,
        bool natural_direction1 = true,
        bool natural_direction2 = true,
        CancellationToken ct = default)
    {
        if (!PatternInputValidator.TryValidateRectangular(
                count1, spacing_mm1, dir2, count2, spacing_mm2, out var error))
            return Task.FromResult(Err(error));

        return Call("rectangular_pattern", new JObject
        {
            ["feature_names"] = new JArray(feature_names),
            ["dir1"] = dir1, ["count1"] = count1, ["spacing_mm1"] = spacing_mm1,
            ["dir2"] = dir2, ["count2"] = count2, ["spacing_mm2"] = spacing_mm2,
            ["natural_direction1"] = natural_direction1, ["natural_direction2"] = natural_direction2,
        }, ct);
    }

    private static string Err(string message)
        => ToolResponse.Error("INVALID_ARGUMENT", message);

    private async Task<string> Call(string command, JObject p, CancellationToken ct, int? timeoutMs = null)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct, timeoutMs);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex.Code, ex.Message);
        }
    }
}
