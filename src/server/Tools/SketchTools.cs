using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Tools;

/// <summary>
/// Sketch tools (toolset <c>sketch</c>, all write). Thin MCP wrappers that serialize typed
/// parameters and round-trip a wire command to the active Inventor add-in. All length inputs are in
/// <b>mm</b> and angles in <b>degrees</b>; the add-in handler converts to Inventor's internal
/// centimetres/radians.
/// </summary>
[McpServerToolType]
public sealed class SketchTools
{
    private readonly PluginClient _client;
    public SketchTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_create_sketch"),
     Description("Create a new 2D sketch on a plane. plane=XY|XZ|YZ for an origin work plane, or a face/work-plane reference id. Requires an active part document. Returns the new sketch name.")]
    public Task<string> CreateSketch(string plane = "XY", CancellationToken ct = default)
        => Call("create_sketch", new JObject { ["plane"] = plane }, ct);

    [McpServerTool(Name = "inventor_project_geometry"),
     Description("Project model edges/vertices (by edge_ids) into the active sketch. Returns the count of projected curves.")]
    public Task<string> ProjectGeometry(string[] edgeIds, CancellationToken ct = default)
        => Call("project_geometry", new JObject { ["edge_ids"] = new JArray(edgeIds) }, ct);

    [McpServerTool(Name = "inventor_draw_line"),
     Description("Draw a sketch line from (x1,y1) to (x2,y2) in mm in the active sketch. Returns the new sketch-line entity id.")]
    public Task<string> DrawLine(double x1, double y1, double x2, double y2, CancellationToken ct = default)
        => Call("draw_line", new JObject { ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 }, ct);

    [McpServerTool(Name = "inventor_draw_circle"),
     Description("Draw a sketch circle centered at (cx,cy) with the given radius (all mm) in the active sketch. Returns the new circle entity id.")]
    public Task<string> DrawCircle(double cx, double cy, double radius, CancellationToken ct = default)
        => Call("draw_circle", new JObject { ["cx"] = cx, ["cy"] = cy, ["radius"] = radius }, ct);

    [McpServerTool(Name = "inventor_draw_rectangle"),
     Description("Draw a two-point sketch rectangle with opposite corners (x1,y1) and (x2,y2) in mm. Returns the four new line entity ids.")]
    public Task<string> DrawRectangle(double x1, double y1, double x2, double y2, CancellationToken ct = default)
        => Call("draw_rectangle", new JObject { ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 }, ct);

    [McpServerTool(Name = "inventor_draw_arc"),
     Description("Draw a sketch arc centered at (cx,cy) with radius (mm), sweeping from start_deg to end_deg (degrees, CCW). Returns the new arc entity id.")]
    public Task<string> DrawArc(double cx, double cy, double radius, double startDeg, double endDeg, CancellationToken ct = default)
        => Call("draw_arc", new JObject { ["cx"] = cx, ["cy"] = cy, ["radius"] = radius, ["start_deg"] = startDeg, ["end_deg"] = endDeg }, ct);

    [McpServerTool(Name = "inventor_add_sketch_dimension"),
     Description("Add a dimension constraint driving a sketch entity (by entity_id) to value mm. Returns the dimension constraint name.")]
    public Task<string> AddSketchDimension(string entityId, double value, CancellationToken ct = default)
        => Call("add_sketch_dimension", new JObject { ["entity_id"] = entityId, ["value_mm"] = value }, ct);

    [McpServerTool(Name = "inventor_add_sketch_constraint"),
     Description("Add a geometric sketch constraint over entity_ids. type=coincident|parallel|perpendicular|horizontal|vertical|tangent|concentric|equal|collinear|symmetric. Returns the constraint name.")]
    public Task<string> AddSketchConstraint(string type, string[] entityIds, CancellationToken ct = default)
        => Call("add_sketch_constraint", new JObject { ["type"] = type, ["entity_ids"] = new JArray(entityIds) }, ct);

    [McpServerTool(Name = "inventor_close_sketch"),
     Description("Finish editing a sketch (exit sketch edit mode). sketch_name optional; defaults to the active sketch. Returns the closed sketch name.")]
    public Task<string> CloseSketch(string? sketchName = null, CancellationToken ct = default)
        => Call("close_sketch", new JObject { ["sketch_name"] = sketchName }, ct);

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
