#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>fillet</c> — add a constant-radius edge fillet over the given model edge ids. radius in mm
/// (→ cm). Uses <c>FilletFeatures.AddSimple</c> (the simple all-defaults overload), which takes an
/// <c>EdgeCollection</c> built from <c>TransientObjects.CreateEdgeCollection()</c>. Returns the new
/// fillet feature name.
/// </summary>
public sealed class FilletHandler : HandlerBase, IInventorCommand
{
    public string Name => "fillet";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "fillet", out var app, out var part, out var failure))
            return failure!;

        if (p["edge_ids"] is not JArray edgeIds || edgeIds.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "edge_ids[] is required and must be non-empty");
        if (p["radius_mm"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "radius_mm is required");
        var radiusMm = p.Value<double>("radius_mm");
        if (radiusMm <= 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "radius_mm must be greater than 0");

        try
        {
            var def = part.ComponentDefinition;
            var edges = FeatureSupport.EdgeCollection(app, def, edgeIds);
            var feature = def.Features.FilletFeatures.AddSimple(
                edges,
                UnitConvert.MmToCm(radiusMm),
                AllFillets: false, AllRounds: false,
                AutomaticEdgeChain: true,
                RollAlongSharpEdges: true,
                RollingBallWherePossible: true,
                PreserveAllFeatures: false);
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["radius_mm"] = radiusMm,
                ["edge_count"] = edgeIds.Count,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
