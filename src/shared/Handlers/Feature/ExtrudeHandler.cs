#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;

namespace Bimwright.Inventor.Shared.Handlers.Feature;

/// <summary>
/// <c>extrude</c> — extrude a named sketch's profile by a distance. distance in mm (→ cm for the API);
/// operation=join|cut|intersect; direction=positive|negative|symmetric. STA-bound. This is the
/// category template referenced by the plan. Returns the new feature name and the resulting body
/// volume in mm^3.
/// </summary>
public sealed class ExtrudeHandler : HandlerBase, IInventorCommand
{
    public string Name => "extrude";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not PartDocument part)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "extrude requires an active part document");

        var sketchName = (string?)p["sketch_name"];
        if (string.IsNullOrWhiteSpace(sketchName) || p["distance_mm"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name and distance_mm are required");
        var distanceMm = p.Value<double>("distance_mm");
        if (distanceMm <= 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "distance_mm must be greater than 0");

        try
        {
            var def = part.ComponentDefinition;
            var profile = FeatureSupport.SolidProfile(def, sketchName!);
            var operation = FeatureSupport.Operation((string?)p["operation"]);
            var direction = FeatureSupport.Direction((string?)p["direction"]);

            // distance_mm -> cm; AddByDistanceExtent wants the distance as Object (variant), taper 0.
            var feature = def.Features.ExtrudeFeatures.AddByDistanceExtent(
                profile,
                UnitConvert.MmToCm(distanceMm),
                direction,
                operation,
                0.0);

            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["operation"] = (string?)p["operation"] ?? "join",
                ["distance_mm"] = distanceMm,
                ["volume_mm3"] = BodyVolumeMm3(def),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }

    private static double BodyVolumeMm3(PartComponentDefinition def)
    {
        try
        {
            // SurfaceBody.Volume is a parameterized accessor (PrecisionPercent); call get_Volume directly.
            double cm3 = 0;
            foreach (SurfaceBody b in def.SurfaceBodies) cm3 += b.get_Volume(0.0);
            return UnitConvert.Cm3ToMm3(cm3);
        }
        catch { return 0; }
    }
}
#endif
