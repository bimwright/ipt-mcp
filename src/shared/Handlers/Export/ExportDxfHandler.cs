#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// <c>export_dxf</c> — writes a 2D DXF (.dxf). Because Phase 1 ships no drawing tools, the DXF source
/// must be declared explicitly: <c>source=sketch</c> (a named planar sketch on the active part) or
/// <c>source=flat_pattern</c> (the active sheet-metal part's flat pattern). The DXF is produced through
/// the relevant entity's <c>DataIO.WriteDataToFile</c>. If the requested source is not available on the
/// active document the handler returns <c>WRONG_DOCUMENT_TYPE</c> or <c>INVALID_ARGUMENT</c>.
/// </summary>
public sealed class ExportDxfHandler : HandlerBase, IInventorCommand
{
    public string Name => "export_dxf";
    public bool IsReadOnly => true;

    private const string SketchDxfFormat = "DXF";
    private const string FlatPatternDxfFormat = "FLAT PATTERN DXF";

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        var outputPath = (p["output_path"]?.Type == JTokenType.String) ? (string)p["output_path"]! : "";
        if (ExportPathPolicy.TryRejectPath(outputPath, out var pathRejection))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, pathRejection);

        var source = ((p["source"]?.Type == JTokenType.String) ? (string)p["source"]! : "").Trim().ToLowerInvariant();
        if (source != "sketch" && source != "flat_pattern")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "source must be 'sketch' or 'flat_pattern' (Phase 1 has no drawing tools)");

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        if (doc.DocumentType != DocumentTypeEnum.kPartDocumentObject)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                "export_dxf requires an active part document (sketch or sheet-metal flat-pattern source)");

        var partDef = ((PartDocument)doc).ComponentDefinition;

        try
        {
            if (source == "sketch")
            {
                var sketchName = (p["sketch_name"]?.Type == JTokenType.String) ? (string)p["sketch_name"]! : "";
                if (string.IsNullOrWhiteSpace(sketchName))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name is required when source=sketch");

                var sketch = EntityResolver.FindSketch(partDef, sketchName);
                if (sketch is null)
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"no sketch named '{sketchName}' on the active part");

                sketch.DataIO.WriteDataToFile(SketchDxfFormat, outputPath);

                return Ok(ctx, new JObject
                {
                    ["format"] = "DXF",
                    ["source"] = "sketch",
                    ["sketch_name"] = sketch.Name,
                    ["output_path"] = outputPath,
                    ["exported"] = true,
                });
            }
            else // flat_pattern
            {
                if (partDef is not SheetMetalComponentDefinition smDef)
                    return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                        "source=flat_pattern requires an active sheet-metal part document");

                if (!smDef.HasFlatPattern)
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        "the sheet-metal part has no flat pattern; create one before exporting DXF");

                smDef.FlatPattern.DataIO.WriteDataToFile(FlatPatternDxfFormat, outputPath);

                return Ok(ctx, new JObject
                {
                    ["format"] = "DXF",
                    ["source"] = "flat_pattern",
                    ["output_path"] = outputPath,
                    ["exported"] = true,
                });
            }
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to export DXF: " + ex.Message);
        }
    }
}
#endif
