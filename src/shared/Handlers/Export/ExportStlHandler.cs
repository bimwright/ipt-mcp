#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Export;

/// <summary>
/// <c>export_stl</c> — writes the active part or assembly to an STL (.stl) file via the built-in STL
/// translator add-in. Does not mutate the active document.
/// </summary>
public sealed class ExportStlHandler : HandlerBase, IInventorCommand
{
    public string Name => "export_stl";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        var outputPath = (p["output_path"]?.Type == JTokenType.String) ? (string)p["output_path"]! : "";
        if (string.IsNullOrWhiteSpace(outputPath))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "output_path is required");

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        if (doc.DocumentType != DocumentTypeEnum.kPartDocumentObject
            && doc.DocumentType != DocumentTypeEnum.kAssemblyDocumentObject)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                "export_stl requires an active part or assembly document");

        try
        {
            var translator = ExportSupport.GetTranslator(app, ExportSupport.StlTranslatorId, "STL");
            ExportSupport.SaveCopyAs(app, translator, doc, outputPath);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to export STL: " + ex.Message);
        }

        return Ok(ctx, new JObject
        {
            ["format"] = "STL",
            ["output_path"] = outputPath,
            ["exported"] = true,
        });
    }
}
#endif
