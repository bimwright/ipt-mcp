#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Feature;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetFabrication;

/// <summary>
/// <c>sheet_fabrication_flat_pattern</c> — create the flat pattern if missing, then report it. Creating a flat
/// pattern changes document state, so this is a write tool. <c>face</c> optionally picks the base
/// face (<c>face:F</c>/<c>body:B/face:F</c>) via <c>Unfold2</c>; without it
/// <c>Unfold()</c> lets Inventor choose. Returns <c>has_flat_pattern</c>,
/// <c>created</c> (false when the flat pattern already existed — no model change happened) and
/// <c>flat_length_mm</c>/<c>flat_width_mm</c> — the blank size a CNC/nesting workflow needs before
/// <c>export_dxf</c> with <c>source=flat_pattern</c>.
/// </summary>
public sealed class SheetFabricationFlatPatternHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_fabrication_flat_pattern";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetFabricationSupport.TryGetSheetFabricationPart(ctx, p, "sheet_fabrication_flat_pattern", out var app, out var part, out var fabricationDef, out var failure))
            return failure!;

        var created = false;
        try
        {
            if (!fabricationDef.HasFlatPattern)
            {
                var faceRef = (string?)p["face"];
                if (!string.IsNullOrWhiteSpace(faceRef))
                {
                    var face = FeatureSupport.ResolveFaceRef(part.ComponentDefinition, faceRef!);
                    fabricationDef.Unfold2(face);
                }
                else
                {
                    fabricationDef.Unfold();
                }
                created = fabricationDef.HasFlatPattern;
            }
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }

        var data = new JObject
        {
            ["has_flat_pattern"] = fabricationDef.HasFlatPattern,
            ["created"] = created,
        };
        if (fabricationDef.HasFlatPattern)
        {
            try
            {
                var fp = fabricationDef.FlatPattern;
                data["flat_length_mm"] = Math.Round(UnitConvert.CmToMm(fp.Length), 3);
                data["flat_width_mm"] = Math.Round(UnitConvert.CmToMm(fp.Width), 3);
            }
            catch { }
        }
        return Ok(ctx, data);
    }
}
#endif
