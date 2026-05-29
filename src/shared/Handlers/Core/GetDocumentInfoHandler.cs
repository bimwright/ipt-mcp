#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Core;

/// <summary>
/// <c>get_document_info</c> — read-only. Returns the active document's title, full path, and document
/// type, or <c>NO_DOCUMENT</c> when nothing is open. STA-bound: casts
/// <see cref="InventorCommandContext.Application"/> to <c>Inventor.Application</c>.
/// </summary>
public sealed class GetDocumentInfoHandler : IInventorCommand
{
    public string Name => "get_document_info";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; }
        catch { doc = null; }

        if (doc is null)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document", meta);

        string? path = null;
        try { path = doc.FullFileName; } catch { /* unsaved document has no path */ }

        var data = new JObject
        {
            ["title"] = doc.DisplayName,
            ["path"] = string.IsNullOrEmpty(path) ? null : path,
            ["document_type"] = doc.DocumentType.ToString(),
        };
        return InventorCommandResult.Success(Guid.Empty, data, meta);
    }
}
#endif
