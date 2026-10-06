#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Query;

/// <summary>
/// <c>list_faces</c> — read-only face enumeration of the target part's solid bodies. Rows are
/// <c>{id:"body:B/face:F", surface_type, area_mm2, bbox_mm{min,max}, normal?}</c>; ids round-trip
/// into the <c>face:F</c>/<c>body:B/face:F</c> refs accepted by work-plane refs, unwrap_try and
/// future face-addressed tools. <c>surface_type</c> is the lowercase enum core
/// (plane|cylinder|cone|sphere|torus|bspline|elliptical_cylinder|elliptical_cone|…);
/// <c>normal</c> is emitted for planar faces only (unit vector corrected for
/// <see cref="Face.IsParamReversed"/>). <c>body</c> scopes to one body ("1"/"body:N"/name),
/// <c>surface_type</c> filters by the same lowercase names, <c>max_items</c> caps the list
/// (default 200) and <c>truncated</c> reports overflow. One sick face yields a stub row rather
/// than failing the list — the id column must stay aligned with the 1-based collection index.
/// </summary>
public sealed class ListFacesHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_faces";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, "list_faces", out var app, out var part, out var failure))
            return failure!;

        int maxItems;
        try { maxItems = (int?)p["max_items"] ?? 200; }
        catch (Exception) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "max_items must be an integer"); }
        if (maxItems <= 0) maxItems = 200;
        var typeFilter = (p["surface_type"]?.Type == JTokenType.String) ? ((string)p["surface_type"]!).Trim().ToLowerInvariant() : null;

        try
        {
            var def = part.ComponentDefinition;
            var bodies = def.SurfaceBodies;
            var arr = new JArray();
            var total = 0;
            for (var bi = 1; bi <= bodies.Count; bi++)
            {
                SurfaceBody body;
                if (IsPresent(p["body"]))
                {
                    var only = EntityResolver.ResolveBody(def, p["body"]!.ToString());
                    if (!ReferenceEquals(only, bodies[bi])) continue;
                    body = only;
                }
                else body = bodies[bi];

                var faces = body.Faces;
                for (var fi = 1; fi <= faces.Count; fi++)
                {
                    Face f;
                    try { f = faces[fi]; }
                    catch { continue; }

                    var st = SurfaceTypeName(f);
                    if (typeFilter is not null && st != typeFilter) continue;
                    total++;
                    if (arr.Count >= maxItems) continue;

                    var row = new JObject
                    {
                        ["id"] = $"body:{bi}/face:{fi}",
                        ["surface_type"] = st,
                    };
                    try { row["area_mm2"] = Math.Round(UnitConvert.Cm2ToMm2(f.Evaluator.Area), 3); } catch { }
                    if (f.SurfaceType == SurfaceTypeEnum.kPlaneSurface)
                    {
                        try
                        {
                            var n = PlanarNormal(f);
                            if (n is not null) row["normal"] = new JArray(n[0], n[1], n[2]);
                        }
                        catch { }
                    }
                    try
                    {
                        var rb = f.SurfaceBody.RangeBox;
                        row["bbox_mm"] = new JObject
                        {
                            ["min"] = new JArray(UnitConvert.CmToMm(rb.MinPoint.X), UnitConvert.CmToMm(rb.MinPoint.Y), UnitConvert.CmToMm(rb.MinPoint.Z)),
                            ["max"] = new JArray(UnitConvert.CmToMm(rb.MaxPoint.X), UnitConvert.CmToMm(rb.MaxPoint.Y), UnitConvert.CmToMm(rb.MaxPoint.Z)),
                        };
                    }
                    catch { }
                    arr.Add(row);
                }
            }

            return Ok(ctx, new JObject
            {
                ["faces"] = arr,
                ["total"] = total,
                ["truncated"] = total > arr.Count,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }

    internal static string SurfaceTypeName(Face f)
    {
        try
        {
            return EnumText.Friendly(f.SurfaceType.ToString(), "Surface").ToLowerInvariant()
                .Replace("ellipticalcylinder", "elliptical_cylinder")
                .Replace("ellipticalcone", "elliptical_cone");
        }
        catch { return "unknown"; }
    }

    /// <summary>Unit normal of a planar face, flipped when <see cref="Face.IsParamReversed"/>.</summary>
    internal static double[]? PlanarNormal(Face f)
    {
        if (f.Geometry is not Plane pl) return null;
        var nx = pl.Normal.X; var ny = pl.Normal.Y; var nz = pl.Normal.Z;
        if (f.IsParamReversed) { nx = -nx; ny = -ny; nz = -nz; }
        return new[] { nx, ny, nz };
    }

    private static bool IsPresent(JToken? t) => t is not null && t.Type != JTokenType.Null && t.Type != JTokenType.Undefined;
}
#endif
