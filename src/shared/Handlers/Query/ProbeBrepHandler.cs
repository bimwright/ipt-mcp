#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Query;

/// <summary>
/// <c>probe_brep</c> — read-only B-rep port survey (spec F4-P1, WS2 #124/#126). Scans the active
/// part's planar faces for <b>port mouths</b>: faces carrying an inner-loop full-circle edge —
/// the circular opening of a hole/port. For each port it reports the face
/// <c>normal</c> (unit vector, corrected for <see cref="Face.IsParamReversed"/>), the opening
/// <c>center_mm</c>, <c>port_diameter_mm</c> (2 × smallest inner-loop radius), and every
/// full-circle edge on the face (<c>circles[]</c> with per-edge radius/center/inner_loop), so
/// concentric outer rims (a flange mouth) are visible too. Ports are reported <b>per face</b>:
/// a face with several openings (two drilled holes through one plate face) is one port whose
/// <c>port_diameter_mm</c>/<c>center_mm</c> reflect the smallest inner circle — inspect
/// <c>circles[]</c> for the other openings. Only full circles count (arc/slot mouths are out of
/// scope); a cylindrical boss standing on a face has the same topology and is also reported.
/// <c>body</c> optionally scopes to one body ("1"/"body:N"/name);
/// <c>min_diameter_mm</c>/<c>max_diameter_mm</c> filter on port_diameter;
/// <c>max_items</c> (default 200) caps the list, <c>truncated</c> reports overflow.
/// </summary>
public sealed class ProbeBrepHandler : HandlerBase, IInventorCommand
{
    public string Name => "probe_brep";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "probe_brep", out var app, out var part, out var failure))
            return failure!;

        int maxItems;
        try { maxItems = (int?)p["max_items"] ?? 200; }
        catch (Exception) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "max_items must be an integer"); }
        if (maxItems <= 0) maxItems = 200;
        double? minDia = ReadPositive(p, "min_diameter_mm");
        double? maxDia = ReadPositive(p, "max_diameter_mm");
        if (IsPresent(p["min_diameter_mm"]) && minDia is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "min_diameter_mm must be a number > 0");
        if (IsPresent(p["max_diameter_mm"]) && maxDia is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "max_diameter_mm must be a number > 0");
        if (minDia is not null && maxDia is not null && minDia > maxDia)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "min_diameter_mm must be <= max_diameter_mm");

        try
        {
            var def = part.ComponentDefinition;
            var bodies = def.SurfaceBodies;
            SurfaceBody? onlyBody = null;
            if (IsPresent(p["body"]))
                onlyBody = EntityResolver.ResolveBody(def, p["body"]!.ToString());

            var ports = new JArray();
            var bodiesScanned = 0;
            var facesScanned = 0;
            var total = 0;

            for (var b = 1; b <= bodies.Count; b++)
            {
                SurfaceBody body;
                try { body = bodies[b]; } catch { continue; }   // sick body skips, doesn't fail the survey
                if (onlyBody is not null && !ReferenceEquals(body, onlyBody)) continue;
                bodiesScanned++;

                Faces faces;
                try { faces = body.Faces; } catch { continue; }
                for (var f = 1; f <= faces.Count; f++)
                {
                    // One sick face must not fail the survey (house policy: per-item guards).
                    Face face; Plane? plane;
                    JArray circles = new();
                    double smallestInnerCm = double.MaxValue;
                    Point? smallestCenter = null;
                    try
                    {
                        face = faces[f];
                        if (face.SurfaceType != SurfaceTypeEnum.kPlaneSurface || face.Geometry is not Plane pl)
                            continue;
                        plane = pl;

                        // Collect full-circle edges per loop direction (inner loop = an opening).
                        foreach (EdgeLoop loop in face.EdgeLoops)
                        {
                            var inner = !loop.IsOuterEdgeLoop;
                            foreach (Edge e in loop.Edges)
                            {
                                if (e.CurveType != CurveTypeEnum.kCircleCurve || e.Geometry is not Circle c) continue;
                                circles.Add(new JObject
                                {
                                    ["radius_mm"] = Round(UnitConvert.CmToMm(c.Radius), 4),
                                    ["center_mm"] = Pt(c.Center),
                                    ["inner_loop"] = inner,
                                });
                                if (inner && c.Radius < smallestInnerCm)
                                {
                                    smallestInnerCm = c.Radius;
                                    smallestCenter = c.Center;
                                }
                            }
                        }
                    }
                    catch { continue; }
                    facesScanned++;
                    if (smallestCenter is null) continue;   // no circular opening — not a port

                    var portDiaMm = UnitConvert.CmToMm(smallestInnerCm * 2.0);
                    if (minDia is not null && portDiaMm < minDia.Value - 1e-6) continue;
                    if (maxDia is not null && portDiaMm > maxDia.Value + 1e-6) continue;

                    total++;
                    if (ports.Count < maxItems)
                    {
                        double nx, ny, nz;
                        try
                        {
                            nx = plane.Normal.X; ny = plane.Normal.Y; nz = plane.Normal.Z;
                            if (face.IsParamReversed) { nx = -nx; ny = -ny; nz = -nz; }
                        }
                        catch { continue; }
                        ports.Add(new JObject
                        {
                            ["body"] = $"body:{b}",
                            ["body_name"] = SafeName(body),
                            ["face_index"] = f,
                            ["normal"] = new JArray(Round(nx, 6), Round(ny, 6), Round(nz, 6)),
                            ["center_mm"] = Pt(smallestCenter),
                            ["port_diameter_mm"] = Round(portDiaMm, 4),
                            ["circles"] = circles,
                        });
                    }
                }
            }

            return Ok(ctx, new JObject
            {
                ["ports"] = ports,
                ["total"] = total,
                ["truncated"] = total > ports.Count,
                ["bodies_scanned"] = bodiesScanned,
                ["planar_faces_scanned"] = facesScanned,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }

    private static bool IsPresent(JToken? t) => t is not null && t.Type != JTokenType.Null;

    private static double? ReadPositive(JObject p, string key)
    {
        var t = p[key];
        if (t is null || t.Type == JTokenType.Null) return null;
        if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) return null;
        var v = (double)t;
        return (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) ? null : v;
    }

    private static JArray Pt(Point p) =>
        new JArray(Round(UnitConvert.CmToMm(p.X), 3), Round(UnitConvert.CmToMm(p.Y), 3), Round(UnitConvert.CmToMm(p.Z), 3));

    private static double Round(double v, int d) => Math.Round(v, d);

    private static string? SafeName(SurfaceBody body)
    {
        try { return body.Name; } catch { return null; }
    }
}
#endif
