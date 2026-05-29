#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// <c>export_step</c> — writes the active part or assembly to a STEP (.stp/.step) file via the built-in
/// STEP translator add-in. Does not mutate the active document. Read-only at the model level (it only
/// reads geometry and writes an external file).
/// </summary>
public sealed class ExportStepHandler : HandlerBase, IInventorCommand
{
    public string Name => "export_step";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        var outputPath = (p["output_path"]?.Type == JTokenType.String) ? (string)p["output_path"]! : "";
        if (ExportPathPolicy.TryRejectPath(outputPath, out var pathRejection))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, pathRejection);

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        if (doc.DocumentType != DocumentTypeEnum.kPartDocumentObject
            && doc.DocumentType != DocumentTypeEnum.kAssemblyDocumentObject)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                "export_step requires an active part or assembly document");

        try
        {
            var translator = ExportSupport.GetTranslator(app, ExportSupport.StepTranslatorId, "STEP");
            ExportSupport.SaveCopyAs(app, translator, doc, outputPath);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to export STEP: " + ex.Message);
        }

        return Ok(ctx, new JObject
        {
            ["format"] = "STEP",
            ["output_path"] = outputPath,
            ["exported"] = true,
        });
    }
}
#endif
