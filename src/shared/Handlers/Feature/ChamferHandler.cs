#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>chamfer</c> — add an equal-distance edge chamfer over the given model edge ids. distance in mm
/// (→ cm). Uses <c>ChamferFeatures.AddUsingDistance</c> with an <c>EdgeCollection</c>. Returns the new
/// chamfer feature name.
/// </summary>
public sealed class ChamferHandler : HandlerBase, IInventorCommand
{
    public string Name => "chamfer";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "chamfer", out var app, out var part, out var failure))
            return failure!;

        if (p["edge_ids"] is not JArray edgeIds || edgeIds.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "edge_ids[] is required and must be non-empty");
        if (p["distance_mm"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "distance_mm is required");
        var distanceMm = p.Value<double>("distance_mm");
        if (distanceMm <= 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "distance_mm must be greater than 0");

        try
        {
            var def = part.ComponentDefinition;
            var edges = FeatureSupport.EdgeCollection(app, def, edgeIds);
            var feature = def.Features.ChamferFeatures.AddUsingDistance(
                edges,
                UnitConvert.MmToCm(distanceMm),
                AutomaticEdgeChain: true,
                CornerSetback: true,
                PreserveAllFeatures: false);
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["distance_mm"] = distanceMm,
                ["edge_count"] = edgeIds.Count,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
