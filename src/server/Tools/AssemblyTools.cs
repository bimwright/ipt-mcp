using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

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
    public Task<string> PlaceOccurrence(string path, bool grounded = false,
        double[]? positionMm = null, double[]? rotationDegXyz = null, CancellationToken ct = default)
    {
        if (positionMm is { Length: not 3 }) return Task.FromResult(Error("INVALID_ARGUMENT", "position_mm must be [x,y,z]"));
        if (rotationDegXyz is { Length: not 3 }) return Task.FromResult(Error("INVALID_ARGUMENT", "rotation_deg_xyz must be [rx,ry,rz]"));
        return Call("place_occurrence", new JObject
        {
            ["path"] = path,
            ["grounded"] = grounded,
            ["position_mm"] = positionMm is null ? null : new JArray(positionMm),
            ["rotation_deg_xyz"] = rotationDegXyz is null ? null : new JArray(rotationDegXyz),
        }, ct);
    }

    [McpServerTool(Name = "inventor_add_constraint"),
     Description("Add an assembly constraint between two refs. type=mate|flush|insert|angle. Each side: occurrence name (omit/empty = the assembly document itself — its origin planes/axes) + ref name (iMate → work feature → origin name resolution). offset_mm applies to mate/flush and insert distance; angle_deg required for type=angle; insert_opposed flips insert axis sense. IMPORTANT: a solver-sick constraint does NOT throw — ALWAYS check `health` in the response (expect 'up_to_date'). Repairing/deleting a sick constraint currently requires send_code or the Inventor UI.")]
    public Task<string> AddConstraint(string type, string aRef, string bRef,
        string? aOccurrence = null, string? bOccurrence = null,
        double offsetMm = 0, double? angleDeg = null, bool insertOpposed = true,
        CancellationToken ct = default)
    {
        var t = (type ?? "").Trim().ToLowerInvariant();
        if (t is not ("mate" or "flush" or "insert" or "angle"))
            return Task.FromResult(Error("INVALID_ARGUMENT", "type must be mate|flush|insert|angle"));
        if (t == "angle" && angleDeg is null)
            return Task.FromResult(Error("INVALID_ARGUMENT", "angle_deg is required for type=angle"));
        return Call("add_constraint", new JObject
        {
            ["type"] = t,
            ["a_occurrence"] = aOccurrence, ["a_ref"] = aRef,
            ["b_occurrence"] = bOccurrence, ["b_ref"] = bRef,
            ["offset_mm"] = offsetMm, ["angle_deg"] = angleDeg,
            ["insert_opposed"] = insertOpposed,
        }, ct);
    }

    [McpServerTool(Name = "inventor_create_imate"),
     Description("Create a NAMED iMate on the ACTIVE PART using a deterministic face selector (no face indexes). name convention IF_* (e.g. IF_MATE_TOP). type=mate|flush|insert. selector_kind=planar (requires normal=+X|-X|+Y|-Y|+Z|-Z and extreme=max|min) or cylindrical (requires radius_mm; optional axis). near_mm=[x,y,z] disambiguates ties. If the selector matches 0 or >1 faces the error lists the candidates — refine with near_mm, never guess. Does not save the document.")]
    public Task<string> CreateIMate(string name, string type, string selectorKind,
        string? normal = null, string extreme = "max", double? radiusMm = null, string? axis = null,
        double[]? nearMm = null, double toleranceDeg = 5.0, double radiusTolMm = 0.01,
        double offsetMm = 0, bool insertOpposed = true, double distanceMm = 0,
        CancellationToken ct = default)
    {
        var selector = new JObject { ["kind"] = selectorKind };
        if (normal is not null) selector["normal"] = normal;
        if (selectorKind?.Trim().ToLowerInvariant() == "planar") selector["extreme"] = extreme;
        if (radiusMm is not null) selector["radius_mm"] = radiusMm;
        if (axis is not null) selector["axis"] = axis;
        if (nearMm is not null)
        {
            if (nearMm.Length != 3) return Task.FromResult(Error("INVALID_ARGUMENT", "near_mm must be [x,y,z]"));
            selector["near_mm"] = new JArray(nearMm);
        }
        selector["tolerance_deg"] = toleranceDeg;
        selector["radius_tol_mm"] = radiusTolMm;

        if (!FaceSelectorSpec.TryParse(selector, out _, out var err))
            return Task.FromResult(Error("INVALID_ARGUMENT", err));

        return Call("create_imate", new JObject
        {
            ["name"] = name, ["type"] = type, ["selector"] = selector,
            ["offset_mm"] = offsetMm, ["insert_opposed"] = insertOpposed, ["distance_mm"] = distanceMm,
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
