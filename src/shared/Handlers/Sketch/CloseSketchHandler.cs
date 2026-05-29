#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>
/// <c>close_sketch</c> — finish editing a sketch (exit sketch edit mode) and refresh its profiles.
/// sketch_name optional; defaults to the most recently created sketch.
/// </summary>
public sealed class CloseSketchHandler : HandlerBase, IInventorCommand
{
    public string Name => "close_sketch";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "close_sketch", out var app, out var part, out var failure))
            return failure!;

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            try { sketch.ExitEdit(); } catch { /* not in edit mode — fine */ }
            try { sketch.UpdateProfiles(); } catch { /* profiles refresh is best-effort */ }
            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["profile_count"] = sketch.Profiles.Count,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
