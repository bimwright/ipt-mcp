#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetFabrication;

/// <summary>
/// <c>sheet_fabrication_info</c> — read-only sheet fabrication part summary. Reports the sheet fabrication style state an
/// agent needs before authoring features: <c>thickness_mm</c>/<c>thickness_expression</c> (the
/// effective <c>Thickness</c> parameter — <c>ModelValue</c> cm → mm), <c>bend_radius_mm</c>,
/// <c>use_sheet_fabrication_style_thickness</c> (false means the part overrides the style),
/// <c>style_name</c>, <c>unfold_method</c>, <c>has_flat_pattern</c> and, once a flat pattern
/// exists, <c>flat_length_mm</c>/<c>flat_width_mm</c> — the numbers a CNC/nesting workflow needs
/// before export_dxf source=flat_pattern. Fails <c>WRONG_DOCUMENT_TYPE</c> on a non-sheet fabrication
/// part. Optional <c>document</c> targets a document already in memory.
/// </summary>
public sealed class SheetFabricationInfoHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_fabrication_info";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetFabricationSupport.TryGetSheetFabricationPart(ctx, p, "sheet_fabrication_info", out var app, out var part, out var fabricationDef, out var failure))
            return failure!;

        var data = new JObject { ["is_sheet_fabrication"] = true };
        try
        {
            try { data["use_sheet_fabrication_style_thickness"] = fabricationDef.UseSheetMetalStyleThickness; } catch { }
            try
            {
                data["thickness_mm"] = Math.Round(UnitConvert.CmToMm(fabricationDef.Thickness.ModelValue), 4);
                data["thickness_expression"] = fabricationDef.Thickness.Expression;
            }
            catch { }
            try
            {
                data["bend_radius_mm"] = Math.Round(UnitConvert.CmToMm(fabricationDef.BendRadius.ModelValue), 4);
                data["bend_radius_expression"] = fabricationDef.BendRadius.Expression;
            }
            catch { }
            try { data["style_name"] = fabricationDef.ActiveSheetMetalStyle?.Name; } catch { }
            try { data["unfold_method"] = fabricationDef.UnfoldMethod?.Name; } catch { }
            try { data["bend_count"] = fabricationDef.Bends.Count; } catch { }
            try { data["has_flat_pattern"] = fabricationDef.HasFlatPattern; } catch { }
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
            data.Merge(SheetFabricationSupport.TopologyCounts(part.ComponentDefinition));
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
        return Ok(ctx, data);
    }
}
#endif
