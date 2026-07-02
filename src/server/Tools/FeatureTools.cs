using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

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
     Description("Create holes on the ACTIVE PART: pick a planar face with the deterministic selector (face_normal +X|-X|+Y|-Y|+Z|-Z, face_extreme max|min, optional face_near_mm), give hole centers as a FLAT mm array [x1,y1,z1,x2,y2,z2,...] lying ON that face plane, diameter_mm and kind=drilled|counterbore|countersink. through=true OR depth_mm (exclusive). Optional tap metadata: tapped_designation (e.g. 'M6x1') marks the hole tapped (metadata only, no thread geometry). Returns feature_names + hole_count.")]
    public Task<string> Hole(string faceNormal, double[] pointsMm, double diameterMm,
        string kind = "drilled", string faceExtreme = "max", double[]? faceNearMm = null,
        bool through = true, double? depthMm = null,
        double? cboreDiameterMm = null, double? cboreDepthMm = null,
        double? csinkDiameterMm = null, double csinkAngleDeg = 82,
        string? tappedDesignation = null, string tappedClass = "6H", bool tappedRightHanded = true,
        CancellationToken ct = default)
    {
        if (pointsMm is null || pointsMm.Length == 0 || pointsMm.Length % 3 != 0)
            return Task.FromResult(Err("points_mm must be a flat [x1,y1,z1,...] array (length multiple of 3)"));
        if (through && depthMm is not null)
            return Task.FromResult(Err("through=true and depth_mm are mutually exclusive"));
        if (!through && depthMm is null)
            return Task.FromResult(Err("either through=true or depth_mm is required"));
        var points = new JArray();
        for (int i = 0; i < pointsMm.Length; i += 3)
            points.Add(new JArray(pointsMm[i], pointsMm[i + 1], pointsMm[i + 2]));
        var face = new JObject { ["kind"] = "planar", ["normal"] = faceNormal, ["extreme"] = faceExtreme };
        if (faceNearMm is not null) face["near_mm"] = new JArray(faceNearMm);
        return Call("hole", new JObject
        {
            ["face"] = face, ["points_mm"] = points, ["diameter_mm"] = diameterMm, ["kind"] = kind,
            ["through"] = through, ["depth_mm"] = depthMm,
            ["cbore_diameter_mm"] = cboreDiameterMm, ["cbore_depth_mm"] = cboreDepthMm,
            ["csink_diameter_mm"] = csinkDiameterMm, ["csink_angle_deg"] = csinkAngleDeg,
            ["tapped_designation"] = tappedDesignation, ["tapped_class"] = tappedClass,
            ["tapped_right_handed"] = tappedRightHanded,
        }, ct);
    }

    [McpServerTool(Name = "inventor_circular_pattern"),
     Description("Circular-pattern part features around a named axis of the ACTIVE PART (work axis name or origin 'X Axis'|'Y Axis'|'Z Axis'). count instances over angle_deg (default full 360). Returns pattern feature name.")]
    public Task<string> CircularPattern(string[] featureNames, string axis, int count,
        double angleDeg = 360, bool naturalDirection = true, CancellationToken ct = default)
        => Call("circular_pattern", new JObject
        {
            ["feature_names"] = new JArray(featureNames), ["axis"] = axis,
            ["count"] = count, ["angle_deg"] = angleDeg, ["natural_direction"] = naturalDirection,
        }, ct);

    [McpServerTool(Name = "inventor_rectangular_pattern"),
     Description("Rectangular-pattern part features along one or two named axes of the ACTIVE PART (work axis or origin axis names). count1/spacing_mm1 along dir1; optional dir2/count2/spacing_mm2. Returns pattern feature name.")]
    public Task<string> RectangularPattern(string[] featureNames, string dir1, int count1, double spacingMm1,
        string? dir2 = null, int? count2 = null, double? spacingMm2 = null,
        bool naturalDirection1 = true, bool naturalDirection2 = true, CancellationToken ct = default)
        => Call("rectangular_pattern", new JObject
        {
            ["feature_names"] = new JArray(featureNames),
            ["dir1"] = dir1, ["count1"] = count1, ["spacing_mm1"] = spacingMm1,
            ["dir2"] = dir2, ["count2"] = count2, ["spacing_mm2"] = spacingMm2,
            ["natural_direction1"] = naturalDirection1, ["natural_direction2"] = naturalDirection2,
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
