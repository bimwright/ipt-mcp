#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// Geometric resolution of <see cref="EdgeSelectorSpec"/> against a part definition. Returns the
/// matching edges plus a JSON row per match (<c>edge</c> id, <c>radius_mm</c>, <c>center_mm</c>,
/// <c>adjacent</c> surface types) so callers can verify what was selected before it is filleted.
/// </summary>
public static class EdgeSelector
{
    public sealed class Match
    {
        public Edge Edge { get; set; } = null!;
        public int BodyIndex { get; set; }
        public int EdgeIndex { get; set; }
        public JObject Json { get; set; } = new();
    }

    public static List<Match> Select(PartComponentDefinition def, EdgeSelectorSpec spec)
    {
        var matches = new List<Match>();
        SurfaceBody? onlyBody = null;
        if (spec.OnBody is not null)
            onlyBody = EntityResolver.ResolveBody(def, spec.OnBody);   // throws on bad selector

        var bodies = def.SurfaceBodies;
        for (var b = 1; b <= bodies.Count; b++)
        {
            var body = bodies[b];
            if (onlyBody is not null && !ReferenceEquals(body, onlyBody)) continue;

            var edges = body.Edges;
            for (var e = 1; e <= edges.Count; e++)
            {
                var edge = edges[e];
                var ct = edge.CurveType;
                if (ct != CurveTypeEnum.kCircleCurve && ct != CurveTypeEnum.kCircularArcCurve) continue;
                if (edge.Geometry is not Circle circle) continue;

                var radiusMm = UnitConvert.CmToMm(circle.Radius);
                if (spec.RadiusMm is not null && Math.Abs(radiusMm - spec.RadiusMm.Value) > spec.RadiusTolMm)
                    continue;

                var cx = UnitConvert.CmToMm(circle.Center.X);
                var cy = UnitConvert.CmToMm(circle.Center.Y);
                var cz = UnitConvert.CmToMm(circle.Center.Z);
                if (spec.CenterMm is not null)
                {
                    var dx = cx - spec.CenterMm[0];
                    var dy = cy - spec.CenterMm[1];
                    var dz = cz - spec.CenterMm[2];
                    if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > spec.CenterTolMm) continue;
                }

                var adjacent = new List<string>(2);
                var ok = true;
                foreach (Face f in edge.Faces)
                {
                    var name = SurfaceTypeName(f.SurfaceType);
                    adjacent.Add(name);
                    if (spec.AdjacentSurfaceTypes is not null &&
                        !spec.AdjacentSurfaceTypes.Contains(name, StringComparer.OrdinalIgnoreCase))
                    { ok = false; break; }
                }
                if (!ok) continue;

                matches.Add(new Match
                {
                    Edge = edge,
                    BodyIndex = b,
                    EdgeIndex = e,
                    Json = new JObject
                    {
                        ["edge"] = $"body:{b}/edge:{e}",
                        ["radius_mm"] = Math.Round(radiusMm, 4),
                        ["center_mm"] = new JArray(Math.Round(cx, 3), Math.Round(cy, 3), Math.Round(cz, 3)),
                        ["adjacent"] = new JArray(adjacent),
                    },
                });
            }
        }
        return matches;
    }

    public static string SurfaceTypeName(SurfaceTypeEnum t) => t switch
    {
        SurfaceTypeEnum.kPlaneSurface => "plane",
        SurfaceTypeEnum.kCylinderSurface => "cylinder",
        SurfaceTypeEnum.kConeSurface => "cone",
        SurfaceTypeEnum.kTorusSurface => "torus",
        SurfaceTypeEnum.kSphereSurface => "sphere",
        SurfaceTypeEnum.kBSplineSurface => "bspline",
        SurfaceTypeEnum.kEllipticalCylinderSurface => "elliptical_cylinder",
        SurfaceTypeEnum.kEllipticalConeSurface => "elliptical_cone",
        _ => "unknown",
    };
}
#endif
