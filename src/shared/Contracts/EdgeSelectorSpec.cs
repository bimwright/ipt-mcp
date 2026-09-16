using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Parsed, validated deterministic edge selector for fillet/chamfer-style tools (spec F4 P1).
/// API-agnostic: parsing/validation live here so tests compile it without Inventor; geometric
/// resolution happens in the plugin-side EdgeSelector. Lengths in mm.
/// </summary>
public sealed class EdgeSelectorSpec
{
    public string Kind { get; private set; } = "";             // "circular" (only kind so far)
    public double? RadiusMm { get; private set; }              // circle radius; null = any
    public double RadiusTolMm { get; private set; } = 0.01;
    public double[]? CenterMm { get; private set; }            // [x,y,z] circle-center filter
    public double CenterTolMm { get; private set; } = 0.5;
    public string? OnBody { get; private set; }                // body selector: "1" | "body:N" | name
    public string[]? AdjacentSurfaceTypes { get; private set; } // both adjacent faces must be in the set

    /// <summary>Friendly surface names ↔ <c>SurfaceTypeEnum</c> (mapping lives plugin-side).</summary>
    public static readonly string[] SurfaceTypes =
    {
        "plane", "cylinder", "cone", "torus", "sphere", "bspline",
        "elliptical_cylinder", "elliptical_cone", "unknown",
    };

    public static bool TryParse(JToken? t, out EdgeSelectorSpec spec, out string error)
    {
        spec = new EdgeSelectorSpec();
        error = "";
        if (t is not JObject o) { error = "edges must be an array of edge ids or a selector object"; return false; }

        var kind = ((string?)o["kind"])?.Trim().ToLowerInvariant();
        if (kind != "circular")
        { error = "edges.kind must be 'circular'"; return false; }
        spec.Kind = kind;

        if (o["radius_mm"] is not null)
        {
            if (!TryPositive(o["radius_mm"], "edges.radius_mm", out var r, out error)) return false;
            spec.RadiusMm = r;
        }
        if (o["radius_tol_mm"] is not null)
        {
            if (!TryPositive(o["radius_tol_mm"], "edges.radius_tol_mm", out var rt, out error)) return false;
            spec.RadiusTolMm = rt;
        }

        if (o["center_mm"] is not null)
        {
            if (o["center_mm"] is not JArray c || c.Count != 3 ||
                c.Any(x => !IsFiniteNumber(x)))
            { error = "edges.center_mm must be [x,y,z] (3 finite numbers)"; return false; }
            spec.CenterMm = c.Select(x => (double)x).ToArray();
        }
        if (o["center_tol_mm"] is not null)
        {
            if (!TryPositive(o["center_tol_mm"], "edges.center_tol_mm", out var ct, out error)) return false;
            spec.CenterTolMm = ct;
        }

        if (o["on_body"] is not null)
        {
            var b = ((string?)o["on_body"])?.Trim();
            if (string.IsNullOrEmpty(b)) { error = "edges.on_body must be a body selector (\"1\", \"body:N\", or a body name)"; return false; }
            spec.OnBody = b;
        }

        if (o["adjacent_surface_types"] is not null)
        {
            if (o["adjacent_surface_types"] is not JArray arr || arr.Count == 0 ||
                arr.Any(x => x.Type != JTokenType.String))
            { error = "edges.adjacent_surface_types must be a non-empty array of " + string.Join("|", SurfaceTypes); return false; }
            var types = arr.Select(x => ((string)x!).Trim().ToLowerInvariant()).ToArray();
            var bad = types.FirstOrDefault(x => !SurfaceTypes.Contains(x));
            if (bad is not null)
            { error = $"edges.adjacent_surface_types: unknown surface type '{bad}' (" + string.Join("|", SurfaceTypes) + ")"; return false; }
            spec.AdjacentSurfaceTypes = types;
        }
        return true;
    }

    // net48: no double.IsFinite — NaN/Infinity must not pass (an infinite tolerance would
    // silently disable the filter at resolution time).
    private static bool IsFiniteNumber(JToken? t) =>
        t is not null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) &&
        !double.IsNaN((double)t) && !double.IsInfinity((double)t);

    private static bool TryPositive(JToken? t, string name, out double value, out string error)
    {
        value = 0;
        error = "";
        if (!IsFiniteNumber(t)) { error = $"{name} must be a finite number"; return false; }
        value = (double)t!;
        if (value <= 0) { error = $"{name} must be > 0"; return false; }
        return true;
    }
}
