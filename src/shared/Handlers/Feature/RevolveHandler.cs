#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;

namespace Bimwright.Inventor.Shared.Handlers.Feature;

/// <summary>
/// <c>revolve</c> — revolve a named sketch's profile about an axis. The axis is either a sketch line
/// (entity id resolved against the same sketch) or an origin work axis (XAxis|YAxis|ZAxis →
/// "X Axis"/"Y Axis"/"Z Axis"). angle in degrees (→ radians for the API). A full 360° revolve uses
/// <c>AddFull</c>; a partial angle uses <c>AddByAngle</c>. Returns the new feature name.
/// </summary>
public sealed class RevolveHandler : HandlerBase, IInventorCommand
{
    public string Name => "revolve";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not PartDocument part)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "revolve requires an active part document");

        var sketchName = (string?)p["sketch_name"];
        var axisId = (string?)p["axis_id"];
        if (string.IsNullOrWhiteSpace(sketchName) || string.IsNullOrWhiteSpace(axisId) || p["angle_deg"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name, axis_id and angle_deg are required");
        var angleDeg = p.Value<double>("angle_deg");

        try
        {
            var def = part.ComponentDefinition;
            var profile = FeatureSupport.SolidProfile(def, sketchName!);
            var operation = FeatureSupport.Operation((string?)p["operation"]);
            object axis = ResolveAxis(def, sketchName!, axisId!);

            RevolveFeature feature;
            if (Math.Abs(Math.Abs(angleDeg) - 360.0) < 1e-6 || angleDeg == 0)
            {
                feature = def.Features.RevolveFeatures.AddFull(profile, axis, operation);
            }
            else
            {
                feature = def.Features.RevolveFeatures.AddByAngle(
                    profile, axis, UnitConvert.DegToRad(Math.Abs(angleDeg)),
                    angleDeg < 0 ? PartFeatureExtentDirectionEnum.kNegativeExtentDirection
                                 : PartFeatureExtentDirectionEnum.kPositiveExtentDirection,
                    operation);
            }

            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["angle_deg"] = angleDeg,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
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
