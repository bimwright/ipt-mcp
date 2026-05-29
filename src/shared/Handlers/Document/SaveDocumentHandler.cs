#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>
/// <c>save_document</c> — saves the active document. With a <c>path</c> it performs a Save-As to that
/// location; without one it saves in place (failing with <c>INVALID_ARGUMENT</c> if the document was
/// never saved and therefore has no path).
/// </summary>
public sealed class SaveDocumentHandler : HandlerBase, IInventorCommand
{
    public string Name => "save_document";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        string path = (p["path"]?.Type == JTokenType.String) ? (string)p["path"]! : "";

        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                doc.SaveAs(path, false);
            }
            else
            {
                string? existing = null;
                try { existing = doc.FullFileName; } catch { /* never saved */ }
                if (string.IsNullOrEmpty(existing))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        "document has never been saved; provide a path to save it");
                doc.Save();
            }
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to save document: " + ex.Message);
        }

        string? saved = null;
        try { saved = doc.FullFileName; } catch { /* ignore */ }

        return Ok(ctx, new JObject
        {
            ["title"] = doc.DisplayName,
            ["path"] = string.IsNullOrEmpty(saved) ? null : saved,
            ["saved"] = true,
        });
    }
}
#endif
