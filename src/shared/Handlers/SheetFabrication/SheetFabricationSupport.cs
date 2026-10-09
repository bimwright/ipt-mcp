#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.SheetFabrication;

/// <summary>
/// Shared sheet fabrication handler helpers: resolves the target part (honoring the optional
/// <c>document</c> parameter) and gates on <see cref="SheetMetalComponentDefinition"/> so a plain
/// part fails with <c>WRONG_DOCUMENT_TYPE</c> instead of a cryptic cast error (precedent:
/// <c>ExportDxfHandler</c>'s flat-pattern branch).
/// </summary>
internal static class SheetFabricationSupport
{
    public static bool TryGetSheetFabricationPart(
        InventorCommandContext ctx,
        JObject? p,
        string commandName,
        out Application app,
        out PartDocument part,
        out SheetMetalComponentDefinition fabricationDef,
        out InventorCommandResult? failure)
    {
        fabricationDef = null!;
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, commandName, out app, out part, out failure))
            return false;

        if (part.ComponentDefinition is not SheetMetalComponentDefinition fabricationPart)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                commandName + " requires a sheet fabrication part document");
            return false;
        }

        fabricationDef = fabricationPart;
        return true;
    }

    /// <summary>
    /// The sheet fabrication feature collections (Face/Flange/Cut/Bend/…). In the interop
    /// <see cref="SheetMetalComponentDefinition.Features"/> is declared <see cref="PartFeatures"/>,
    /// which lacks the sheet fabrication collections — the runtime object also implements the
    /// <see cref="SheetMetalFeatures"/> interface, so a cast exposes them (same late-bound path
    /// Python gateways use). Returns null only if the object model contract breaks.
    /// </summary>
    public static SheetMetalFeatures? FabricationFeatures(SheetMetalComponentDefinition fabricationDef)
        => fabricationDef.Features as SheetMetalFeatures;

    /// <summary>Current face/edge counts across all bodies — returned by every write tool so a
    /// caller can detect that topology ids shifted (the documented staleness caveat).</summary>
    public static JObject TopologyCounts(PartComponentDefinition def)
    {
        var faces = 0;
        var edges = 0;
        try
        {
            foreach (SurfaceBody b in def.SurfaceBodies)
            {
                faces += b.Faces.Count;
                edges += b.Edges.Count;
            }
        }
        catch { }
        return new JObject { ["face_count"] = faces, ["edge_count"] = edges };
    }
}
#endif
