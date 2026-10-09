#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>revolve</c> — revolve a named sketch's profile about an axis. The axis is either a sketch line
/// (entity id resolved against the same sketch) or an origin work axis (XAxis|YAxis|ZAxis →
/// "X Axis"/"Y Axis"/"Z Axis"). angle in degrees (→ radians for the API) or an Inventor angle
/// expression. A numeric full 360° revolve uses <c>AddFull</c>; a partial angle or an expression uses
/// <c>AddByAngle</c>. Returns the new feature name.
/// </summary>
public sealed class RevolveHandler : HandlerBase, IInventorCommand
{
    public string Name => "revolve";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "revolve", out var app, out var part, out var failure))
            return failure!;

        var sketchName = (string?)p["sketch_name"];
        var axisId = (string?)p["axis_id"];
        if (string.IsNullOrWhiteSpace(sketchName) || string.IsNullOrWhiteSpace(axisId))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name, axis_id and angle_deg are required");
        if (!RevolveParams.TryParseAngle(p["angle_deg"], out var angle, out var expression, out var angleError))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, angleError);

        return PartMutation.Run(ctx, app, part, Name, () =>
        {
            var def = part.ComponentDefinition;
            var expressionRadians = expression is null ? 0 :
                PartMutation.Evaluate(def.Parameters, expression, part.UnitsOfMeasure.AngleUnits, "angle_deg");
            if (expression is not null && (expressionRadians <= 0 || expressionRadians > 2 * Math.PI))
                throw new ArgumentException("angle_deg expression must evaluate to an angle greater than 0 and at most 360 degrees");
            var profile = FeatureSupport.SolidProfile(def, sketchName!);
            var operation = FeatureSupport.Operation((string?)p["operation"]);
            object axis = ResolveAxis(def, sketchName!, axisId!);

            RevolveFeature feature;
            if (expression is not null)
            {
                // The angle's value can change later, so it can't be routed to AddFull or a
                // direction up front; the expression drives an angle extent in the positive direction.
                feature = def.Features.RevolveFeatures.AddByAngle(
                    profile, axis, expression, PartFeatureExtentDirectionEnum.kPositiveExtentDirection, operation);
            }
            else if (Math.Abs(Math.Abs(angle!.Value) - 360.0) < 1e-6 || angle.Value == 0)
            {
                feature = def.Features.RevolveFeatures.AddFull(profile, axis, operation);
            }
            else
            {
                feature = def.Features.RevolveFeatures.AddByAngle(
                    profile, axis, UnitConvert.DegToRad(Math.Abs(angle!.Value)),
                    angle.Value < 0 ? PartFeatureExtentDirectionEnum.kNegativeExtentDirection
                                 : PartFeatureExtentDirectionEnum.kPositiveExtentDirection,
                    operation);
            }

            if (!part.Update2(false) || feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("Revolve recompute failed");
            var data = new JObject
            {
                ["feature_name"] = feature.Name,
            };
            if (expression is null) data["angle_deg"] = angle!.Value;
            else
            {
                if (feature.Extent is not AngleExtent extent)
                    throw new InvalidOperationException("Revolve did not retain an angle extent for the expression");
                var actual = Convert.ToDouble(extent.Angle.ModelValue);
                if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(actual - expressionRadians) > 1e-8)
                    throw new InvalidOperationException("Revolve angle readback does not match the requested expression");
                data["angle_expression"] = extent.Angle.Expression;
            }
            return Ok(ctx, data);
        });
    }

    private static object ResolveAxis(PartComponentDefinition def, string sketchName, string axisId)
    {
        switch (axisId.Trim().ToUpperInvariant())
        {
            case "XAXIS": case "X": return def.WorkAxes["X Axis"];
            case "YAXIS": case "Y": return def.WorkAxes["Y Axis"];
            case "ZAXIS": case "Z": return def.WorkAxes["Z Axis"];
        }
        // sketch-line entity id within the revolved sketch
        var sketch = EntityResolver.FindSketch(def, sketchName)
            ?? throw new ArgumentException($"no sketch named '{sketchName}'");
        var entity = EntityResolver.ResolveSketchEntity(sketch, axisId);
        if (entity is SketchLine line) return line;
        throw new ArgumentException($"axis_id '{axisId}' is not a sketch line (use a line entity id or XAxis|YAxis|ZAxis)");
    }
}
#endif
