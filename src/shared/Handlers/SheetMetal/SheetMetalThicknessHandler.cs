#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>sm_set_thickness</c> — override the sheet thickness on the part definition. The correct path
/// (the surveyed Python gateway used a comma-decimal locale hack): clear
/// <c>UseSheetMetalStyleThickness</c> so the part stops inheriting the style, then assign the
/// <c>Thickness</c> parameter's <c>Expression</c> — a string is culture-invariant
/// (<c>"2.5 mm"</c>). <c>thickness</c> accepts a number (mm) or an expression string
/// (<c>"3 mm"</c>, <c>"plate_thk"</c>). Returns the applied expression and model value in mm.
/// </summary>
public sealed class SheetMetalThicknessHandler : HandlerBase, IInventorCommand
{
    public string Name => "sm_set_thickness";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetSheetMetalPart(ctx, p, "sm_set_thickness", out var app, out var part, out var smDef, out var failure))
            return failure!;

        var token = p["thickness"] ?? p["thickness_mm"];
        string expr;
        if (token is null || token.Type == JTokenType.Null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "thickness is required (mm number or expression like '2.5 mm')");
        else if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            var t = token.Value<double>();
            if (double.IsNaN(t) || double.IsInfinity(t) || t <= 0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "thickness must be a finite number greater than 0");
            expr = t.ToString("0.####", CultureInfo.InvariantCulture) + " mm";
        }
        else if (token.Type == JTokenType.String)
        {
            expr = ((string)token!).Trim();
            if (expr.Length == 0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "thickness expression is empty");
        }
        else
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "thickness must be a number (mm) or an expression string");

        try
        {
            smDef.UseSheetMetalStyleThickness = false;
            smDef.Thickness.Expression = expr;
            part.Update();

            var data = new JObject
            {
                ["thickness_expression"] = smDef.Thickness.Expression,
                ["use_sheet_metal_style_thickness"] = smDef.UseSheetMetalStyleThickness,
            };
            try { data["thickness_mm"] = Math.Round(UnitConvert.CmToMm(smDef.Thickness.ModelValue), 4); } catch { }
            data.Merge(SheetMetalSupport.TopologyCounts(part.ComponentDefinition));
            return Ok(ctx, data);
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
