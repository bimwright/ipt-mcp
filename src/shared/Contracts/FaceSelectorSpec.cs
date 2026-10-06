using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Parsed, validated deterministic face selector (spec §5). API-agnostic: parsing/validation live
/// here so the server and tests compile it without Inventor; geometric resolution happens in the
/// plugin-side FaceSelector. Directions are part-frame axis tokens (+X..-Z). Lengths in mm.
/// </summary>
public sealed class FaceSelectorSpec
{
    public string Kind { get; private set; } = "";        // "planar" | "cylindrical"
    public string? Direction { get; private set; }        // planar: required normal; cylindrical: optional axis
    public string Extreme { get; private set; } = "max";  // planar only: "max" | "min"
    public double ToleranceDeg { get; private set; } = 5.0;
    public double? RadiusMm { get; private set; }          // cylindrical only, required
    public double RadiusTolMm { get; private set; } = 0.01;
    public double[]? NearMm { get; private set; }          // optional [x,y,z] tie-break point

    public static readonly string[] Directions = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };

    public static bool TryParse(JObject? o, out FaceSelectorSpec spec, out string error)
    {
        spec = new FaceSelectorSpec();
        error = "";
        if (o is null) { error = "selector object is required"; return false; }

        var kind = ((string?)o["kind"])?.Trim().ToLowerInvariant();
        if (kind != "planar" && kind != "cylindrical")
        { error = "selector.kind must be 'planar' or 'cylindrical'"; return false; }
        spec.Kind = kind;

        if (o["tolerance_deg"] is not null)
        {
            if (!IsFiniteNumber(o["tolerance_deg"]))
            { error = "selector.tolerance_deg must be a finite number"; return false; }
            spec.ToleranceDeg = (double)o["tolerance_deg"]!;
        }
        if (spec.ToleranceDeg <= 0 || spec.ToleranceDeg >= 90)
        { error = "selector.tolerance_deg must be in (0, 90)"; return false; }

        if (o["near_mm"] is JArray near)
        {
            if (near.Count != 3 || near.Any(t => !IsFiniteNumber(t)))
            { error = "selector.near_mm must be [x,y,z] (3 finite numbers)"; return false; }
            spec.NearMm = near.Select(t => (double)t).ToArray();
        }
        else if (o["near_mm"] is not null)
        { error = "selector.near_mm must be [x,y,z] (3 finite numbers)"; return false; }

        if (kind == "planar")
        {
            var normal = ((string?)o["normal"])?.Trim();
            if (normal is null || !Directions.Contains(normal))
            { error = "selector.normal is required for planar and must be one of " + string.Join("|", Directions); return false; }
            spec.Direction = normal;

            var extreme = (((string?)o["extreme"]) ?? "max").Trim().ToLowerInvariant();
            if (extreme != "max" && extreme != "min")
            { error = "selector.extreme must be 'max' or 'min'"; return false; }
            spec.Extreme = extreme;
        }
        else // cylindrical
        {
            if (o["radius_mm"] is null) { error = "selector.radius_mm is required for cylindrical"; return false; }
            if (!IsFiniteNumber(o["radius_mm"]) || (double)o["radius_mm"]! <= 0)
            { error = "selector.radius_mm must be a finite number > 0"; return false; }
            spec.RadiusMm = (double)o["radius_mm"]!;

            if (o["radius_tol_mm"] is not null)
            {
                if (!IsFiniteNumber(o["radius_tol_mm"]))
                { error = "selector.radius_tol_mm must be a finite number"; return false; }
                spec.RadiusTolMm = (double)o["radius_tol_mm"]!;
            }
            if (spec.RadiusTolMm <= 0) { error = "selector.radius_tol_mm must be > 0"; return false; }

            var axis = ((string?)o["axis"])?.Trim();
            if (axis is not null)
            {
                if (!Directions.Contains(axis))
                { error = "selector.axis must be one of " + string.Join("|", Directions); return false; }
                spec.Direction = axis;
            }
        }
        return true;
    }

    // net48: no double.IsFinite — NaN/Infinity must not pass (an infinite tolerance/radius
    // would silently disable the filter or match the wrong face).
    private static bool IsFiniteNumber(JToken? t) =>
        t is not null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) &&
        !double.IsNaN((double)t) && !double.IsInfinity((double)t);
}
