#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>Add a driving line-length or circle/arc-radius dimension, preserving expressions.</summary>
public sealed class AddSketchDimensionHandler : HandlerBase, IInventorCommand
{
    public string Name => "add_sketch_dimension";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, Name, out var app, out var part, out var failure))
            return failure!;
        var entityId = (string?)p["entity_id"];
        if (string.IsNullOrWhiteSpace(entityId))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "entity_id and value_mm are required");
        if (!SketchDimensionParams.TryParseValue(p["value_mm"], out var valueMm, out var expression, out var error))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, error);

        return PartMutation.Run(ctx, app, part, Name, () =>
        {
            var def = part.ComponentDefinition;
            // Use the same default units as Parameter.Expression, including inch documents.
            var expectedCm = expression is null ? UnitConvert.MmToCm(valueMm!.Value)
                : PartMutation.Evaluate(def.Parameters, expression, part.UnitsOfMeasure.LengthUnits, "value_mm");
            if (expectedCm <= 0) throw new ArgumentException("value_mm must evaluate to a value greater than 0");
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            var entity = EntityResolver.ResolveSketchEntity(sketch, entityId!);
            var dims = sketch.DimensionConstraints;
            Parameter param;
            switch (entity)
            {
                case SketchCircle circle:
                    param = dims.AddRadius((SketchEntity)circle, TextPoint(app, circle.CenterSketchPoint, circle.Radius), false).Parameter;
                    break;
                case SketchArc arc:
                    param = dims.AddRadius((SketchEntity)arc, TextPoint(app, arc.CenterSketchPoint, arc.Radius), false).Parameter;
                    break;
                case SketchLine line:
                    var tp = app.TransientGeometry.CreatePoint2d(
                        (line.StartSketchPoint.Geometry.X + line.EndSketchPoint.Geometry.X) / 2.0,
                        (line.StartSketchPoint.Geometry.Y + line.EndSketchPoint.Geometry.Y) / 2.0 + 1.0);
                    param = dims.AddTwoPointDistance(line.StartSketchPoint, line.EndSketchPoint,
                        DimensionOrientationEnum.kAlignedDim, tp, false).Parameter;
                    break;
                default:
                    throw new ArgumentException($"entity '{entityId}' (type {entity.Type}) is not dimensionable by this tool (supports line, circle, arc)");
            }

            if (expression is null) param.Value = expectedCm;
            else param.Expression = expression;
            if (!part.Update2(false) || sketch.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("Sketch dimension recompute failed");
            var actualCm = Convert.ToDouble(param.ModelValue);
            if (double.IsNaN(actualCm) || double.IsInfinity(actualCm) || actualCm <= 0 ||
                Math.Abs(actualCm - expectedCm) > Math.Max(1e-7, Math.Abs(expectedCm) * 1e-8))
                throw new InvalidOperationException("Sketch dimension readback does not match the requested value");
            var data = new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["dimension_name"] = param.Name,
                ["value_mm"] = expression is null ? valueMm!.Value : UnitConvert.CmToMm(actualCm),
            };
            if (expression is not null) data["expression"] = param.Expression;
            return Ok(ctx, data);
        });
    }

    private static Point2d TextPoint(Application app, SketchPoint center, double radiusCm) =>
        app.TransientGeometry.CreatePoint2d(center.Geometry.X + radiusCm, center.Geometry.Y + radiusCm);
}
#endif
