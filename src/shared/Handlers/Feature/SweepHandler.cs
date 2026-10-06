#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>sweep</c> — sweep a sketch profile along a sketch path (spec F4-P2-3). The path resolves to
/// the path sketch's first curve; <c>SweepFeatures.CreatePath</c> chains connected entities so a
/// multi-segment path resolves to a multi-entity <see cref="Path"/> (<c>path_entity_count</c> in the
/// response). Guide rail/surface and section-twist sweep types are deferred (no run evidence).
/// STA-bound.
/// </summary>
public sealed class SweepHandler : HandlerBase, IInventorCommand
{
    public string Name => "sweep";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "sweep", out var app, out var part, out var failure))
            return failure!;

        var profileName = (string?)p["profile"];
        var pathName = (string?)p["path"];
        if (string.IsNullOrWhiteSpace(profileName) || string.IsNullOrWhiteSpace(pathName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "profile and path sketch names are required");

        PartFeatureOperationEnum operation;
        try { operation = FeatureSupport.Operation((string?)p["operation"]); }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        var orientation = ((string?)p["orientation"] ?? "normal_to_path").Trim().ToLowerInvariant();
        SweepProfileOrientationEnum orient;
        switch (orientation)
        {
            case "normal_to_path": orient = SweepProfileOrientationEnum.kNormalToPath; break;
            case "parallel": orient = SweepProfileOrientationEnum.kParallelToOriginalProfile; break;
            default:
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    $"unknown orientation '{orientation}' (normal_to_path|parallel)");
        }

        try
        {
            var def = part.ComponentDefinition;
            var profile = FeatureSupport.ProfileOf(def, profileName);
            var pathSketch = Bimwright.Ipt.Shared.Handlers.EntityResolver.FindSketch(def, pathName.Trim())
                ?? throw new ArgumentException($"no sketch named '{pathName}'");
            var curve = FeatureSupport.FirstSketchCurve(pathSketch, "path", pathName);
            var path = def.Features.SweepFeatures.CreatePath(curve);
            var sweepDef = def.Features.SweepFeatures.CreateSweepDefinition(
                SweepTypeEnum.kPathSweepType, profile, path, operation);
            sweepDef.ProfileOrientation = orient;

            Inventor.SweepFeature feature;
            try { feature = def.Features.SweepFeatures.Add(sweepDef); }
            catch (Exception ex)
            {
                // The live-verified dominant failure: E_FAIL when the path is coplanar with /
                // inside the profile plane, or the swept shape self-intersects at a sharp turn.
                return Fail(ctx, InventorErrorCodes.API_ERROR,
                    ex.Message + " — sweep Add rejected the geometry; check the profile plane is " +
                    "perpendicular-ish to the path tangent and the section does not self-intersect at turns");
            }

            string? bodyName = null;
            var requestedName = (string?)p["name"];
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                feature.Name = requestedName;
                if (operation == PartFeatureOperationEnum.kNewBodyOperation)
                    bodyName = FeatureSupport.NameNewBody(def, requestedName);
            }

            var data = new JObject
            {
                ["feature_name"] = feature.Name,
                ["operation"] = ((string?)p["operation"] ?? "join").Trim().ToLowerInvariant(),
                ["path_entity_count"] = path.Count,
                ["volume_mm3"] = FeatureSupport.FeatureBodyVolumeMm3(def, feature.SurfaceBodies),
            };
            if (bodyName is not null) data["body_name"] = bodyName;
            return Ok(ctx, data);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
