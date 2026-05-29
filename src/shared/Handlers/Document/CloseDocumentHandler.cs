#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>
/// <c>close_document</c> — closes the active document. <c>save=true</c> saves before closing;
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
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        bool save = p["save"]?.Type == JTokenType.Boolean && (bool)p["save"]!;
        string title = doc.DisplayName;

        try
        {
            if (save)
            {
                string? existing = null;
                try { existing = doc.FullFileName; } catch { /* never saved */ }
                if (string.IsNullOrEmpty(existing))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        "cannot save on close: document has never been saved (no path)");
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

        return Ok(ctx, new JObject { ["title"] = title, ["closed"] = true, ["saved"] = save });
    }
}
#endif
