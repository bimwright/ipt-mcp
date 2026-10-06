#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Feature;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>sm_cut</c> — punch a closed-profile cutout through the sheet. <c>sketch</c> is a sketch name
/// or <c>"Sketch:N"</c> profile spec. Unlike the surveyed Python implementation this actually
/// implements <c>cut_across_bends</c> (<c>CutDefinition.CutAcrossBends</c>, default false) and
/// <c>cut_normal_to_flat</c> (<c>CutDefinition.CutNormalToFlat</c>, default false — true keeps the
/// cut wall normal to the flat pattern, what CNC tooling wants). The extent is through-all in the
/// <c>direction</c> sense (positive|negative, default positive). Returns feature_name + fresh
/// face/edge counts.
/// </summary>
public sealed class SheetMetalCutHandler : HandlerBase, IInventorCommand
{
    public string Name => "sm_cut";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetSheetMetalPart(ctx, p, "sm_cut", out var app, out var part, out var smDef, out var failure))
            return failure!;

        var sketch = (string?)p["sketch"];
        if (string.IsNullOrWhiteSpace(sketch))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch is required (a sketch name or 'Sketch:N' profile spec)");

        var cutAcrossBends = p["cut_across_bends"]?.Value<bool?>() ?? false;
        var cutNormalToFlat = p["cut_normal_to_flat"]?.Value<bool?>() ?? false;
        var dirStr = ((string?)p["direction"] ?? "positive").Trim().ToLowerInvariant();
        if (dirStr is not ("positive" or "negative"))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "direction must be positive|negative");
        var direction = dirStr == "positive"
            ? PartFeatureExtentDirectionEnum.kPositiveExtentDirection
            : PartFeatureExtentDirectionEnum.kNegativeExtentDirection;

        try
        {
            var def = part.ComponentDefinition;
            var smFeatures = SheetMetalSupport.SmFeatures(smDef)
                ?? throw new InvalidOperationException("sheet-metal features are unavailable");
            var profile = FeatureSupport.ProfileOf(def, sketch!);
            var cutDef = smFeatures.CutFeatures.CreateCutDefinition(profile);
            // CutAcrossBends is get-only in the interop: cutting across bends is requested via
            // SetCutAcrossBendsExtent(distance) — the cut depth through the flattened bend zone,
            // which is exactly the sheet thickness.
            if (cutAcrossBends) cutDef.SetCutAcrossBendsExtent(smDef.Thickness.Expression);
            else cutDef.SetThroughAllExtent(direction);
            cutDef.CutNormalToFlat = cutNormalToFlat;
            var feature = smFeatures.CutFeatures.Add(cutDef);

            var data = new JObject
            {
                ["feature_name"] = feature.Name,
                ["cut_across_bends"] = cutAcrossBends,
                ["cut_normal_to_flat"] = cutNormalToFlat,
            };
            var requestedName = (string?)p["name"];
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                try { feature.Name = requestedName; data["feature_name"] = feature.Name; } catch { }
            }
            data.Merge(SheetMetalSupport.TopologyCounts(part.ComponentDefinition));
            return Ok(ctx, data);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
