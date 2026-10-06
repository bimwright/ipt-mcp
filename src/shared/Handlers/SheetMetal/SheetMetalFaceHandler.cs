#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Feature;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>sm_face</c> — build a sheet-metal face (the base plate) from a closed sketch profile.
/// <c>sketch</c> is a sketch name or <c>"Sketch:N"</c> profile spec resolved by
/// <see cref="FeatureSupport.ProfileOf"/>. Uses <c>FaceFeatures.CreateFaceFeatureDefinition</c> →
/// <c>Add</c>. Returns the feature name plus fresh face/edge counts (topology ids shift on every
/// feature — re-list before reusing ids).
/// </summary>
public sealed class SheetMetalFaceHandler : HandlerBase, IInventorCommand
{
    public string Name => "sm_face";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetSheetMetalPart(ctx, p, "sm_face", out var app, out var part, out var smDef, out var failure))
            return failure!;

        var sketch = (string?)p["sketch"];
        if (string.IsNullOrWhiteSpace(sketch))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch is required (a sketch name or 'Sketch:N' profile spec)");

        try
        {
            var def = part.ComponentDefinition;
            var smFeatures = SheetMetalSupport.SmFeatures(smDef)
                ?? throw new InvalidOperationException("sheet-metal features are unavailable");
            var profile = FeatureSupport.ProfileOf(def, sketch!);
            var faceDef = smFeatures.FaceFeatures.CreateFaceFeatureDefinition(profile);
            var feature = smFeatures.FaceFeatures.Add(faceDef);

            var data = new JObject { ["feature_name"] = feature.Name };
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
