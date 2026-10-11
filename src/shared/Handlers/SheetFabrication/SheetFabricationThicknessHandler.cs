#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetFabrication;

/// <summary>
/// <c>sheet_fabrication_set_thickness</c> — override the sheet thickness on the part definition. Clear
/// <c>UseSheetMetalStyleThickness</c> so the part stops inheriting the style, then assign the
/// <c>Thickness</c> parameter's <c>Expression</c> — a string is culture-invariant
/// (<c>"2.5 mm"</c>). <c>thickness</c> accepts a number (mm) or an expression string
/// (<c>"3 mm"</c>, <c>"plate_thk"</c>). Returns the applied expression and model value in mm.
/// </summary>
public sealed class SheetFabricationThicknessHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_fabrication_set_thickness";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetFabricationSupport.TryGetSheetFabricationPart(ctx, p, "sheet_fabrication_set_thickness", out var app, out var part, out var fabricationDef, out var failure))
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

        Transaction? transaction = null;
        string? previousExpression = null;
        var previousInheritance = false;
        var editStarted = false;
        try
        {
            previousExpression = fabricationDef.Thickness.Expression;
            previousInheritance = fabricationDef.UseSheetMetalStyleThickness;
            // Batch owns its transaction; a standalone edit owns and aborts its own.
            if (app.TransactionManager.CurrentTransaction is null)
                transaction = app.TransactionManager.StartTransaction((global::Inventor._Document)(object)part, "MCP: sheet_fabrication_set_thickness");
            editStarted = true;
            fabricationDef.UseSheetMetalStyleThickness = false;
            fabricationDef.Thickness.Expression = expr;
            if (!part.Update2(false))
                throw new InvalidOperationException("Inventor could not rebuild the part with the requested thickness");

            var data = new JObject
            {
                ["thickness_expression"] = fabricationDef.Thickness.Expression,
                ["use_sheet_fabrication_style_thickness"] = fabricationDef.UseSheetMetalStyleThickness,
            };
            try { data["thickness_mm"] = Math.Round(UnitConvert.CmToMm(fabricationDef.Thickness.ModelValue), 4); } catch { }
            data.Merge(SheetFabricationSupport.TopologyCounts(part.ComponentDefinition));
            transaction?.End();
            transaction = null;
            return Ok(ctx, data);
        }
        catch (Exception ex)
        {
            return SheetFabricationThicknessRecovery.Fail(ctx, ex, !editStarted && transaction is null ? null : () =>
            {
                if (transaction is not null) transaction.Abort();
                else if (editStarted)
                {
                    // Do not abort a caller's batch when continue_on_error is enabled.
                    fabricationDef.Thickness.Expression = previousExpression!;
                    fabricationDef.UseSheetMetalStyleThickness = previousInheritance;
                    if (!part.Update2(false))
                        throw new InvalidOperationException("Inventor could not rebuild the previous thickness state");
                }
            });
        }
    }
}
#endif
