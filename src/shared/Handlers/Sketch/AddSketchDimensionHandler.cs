#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;

namespace Bimwright.Inventor.Shared.Handlers.Sketch;

/// <summary>
/// <c>add_sketch_dimension</c> — add a driving dimension to a sketch entity and set its value (mm).
/// The dimension <i>kind</i> is inferred from the entity type: a circle/arc gets a radius dimension,
/// a line gets a two-point distance between its endpoints. The Inventor API exposes no "generic"
/// dimension Add, hence this per-type dispatch. The created constraint's <c>.Parameter.Value</c> is
/// in internal cm, so the mm value is converted before assignment.
/// </summary>
public sealed class AddSketchDimensionHandler : HandlerBase, IInventorCommand
{
    public string Name => "add_sketch_dimension";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not PartDocument part)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "add_sketch_dimension requires an active part document");

        var entityId = (string?)p["entity_id"];
        if (string.IsNullOrWhiteSpace(entityId) || p["value_mm"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "entity_id and value_mm are required");
        var valueMm = p.Value<double>("value_mm");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            var entity = EntityResolver.ResolveSketchEntity(sketch, entityId!);
            var dims = sketch.DimensionConstraints;

            // AddRadius/AddTwoPointDistance want a SketchEntity / SketchPoint and return distinct
            // typed constraints with no common DimensionConstraint base in the COM hierarchy, so we
            // grab the driving Parameter per-branch rather than a shared constraint variable.
            Parameter param;
            switch (entity)
            {
                case SketchCircle circle:
                {
                    var tp = TextPoint(app, circle.CenterSketchPoint, circle.Radius);
                    param = dims.AddRadius((SketchEntity)circle, tp, false).Parameter;
                    break;
                }
                case SketchArc arc:
                {
                    var tp = TextPoint(app, arc.CenterSketchPoint, arc.Radius);
                    param = dims.AddRadius((SketchEntity)arc, tp, false).Parameter;
                    break;
                }
                case SketchLine line:
                {
                    var tp = app.TransientGeometry.CreatePoint2d(
                        (line.StartSketchPoint.Geometry.X + line.EndSketchPoint.Geometry.X) / 2.0,
                        (line.StartSketchPoint.Geometry.Y + line.EndSketchPoint.Geometry.Y) / 2.0 + 1.0);
                    param = dims.AddTwoPointDistance(line.StartSketchPoint, line.EndSketchPoint,
                        DimensionOrientationEnum.kAlignedDim, tp, false).Parameter;
                    break;
                }
                default:
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        $"entity '{entityId}' (type {entity.Type}) is not dimensionable by this tool (supports line, circle, arc)");
            }

            // Parameter.Value is in internal cm; drive it to the requested mm value.
            param.Value = UnitConvert.MmToCm(valueMm);
            try { part.Update(); } catch { /* best-effort recompute */ }

            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["dimension_name"] = param.Name,
                ["value_mm"] = valueMm,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }

    private static Point2d TextPoint(Application app, SketchPoint center, double radiusCm) =>
        app.TransientGeometry.CreatePoint2d(center.Geometry.X + radiusCm, center.Geometry.Y + radiusCm);
}
#endif
