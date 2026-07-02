using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

public class ConstraintSideDto
{
    [JsonProperty("occurrence")]
    [Description("Name of the component occurrence. Omit or leave empty for the top-level assembly document itself.")]
    public string? Occurrence { get; set; }

    [JsonProperty("ref")]
    [Description("Named reference: iMate name, work feature name, or origin plane/axis name.")]
    public string Ref { get; set; } = "";
}

public class FaceSelectorDto
{
    [JsonProperty("kind")]
    [Description("planar or cylindrical")]
    public string Kind { get; set; } = "";

    [JsonProperty("normal")]
    [Description("planar only: normal direction (+X, -X, +Y, -Y, +Z, -Z)")]
    public string? Normal { get; set; }

    [JsonProperty("extreme")]
    [Description("planar only: extreme position (max or min)")]
    public string Extreme { get; set; } = "max";

    [JsonProperty("radius_mm")]
    [Description("cylindrical only: target radius in mm")]
    public double? RadiusMm { get; set; }

    [JsonProperty("axis")]
    [Description("cylindrical only: optional axis direction (+X, -X, +Y, -Y, +Z, -Z)")]
    public string? Axis { get; set; }

    [JsonProperty("near_mm")]
    [Description("optional tie-break point [x, y, z] in mm")]
    public double[]? NearMm { get; set; }

    [JsonProperty("tolerance_deg")]
    [Description("angular tolerance in degrees for normals (default 5.0)")]
    public double ToleranceDeg { get; set; } = 5.0;

    [JsonProperty("radius_tol_mm")]
    [Description("radius tolerance in mm (default 0.01)")]
    public double RadiusTolMm { get; set; } = 0.01;
}

/// <summary>
/// Assembly composition tools (toolset <c>assembly</c>, write). Relationships-over-coordinates:
/// place occurrences, then constrain them via NAMED refs (iMate name → work feature name → origin
/// names "XY Plane"|"XZ Plane"|"YZ Plane"|"X Axis"|"Y Axis"|"Z Axis"|"Center Point"). Unknown names
/// return INVALID_ARGUMENT with the full list of available names (self-teaching). Lengths mm, angles deg.
/// </summary>
[McpServerToolType]
public sealed class AssemblyTools
{
    private readonly PluginClient _client;
    public AssemblyTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_place_occurrence"),
     Description("Place a component (.ipt/.iam full path) into the ACTIVE ASSEMBLY. position_mm/rotation_deg_xyz give an initial pose only (final position comes from constraints; rotations applied X then Y then Z). grounded pins it. Returns occurrence_name (use it in all later refs) and bbox_mm.")]
    public Task<string> PlaceOccurrence(
        string path,
        bool grounded = false,
        double[]? position_mm = null,
        double[]? rotation_deg_xyz = null,
        CancellationToken ct = default)
    {
        if (position_mm is { Length: not 3 }) return Task.FromResult(Error("INVALID_ARGUMENT", "position_mm must be [x,y,z]"));
        if (rotation_deg_xyz is { Length: not 3 }) return Task.FromResult(Error("INVALID_ARGUMENT", "rotation_deg_xyz must be [rx,ry,rz]"));
        return Call("place_occurrence", new JObject
        {
            ["path"] = path,
            ["grounded"] = grounded,
            ["position_mm"] = position_mm is null ? null : new JArray(position_mm),
            ["rotation_deg_xyz"] = rotation_deg_xyz is null ? null : new JArray(rotation_deg_xyz),
        }, ct);
    }

    [McpServerTool(Name = "inventor_add_constraint"),
     Description("Add an assembly constraint between two refs. type=mate|flush|insert|angle. Each side (a, b) specifies: occurrence (omit/empty = assembly document itself) + ref (iMate → work feature → origin name). offset_mm applies to mate/flush and insert distance; angle_deg required for type=angle; insert_opposed flips insert axis sense. IMPORTANT: a solver-sick constraint does NOT throw — ALWAYS check `health` in the response (expect 'up_to_date'). Repairing/deleting a sick constraint currently requires send_code or the Inventor UI.")]
    public Task<string> AddConstraint(
        string type,
        ConstraintSideDto a,
        ConstraintSideDto b,
        double offset_mm = 0,
        double? angle_deg = null,
        bool insert_opposed = true,
        CancellationToken ct = default)
    {
        var t = (type ?? "").Trim().ToLowerInvariant();
        if (t is not ("mate" or "flush" or "insert" or "angle"))
            return Task.FromResult(Error("INVALID_ARGUMENT", "type must be mate|flush|insert|angle"));
        if (t == "angle" && angle_deg is null)
            return Task.FromResult(Error("INVALID_ARGUMENT", "angle_deg is required for type=angle"));
        if (a is null) return Task.FromResult(Error("INVALID_ARGUMENT", "side a is required"));
        if (b is null) return Task.FromResult(Error("INVALID_ARGUMENT", "side b is required"));
        return Call("add_constraint", new JObject
        {
            ["type"] = t,
            ["a_occurrence"] = a.Occurrence, ["a_ref"] = a.Ref,
            ["b_occurrence"] = b.Occurrence, ["b_ref"] = b.Ref,
            ["offset_mm"] = offset_mm, ["angle_deg"] = angle_deg,
            ["insert_opposed"] = insert_opposed,
        }, ct);
    }

    [McpServerTool(Name = "inventor_create_imate"),
     Description("Create a NAMED iMate on the ACTIVE PART using a deterministic face selector (no face indexes). name convention IF_* (e.g. IF_MATE_TOP). type=mate|flush|insert. selector_kind=planar (requires normal=+X|-X|+Y|-Y|+Z|-Z and extreme=max|min) or cylindrical (requires radius_mm; optional axis). near_mm=[x,y,z] disambiguates ties. If the selector matches 0 or >1 faces the error lists the candidates — refine with near_mm, never guess. Does not save the document.")]
    public Task<string> CreateIMate(
        string name,
        string type,
        FaceSelectorDto selector,
        double offset_mm = 0,
        bool insert_opposed = true,
        double distance_mm = 0,
        CancellationToken ct = default)
    {
        if (selector is null) return Task.FromResult(Error("INVALID_ARGUMENT", "selector is required"));

        var selObj = new JObject { ["kind"] = selector.Kind };
        if (selector.Normal is not null) selObj["normal"] = selector.Normal;
        if (selector.Kind?.Trim().ToLowerInvariant() == "planar") selObj["extreme"] = selector.Extreme;
        if (selector.RadiusMm is not null) selObj["radius_mm"] = selector.RadiusMm;
        if (selector.Axis is not null) selObj["axis"] = selector.Axis;
        if (selector.NearMm is not null)
        {
            if (selector.NearMm.Length != 3) return Task.FromResult(Error("INVALID_ARGUMENT", "selector.near_mm must be [x,y,z]"));
            selObj["near_mm"] = new JArray(selector.NearMm);
        }
        selObj["tolerance_deg"] = selector.ToleranceDeg;
        selObj["radius_tol_mm"] = selector.RadiusTolMm;

        if (!FaceSelectorSpec.TryParse(selObj, out _, out var err))
            return Task.FromResult(Error("INVALID_ARGUMENT", err));

        return Call("create_imate", new JObject
        {
            ["name"] = name, ["type"] = type, ["selector"] = selObj,
            ["offset_mm"] = offset_mm, ["insert_opposed"] = insert_opposed, ["distance_mm"] = distance_mm,
        }, ct);
    }

    private static string Error(string code, string message)
        => JsonConvert.SerializeObject(new { ok = false, error = new { code, message } }, Formatting.Indented);

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
