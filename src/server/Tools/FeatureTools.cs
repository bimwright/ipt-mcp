using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
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
     Description("Extrude the profile of a named sketch. distance in mm; operation=join|cut|intersect; direction=positive|negative|symmetric. Requires an active part document. Returns the new feature name.")]
    public Task<string> Extrude(string sketchName, double distance, string operation = "join", string direction = "positive", CancellationToken ct = default)
        => Call("extrude", new JObject
        {
            ["sketch_name"] = sketchName,
            ["distance_mm"] = distance,
            ["operation"] = operation,
            ["direction"] = direction,
        }, ct);

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
     Description("Add a constant-radius edge fillet over the given model edge_ids. radius in mm. Returns the new fillet feature name.")]
    public Task<string> Fillet(string[] edgeIds, double radius, CancellationToken ct = default)
        => Call("fillet", new JObject { ["edge_ids"] = new JArray(edgeIds), ["radius_mm"] = radius }, ct);

    [McpServerTool(Name = "inventor_chamfer"),
     Description("Add an equal-distance edge chamfer over the given model edge_ids. distance in mm. Returns the new chamfer feature name.")]
    public Task<string> Chamfer(string[] edgeIds, double distance, CancellationToken ct = default)
        => Call("chamfer", new JObject { ["edge_ids"] = new JArray(edgeIds), ["distance_mm"] = distance }, ct);

    [McpServerTool(Name = "inventor_create_work_plane"),
     Description("Create a work plane. type=offset (refs=[plane_or_face_id], offset mm) | three_points (refs=[3 point ids]) | tangent (refs=[face_id, plane_id]). Returns the new work-plane name.")]
    public Task<string> CreateWorkPlane(string type, string[] refs, double? offset = null, CancellationToken ct = default)
        => Call("create_work_plane", new JObject
        {
            ["type"] = type,
            ["refs"] = new JArray(refs),
            ["offset_mm"] = offset.HasValue ? new JValue(offset.Value) : JValue.CreateNull(),
        }, ct);

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
        => Call("rectangular_pattern", new JObject
        {
            ["feature_names"] = new JArray(feature_names),
            ["dir1"] = dir1, ["count1"] = count1, ["spacing_mm1"] = spacing_mm1,
            ["dir2"] = dir2, ["count2"] = count2, ["spacing_mm2"] = spacing_mm2,
            ["natural_direction1"] = natural_direction1, ["natural_direction2"] = natural_direction2,
        }, ct);

    private static string Err(string message)
        => Newtonsoft.Json.JsonConvert.SerializeObject(new { ok = false, error = new { code = "INVALID_ARGUMENT", message } }, Newtonsoft.Json.Formatting.Indented);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        catch (InventorGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.Indented);
        }
    }
}
