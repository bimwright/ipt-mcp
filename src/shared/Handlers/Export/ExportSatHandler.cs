#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// <c>export_sat</c> — writes the active part or assembly to an ACIS SAT (.sat) file via the
/// built-in SAT translator add-in (spec F4-P0-5 — the Revit interop path). <c>acis_version</c>
/// maps to the translator's <c>Version</c> option; Inventor documents 7 (ACIS 7.0) as the only
/// valid SAT value, so other values are rejected rather than silently writing a version the
/// downstream consumer cannot read. Does not mutate the active document.
/// </summary>
public sealed class ExportSatHandler : HandlerBase, IInventorCommand
{
    public string Name => "export_sat";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        var outputPath = (p["output_path"]?.Type == JTokenType.String) ? (string)p["output_path"]! : "";
        if (ExportPathPolicy.TryRejectPath(outputPath, out var pathRejection))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, pathRejection);
        if (!outputPath.EndsWith(".sat", StringComparison.OrdinalIgnoreCase))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "output_path must end in .sat");

        var acisToken = p["acis_version"];
        if (acisToken is not null && acisToken.Type is not (JTokenType.Null or JTokenType.Undefined))
        {
            if (acisToken.Type is not (JTokenType.Integer or JTokenType.Float) || acisToken.Value<double>() != 7.0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    "acis_version must be 7 — the Inventor SAT translator's Version option supports ACIS 7.0 only.");
        }

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        if (doc.DocumentType != DocumentTypeEnum.kPartDocumentObject
            && doc.DocumentType != DocumentTypeEnum.kAssemblyDocumentObject)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                "export_sat requires an active part or assembly document");

        try
        {
            var translator = ExportSupport.GetTranslator(app, ExportSupport.SatTranslatorId, "SAT");
            ExportSupport.SaveCopyAs(app, translator, doc, outputPath,
                options => options.Value["Version"] = 7);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to export SAT: " + ex.Message);
        }

        return Ok(ctx, new JObject
        {
            ["format"] = "SAT",
            ["acis_version"] = 7,
            ["output_path"] = outputPath,
            ["exported"] = true,
        });
    }
}
#endif
