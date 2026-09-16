#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>extrude</c> — extrude a named sketch's profile. distance_mm accepts a number (mm) or an
/// expression string ("40 mm", "plate_thk"); operation=join|cut|intersect|new_body;
/// direction=positive|negative|symmetric; optional <c>name</c> renames the created feature;
/// <c>affected_bodies</c> ([id|name]) scopes join/cut on multi-body parts (rejected with
/// new_body). STA-bound. Returns the new feature name and the resulting body volume in mm^3.
/// </summary>
public sealed class ExtrudeHandler : HandlerBase, IInventorCommand
{
    public string Name => "extrude";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "extrude", out var app, out var part, out var failure))
            return failure!;

        var sketchName = (string?)p["sketch_name"];
        if (string.IsNullOrWhiteSpace(sketchName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name and distance_mm are required");
        if (!ExtrudeParams.TryParseDistance(p["distance_mm"], out var distanceMm, out var expression, out var distError))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, distError);

        var operationStr = (string?)p["operation"];
        var abToken = p["affected_bodies"];
        if (abToken is not null && abToken is not JArray)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "affected_bodies must be an array of body ids or names");
        var affectedTokens = abToken as JArray;
        if (ExtrudeParams.TryRejectIncompatible(operationStr, affectedTokens, out var incompatError))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, incompatError);

        try
        {
            var def = part.ComponentDefinition;
            var profile = FeatureSupport.SolidProfile(def, sketchName!);
            var operation = FeatureSupport.Operation(operationStr);
            var direction = FeatureSupport.Direction((string?)p["direction"]);

            // AddByDistanceExtent(Profile, Distance, ExtentDirection, Operation, Taper) —
            // Distance is a variant: cm double or an expression string evaluated in the
            // document's units. The interop has no AffectedBodies overload, so the multi-body
            // path goes through ExtrudeDefinition (AffectedBodies is a settable property there).
            object distanceArg = expression ?? (object)UnitConvert.MmToCm(distanceMm!.Value);
            ExtrudeFeature feature;
            if (affectedTokens is { Count: > 0 })
            {
                var col = app.TransientObjects.CreateObjectCollection();
                foreach (var t in affectedTokens)
                    col.Add(Bimwright.Ipt.Shared.Handlers.EntityResolver.ResolveBody(def, t.ToString()));
                var extDef = def.Features.ExtrudeFeatures.CreateExtrudeDefinition(profile, operation);
                extDef.SetDistanceExtent(distanceArg, direction);
                extDef.AffectedBodies = col;
                feature = def.Features.ExtrudeFeatures.Add(extDef);
            }
            else
            {
                feature = def.Features.ExtrudeFeatures.AddByDistanceExtent(
                    profile, distanceArg, direction, operation, 0.0);
            }

            var requestedName = (string?)p["name"];
            string? bodyName = null;
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                feature.Name = requestedName;
                // new_body leaves the SurfaceBody auto-named — name it "<feature>_body" so
                // affected_bodies can address it by name later. Inventor shares one browser
                // namespace: naming the body the same as the feature silently no-ops, and
                // feature.SurfaceBodies proxies reject Name, so rename through def.SurfaceBodies
                // (the new body is appended last). body_name echoes the real name.
                if (operation == PartFeatureOperationEnum.kNewBodyOperation && def.SurfaceBodies.Count > 0)
                {
                    var body = def.SurfaceBodies[def.SurfaceBodies.Count];
                    try { body.Name = requestedName + "_body"; } catch { }
                    bodyName = body.Name;
                }
            }

            var data = new JObject
            {
                ["feature_name"] = feature.Name,
                ["operation"] = (operationStr ?? "join").Trim().ToLowerInvariant(),
                ["volume_mm3"] = BodyVolumeMm3(def),
            };
            if (bodyName is not null)
                data["body_name"] = bodyName;
            if (expression is not null)
                data["distance_expression"] = expression;
            else
                data["distance_mm"] = distanceMm!.Value;
            if (affectedTokens is { Count: > 0 })
            {
                var resolved = new JArray();
                foreach (var t in affectedTokens)
                    resolved.Add(t.ToString());
                data["affected_bodies"] = resolved;
            }
            return Ok(ctx, data);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }

    private static double BodyVolumeMm3(PartComponentDefinition def)
    {
        try
        {
            // SurfaceBody.Volume is a parameterized accessor (PrecisionPercent); call get_Volume
            // directly. Precision 0.0 is rejected with E_INVALIDARG — 0.01 asks for ≤1% rel. error.
            double cm3 = 0;
            foreach (SurfaceBody b in def.SurfaceBodies) cm3 += b.get_Volume(0.01);
            return UnitConvert.Cm3ToMm3(cm3);
        }
        catch { return 0; }
    }
}
#endif
