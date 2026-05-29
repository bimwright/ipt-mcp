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
