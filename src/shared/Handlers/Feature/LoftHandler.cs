#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>loft</c> — loft an ordered list of sketch profiles (spec F4-P2-2). Sections resolve via
/// <see cref="FeatureSupport.ProfileOf"/> ("SketchName" or "SketchName:N"); optional
/// <c>centerline</c> sketch drives <c>kLoftWithCenterline</c>. Guide rails, section conditions and
/// area-graph sections are deferred (no run evidence). STA-bound.
/// </summary>
public sealed class LoftHandler : HandlerBase, IInventorCommand
{
    public string Name => "loft";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "loft", out var app, out var part, out var failure))
            return failure!;

        var profilesTok = p["profiles"] as JArray;
        if (profilesTok is null || profilesTok.Count < 2)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "profiles requires at least 2 sketch names (ordered loft sections)");

        PartFeatureOperationEnum operation;
        try { operation = FeatureSupport.Operation((string?)p["operation"]); }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        try
        {
            var def = part.ComponentDefinition;
            var sections = app.TransientObjects.CreateObjectCollection();
            foreach (var t in profilesTok)
                sections.Add(FeatureSupport.ProfileOf(def, t.ToString()));

            var loftDef = def.Features.LoftFeatures.CreateLoftDefinition(sections, operation);
            loftDef.MergeTangentFaces = p["merge_tangent_faces"]?.Value<bool>() ?? true;
            loftDef.Closed = p["closed"]?.Value<bool>() ?? false;

            var centerlineName = (string?)p["centerline"];
            if (!string.IsNullOrWhiteSpace(centerlineName))
            {
                var clSketch = Bimwright.Ipt.Shared.Handlers.EntityResolver.FindSketch(def, centerlineName.Trim())
                    ?? throw new ArgumentException($"no sketch named '{centerlineName}'");
                var curve = FeatureSupport.FirstSketchCurve(clSketch, "centerline", centerlineName);
                // LoftType is read-only — assigning Centerline flips the definition to a
                // centerline loft automatically.
                loftDef.Centerline = curve;
            }

            Inventor.LoftFeature feature;
            try { feature = def.Features.LoftFeatures.Add(loftDef); }
            catch (Exception ex)
            {
                return Fail(ctx, InventorErrorCodes.API_ERROR,
                    ex.Message + " — loft Add rejected the geometry; check section ordering, " +
                    "self-intersection, or try merge_tangent_faces=false");
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
                ["section_count"] = sections.Count,
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
