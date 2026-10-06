#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>create_work_plane</c> — create a work plane. type:
/// <list type="bullet">
/// <item><c>offset</c>: refs=[plane_or_face], offset_mm → <c>AddByPlaneAndOffset</c> (offset in cm).</item>
/// <item><c>three_points</c>: refs=[p1,p2,p3] → <c>AddByThreePoints</c>.</item>
/// <item><c>tangent</c>: refs=[face, plane] → <c>AddByPlaneAndTangent</c>.</item>
/// <item><c>fixed</c> (spec F4): origin/x_axis/y_axis as {{x,y,z}} or [x,y,z] (origin mm, axes
/// unit vectors — normalized by the API), optional name/visible → <c>AddFixed</c>.</item>
/// </list>
/// Returns the new work-plane name.
/// </summary>
public sealed class CreateWorkPlaneHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_work_plane";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "create_work_plane", out var app, out var part, out var failure))
            return failure!;

        var type = (p["type"]?.ToString() ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "type is required (offset|three_points|tangent|fixed)");
        var refs = p["refs"] as JArray;
        if (type is "offset" or "three_points" or "tangent" && (refs is null || refs.Count == 0))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "refs[] is required");

        try
        {
            var def = part.ComponentDefinition;
            var wps = def.WorkPlanes;
            WorkPlane wp;

            switch (type)
            {
                case "offset":
                {
                    if (refs!.Count < 1) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "offset needs refs=[plane_or_face]");
                    if (p["offset_mm"] is null || p["offset_mm"]!.Type == JTokenType.Null)
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "offset type requires offset_mm");
                    var basePlane = FeatureSupport.ResolvePlaneRef(def, refs[0].ToString());
                    wp = wps.AddByPlaneAndOffset(basePlane, UnitConvert.MmToCm(p.Value<double>("offset_mm")), false);
                    break;
                }
                case "three_points":
                {
                    if (refs!.Count < 3) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "three_points needs refs=[p1,p2,p3]");
                    wp = wps.AddByThreePoints(
                        FeatureSupport.ResolvePointRef(def, refs[0].ToString()),
                        FeatureSupport.ResolvePointRef(def, refs[1].ToString()),
                        FeatureSupport.ResolvePointRef(def, refs[2].ToString()),
                        false);
                    break;
                }
                case "tangent":
                {
                    if (refs!.Count < 2) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "tangent needs refs=[face, plane]");
                    var face = FeatureSupport.ResolveFaceRef(def, refs[0].ToString());
                    var plane = FeatureSupport.ResolvePlaneRef(def, refs[1].ToString());
                    // proximity point at the face's evaluator mid-point is overkill here; the default
                    // tangency on the near side is selected by passing the face's point on surface.
                    var prox = face.PointOnFace;
                    wp = wps.AddByPlaneAndTangent(plane, face, prox, false);
                    break;
                }
                case "fixed":
                {
                    // Spec F4-P0-2: AddFixed(OriginPoint, XAxis, YAxis, [Construction]). Origin in
                    // mm→cm; axes are unit vectors (CreateUnitVector normalizes). refs[] unused.
                    if (!Bimwright.Ipt.Shared.Handlers.Vec3Params.TryParse(p["origin"], "origin", out var ox, out var oy, out var oz, out var oe))
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, oe);
                    if (!Bimwright.Ipt.Shared.Handlers.Vec3Params.TryParse(p["x_axis"], "x_axis", out var xx, out var xy, out var xz, out var xe))
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, xe);
                    if (!Bimwright.Ipt.Shared.Handlers.Vec3Params.TryParse(p["y_axis"], "y_axis", out var yx, out var yy, out var yz, out var ye))
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ye);
                    if (Bimwright.Ipt.Shared.Handlers.Vec3Params.AxesDegenerate(new[] { xx, xy, xz }, new[] { yx, yy, yz }))
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "x_axis and y_axis must be non-zero and non-parallel");

                    var tg = app.TransientGeometry;
                    wp = wps.AddFixed(
                        tg.CreatePoint(UnitConvert.MmToCm(ox), UnitConvert.MmToCm(oy), UnitConvert.MmToCm(oz)),
                        tg.CreateUnitVector(xx, xy, xz),
                        tg.CreateUnitVector(yx, yy, yz),
                        false);
                    break;
                }
                default:
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"unknown work plane type '{type}' (offset|three_points|tangent|fixed)");
            }

            var requestedName = (string?)p["name"];
            if (!string.IsNullOrWhiteSpace(requestedName))
                wp.Name = requestedName;
            if (p["visible"] is not null && p["visible"]!.Type != JTokenType.Null)
                wp.Visible = p.Value<bool>("visible");

            return Ok(ctx, new JObject
            {
                ["work_plane_name"] = wp.Name,
                ["type"] = type,
                ["visible"] = wp.Visible,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
