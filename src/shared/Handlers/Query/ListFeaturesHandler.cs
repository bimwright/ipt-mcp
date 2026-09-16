#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Query;

/// <summary>
/// <c>list_features</c> — read-only (spec F4-P0-3). Lists the active part's features as
/// <c>{name, type, health, suppressed, body_names[]}</c> in feature-tree order.
/// <c>type</c>/<c>health</c> are stripped enum cores ("ExtrudeFeature", "UpToDate").
/// <c>include_health=false</c> omits the <c>health</c> key. <c>body_names</c> lists the bodies the
/// feature produced/affected (empty for features without bodies, partial when the read fails).
/// <c>max_items</c> caps the list; <c>total</c>+<c>truncated</c> report the model count.
/// </summary>
public sealed class ListFeaturesHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_features";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "list_features", out var app, out var part, out var failure))
            return failure!;

        int maxItems = (int?)p["max_items"] ?? 200;
        if (maxItems <= 0) maxItems = 200;
        var includeHealth = p["include_health"] is null || p["include_health"]!.Type == JTokenType.Null || p.Value<bool>("include_health");

        try
        {
            var features = part.ComponentDefinition.Features;
            var arr = new JArray();
            var total = features.Count;
            var i = 0;
            foreach (PartFeature f in features)
            {
                if (i++ >= maxItems) break;
                // One sick feature must not fail the list — emit whatever fields read cleanly.
                var row = new JObject();
                try
                {
                    row["name"] = f.Name;
                    row["type"] = EnumText.Friendly(f.Type.ToString());
                    row["suppressed"] = f.Suppressed;
                }
                catch { /* keep the partial row */ }
                if (includeHealth)
                {
                    string? health = null;
                    try { health = EnumText.Friendly(f.HealthStatus.ToString(), "Health"); } catch { }
                    row["health"] = health;
                }
                // partial when enumeration throws midway — a sick feature still lists what read
                var bodyNames = new JArray();
                try { foreach (SurfaceBody b in f.SurfaceBodies) bodyNames.Add(b.Name); } catch { }
                row["body_names"] = bodyNames;
                arr.Add(row);
            }

            return Ok(ctx, new JObject
            {
                ["features"] = arr,
                ["total"] = total,
                ["truncated"] = total > maxItems,
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
