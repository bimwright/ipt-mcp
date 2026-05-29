#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;

namespace Bimwright.Inventor.Shared.Handlers.Feature;

/// <summary>
/// <c>create_work_axis</c> — create a work axis. type:
/// <list type="bullet">
/// <item><c>two_points</c>: refs=[p1,p2] → <c>AddByTwoPoints</c>.</item>
/// <item><c>edge</c>: refs=[edge] → <c>AddByLine</c> (a linear model edge acts as the line).</item>
/// <item><c>plane_intersection</c>: refs=[plane1,plane2] → <c>AddByTwoPlanes</c>.</item>
/// <item><c>normal_to_face_through_point</c>: refs=[face, point] → <c>AddByPointAndPlane</c>.</item>
/// </list>
/// Returns the new work-axis name.
/// </summary>
public sealed class CreateWorkAxisHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_work_axis";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not PartDocument part)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "create_work_axis requires an active part document");

        var type = (p["type"]?.ToString() ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "type is required");
        if (p["refs"] is not JArray refs || refs.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "refs[] is required");

        try
        {
            var def = part.ComponentDefinition;
            var axes = def.WorkAxes;
            WorkAxis wa;

            switch (type)
            {
                case "two_points":
                {
                    if (refs.Count < 2) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "two_points needs refs=[p1,p2]");
                    wa = axes.AddByTwoPoints(
                        FeatureSupport.ResolvePointRef(def, refs[0].ToString()),
                        FeatureSupport.ResolvePointRef(def, refs[1].ToString()),
                        false);
                    break;
                }
                case "edge":
                {
                    var edge = EntityResolver.ResolveEdge(def, refs[0].ToString());
                    wa = axes.AddByLine(edge, false);
                    break;
                }
                case "plane_intersection":
                {
                    if (refs.Count < 2) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "plane_intersection needs refs=[plane1,plane2]");
                    wa = axes.AddByTwoPlanes(
                        FeatureSupport.ResolvePlaneRef(def, refs[0].ToString()),
                        FeatureSupport.ResolvePlaneRef(def, refs[1].ToString()),
                        false);
                    break;
                }
                case "normal_to_face_through_point":
                {
                    if (refs.Count < 2) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "normal_to_face_through_point needs refs=[face, point]");
                    var face = FeatureSupport.ResolveFaceRef(def, refs[0].ToString());
                    var point = FeatureSupport.ResolvePointRef(def, refs[1].ToString());
                    wa = axes.AddByPointAndPlane(point, face, false);
                    break;
                }
                default:
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        $"unknown work axis type '{type}' (two_points|edge|plane_intersection|normal_to_face_through_point)");
            }

            return Ok(ctx, new JObject
            {
                ["work_axis_name"] = wa.Name,
                ["type"] = type,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
