#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Properties;

/// <summary>
/// <c>get_mass_properties</c> — read-only. Reports mass (g), volume (mm^3), surface area (mm^2),
/// centre of mass (mm), and the model bounding box (mm) of the active part document.
/// <para>
/// Inventor's API works in internal database units: mass in <b>kg</b>, lengths in <b>cm</b>,
/// volume in <b>cm^3</b>, area in <b>cm^2</b>. We convert at the boundary: kg→g (×1000),
/// cm→mm (<see cref="UnitConvert.CmToMm"/>), cm^3→mm^3 (<see cref="UnitConvert.Cm3ToMm3"/>),
/// cm^2→mm^2 (×100).
/// </para>
/// </summary>
public sealed class GetMassPropertiesHandler : HandlerBase, IInventorCommand
{
    public string Name => "get_mass_properties";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? activeDoc;
        try { activeDoc = app.ActiveDocument; } catch { activeDoc = null; }
        if (activeDoc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        if (activeDoc is not PartDocument doc)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "get_mass_properties requires an active part document");

        try
        {
            PartComponentDefinition def = doc.ComponentDefinition;
            MassProperties mp = def.MassProperties;

            double massKg = mp.Mass;
            double volumeCm3 = mp.Volume;
            double areaCm2 = mp.Area;

            var com = new JObject();
            try
            {
                Point c = mp.CenterOfMass;
                com["x"] = UnitConvert.CmToMm(c.X);
                com["y"] = UnitConvert.CmToMm(c.Y);
                com["z"] = UnitConvert.CmToMm(c.Z);
            }
            catch { /* leave empty if unavailable */ }

            var bbox = new JObject();
            try
            {
                Box rb = def.RangeBox;
                bbox["min"] = new JObject
                {
                    ["x"] = UnitConvert.CmToMm(rb.MinPoint.X),
                    ["y"] = UnitConvert.CmToMm(rb.MinPoint.Y),
                    ["z"] = UnitConvert.CmToMm(rb.MinPoint.Z),
                };
                bbox["max"] = new JObject
                {
                    ["x"] = UnitConvert.CmToMm(rb.MaxPoint.X),
                    ["y"] = UnitConvert.CmToMm(rb.MaxPoint.Y),
                    ["z"] = UnitConvert.CmToMm(rb.MaxPoint.Z),
                };
            }
            catch { /* leave empty if unavailable */ }

            return Ok(ctx, new JObject
            {
                ["mass_g"] = massKg * 1000.0,
                ["volume_mm3"] = UnitConvert.Cm3ToMm3(volumeCm3),
                ["area_mm2"] = areaCm2 * 100.0,
                ["center_of_mass_mm"] = com,
                ["bounding_box_mm"] = bbox,
            });
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to compute mass properties: " + ex.Message);
        }
    }
}
#endif
