#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>
/// <c>draw_text</c> — add a fitted text box to the target sketch (spec F4-P3). <c>text</c> +
/// <c>position</c> [x,y] mm; optional <c>font_size_mm</c> wraps the text in a
/// <c>&lt;StyleOverride FontSize='N mm'&gt;</c>, <c>rotation_deg</c> applies post-create
/// (<c>TextBox.Rotation</c> is radians). Boxed text (<c>AddByRectangle</c>) deferred.
/// </summary>
public sealed class DrawTextHandler : HandlerBase, IInventorCommand
{
    public string Name => "draw_text";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "draw_text", out var app, out var part, out var failure))
            return failure!;

        var text = (string?)p["text"];
        if (string.IsNullOrEmpty(text))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "text is required");
        var posTok = p["position"];
        if (posTok is not JArray pos || pos.Count != 2)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "position [x,y] (mm) is required");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            var origin = SketchSupport.Pt(app, pos[0].Value<double>(), pos[1].Value<double>());

            var formatted = text;
            if (p["font_size_mm"] is { } fs)
            {
                var sizeMm = fs.Value<double>();
                if (sizeMm <= 0 || double.IsNaN(sizeMm) || double.IsInfinity(sizeMm))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "font_size_mm must be a positive finite number");
                formatted = $"<StyleOverride FontSize='{sizeMm} mm'>{text}</StyleOverride>";
            }

            double? rotationDeg = null;
            if (p["rotation_deg"] is { } rot)
            {
                var deg = rot.Value<double>();
                var quadrant = Math.Round(deg / 90.0);
                if (Math.Abs(deg - quadrant * 90.0) > 1e-6)
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        "rotation_deg must be a multiple of 90 — Inventor's TextBox.Rotation rejects arbitrary angles");
                rotationDeg = quadrant * 90.0;
            }

            var tb = sketch.TextBoxes.AddFitted(origin, formatted, Type.Missing);
            if (rotationDeg is { } rd)
                tb.Rotation = Math.Round(rd / 90.0) * (Math.PI / 2.0);

            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["text"] = text,
                ["position_mm"] = new JArray(pos[0].Value<double>(), pos[1].Value<double>()),
                ["rotation_deg"] = rotationDeg,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
