#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Document;

/// <summary>
/// <c>set_units</c> — sets the active document's length unit. Accepts common unit names
/// (mm, cm, m, in/inch, ft/foot, mil/thou). Requires an active document.
/// </summary>
public sealed class SetUnitsHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_units";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        string raw = (p["length_unit"]?.Type == JTokenType.String) ? (string)p["length_unit"]! : "";
        if (string.IsNullOrWhiteSpace(raw))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "length_unit is required");

        if (!TryMap(raw, out UnitsTypeEnum unit))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "unsupported length_unit '" + raw + "' (expected one of: mm, cm, m, in, ft, mil)");

        try
        {
            doc.UnitsOfMeasure.LengthUnits = unit;
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to set units: " + ex.Message);
        }

        return Ok(ctx, new JObject { ["length_unit"] = doc.UnitsOfMeasure.LengthUnits.ToString() });
    }

    private static bool TryMap(string raw, out UnitsTypeEnum unit)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "mm":
            case "millimeter":
            case "millimetre":
                unit = UnitsTypeEnum.kMillimeterLengthUnits; return true;
            case "cm":
            case "centimeter":
            case "centimetre":
                unit = UnitsTypeEnum.kCentimeterLengthUnits; return true;
            case "m":
            case "meter":
            case "metre":
                unit = UnitsTypeEnum.kMeterLengthUnits; return true;
            case "in":
            case "inch":
            case "inches":
                unit = UnitsTypeEnum.kInchLengthUnits; return true;
            case "ft":
            case "foot":
            case "feet":
                unit = UnitsTypeEnum.kFootLengthUnits; return true;
            case "mil":
            case "thou":
                unit = UnitsTypeEnum.kMilLengthUnits; return true;
            default:
                unit = UnitsTypeEnum.kMillimeterLengthUnits; return false;
        }
    }
}
#endif
