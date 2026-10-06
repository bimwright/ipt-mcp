#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>sm_info</c> — read-only sheet-metal part summary. Reports the sheet-metal style state an
/// agent needs before authoring features: <c>thickness_mm</c>/<c>thickness_expression</c> (the
/// effective <c>Thickness</c> parameter — <c>ModelValue</c> cm → mm), <c>bend_radius_mm</c>,
/// <c>use_sheet_metal_style_thickness</c> (false means the part overrides the style),
/// <c>style_name</c>, <c>unfold_method</c>, <c>has_flat_pattern</c> and, once a flat pattern
/// exists, <c>flat_length_mm</c>/<c>flat_width_mm</c> — the numbers a CNC/nesting workflow needs
/// before export_dxf source=flat_pattern. Fails <c>WRONG_DOCUMENT_TYPE</c> on a non-sheet-metal
/// part. Optional <c>document</c> targets a document already in memory.
/// </summary>
public sealed class SheetMetalInfoHandler : HandlerBase, IInventorCommand
{
    public string Name => "sm_info";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetSheetMetalPart(ctx, p, "sm_info", out var app, out var part, out var smDef, out var failure))
            return failure!;

        var data = new JObject { ["is_sheet_metal"] = true };
        try
        {
            try { data["use_sheet_metal_style_thickness"] = smDef.UseSheetMetalStyleThickness; } catch { }
            try
            {
                data["thickness_mm"] = Math.Round(UnitConvert.CmToMm(smDef.Thickness.ModelValue), 4);
                data["thickness_expression"] = smDef.Thickness.Expression;
            }
            catch { }
            try
            {
                data["bend_radius_mm"] = Math.Round(UnitConvert.CmToMm(smDef.BendRadius.ModelValue), 4);
                data["bend_radius_expression"] = smDef.BendRadius.Expression;
            }
            catch { }
            try { data["style_name"] = smDef.ActiveSheetMetalStyle?.Name; } catch { }
            try { data["unfold_method"] = smDef.UnfoldMethod?.Name; } catch { }
            try { data["bend_count"] = smDef.Bends.Count; } catch { }
            try { data["has_flat_pattern"] = smDef.HasFlatPattern; } catch { }
            if (smDef.HasFlatPattern)
            {
                try
                {
                    var fp = smDef.FlatPattern;
                    data["flat_length_mm"] = Math.Round(UnitConvert.CmToMm(fp.Length), 3);
                    data["flat_width_mm"] = Math.Round(UnitConvert.CmToMm(fp.Width), 3);
                }
                catch { }
            }
            data.Merge(SheetMetalSupport.TopologyCounts(part.ComponentDefinition));
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
        return Ok(ctx, data);
    }
}
#endif
