#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>
/// <c>close_document</c> — closes the target document (optional <c>document</c>, else the active one)
/// under <see cref="SilentOperationScope"/>. <c>save=true</c> saves before closing;
/// <c>save=false</c> (default) discards unsaved changes (Close with SkipSave).
/// </summary>
public sealed class CloseDocumentHandler : HandlerBase, IInventorCommand
{
    public string Name => "close_document";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var targetFailure);
        if (targetFailure != null) return targetFailure;
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        bool save = p["save"]?.Type == JTokenType.Boolean && (bool)p["save"]!;
        string title = doc.DisplayName;
        string? noChanges = null;
        if (save)
        {
            // The save policy of save_document; a refused save leaves the document open with its changes.
            var allowUpgrade = p["allow_format_upgrade"]?.Type == JTokenType.Boolean && (bool)p["allow_format_upgrade"]!;
            var reason = DocumentSaveSupport.InPlace(app, doc, allowUpgrade, out var mode);
            if (reason == DocumentSavePolicy.NoChanges) noChanges = reason;
            else if (reason != null)
                return CheckpointSupport.Refuse(ctx, InventorErrorCodes.INVALID_ARGUMENT, reason,
                    DocumentSaveSupport.Message(reason, doc, ctx.InventorYear) + " The document was not closed.",
                    new JObject { ["mode"] = mode, ["closed"] = false });
        }

        var silent = SilentOperationScope.Enter(app, p);
        try
        {
            if (save && noChanges == null)
            {
#if INVENTOR2027
                if (doc is DrawingDocument drawing) drawing.Save2(false);
                else
#endif
                doc.Save();
                doc.Close(true);   // SkipSave: already saved
            }
            else
            {
                doc.Close(true);   // SkipSave: discard unsaved changes
            }
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to close document: " + ex.Message);
        }
        finally
        {
            silent.Dispose();
        }

        var result = new JObject { ["title"] = title, ["closed"] = true, ["saved"] = save && noChanges == null, ["silent"] = silent.Applied };
        if (noChanges != null) result["reason"] = noChanges;
        return Ok(ctx, result);
    }
}
#endif
