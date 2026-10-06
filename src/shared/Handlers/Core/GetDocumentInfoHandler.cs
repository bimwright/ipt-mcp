#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// <c>get_document_info</c> — read-only. Returns the target document's (optional <c>document</c>
/// parameter, else the active one) title, full path, document type and dirty flag, or
/// <c>NO_DOCUMENT</c> when nothing is open. <c>references=true</c> adds the document's direct file
/// references (<c>Document.File.ReferencedFileDescriptors</c>) — the typed replacement for scripts
/// that needed <c>Document.File</c>, which the send_code policy blocks as <c>File.</c>. STA-bound: casts
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
        doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var targetFailure);
        if (targetFailure != null) return targetFailure;

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
        try { data["dirty"] = doc.Dirty; } catch { /* best effort */ }

        if (p["references"]?.Type == JTokenType.Boolean && (bool)p["references"]!)
        {
            var refs = new JArray();
            try
            {
                foreach (FileDescriptor fd in doc.File.ReferencedFileDescriptors)
                {
                    var item = new JObject();
                    try { item["path"] = fd.FullFileName; } catch { item["path"] = null; }
                    try { item["missing"] = fd.ReferenceMissing; } catch { }
                    refs.Add(item);
                }
            }
            catch (Exception ex)
            {
                data["references_error"] = ex.Message;
            }
            data["references"] = refs;
        }
        return InventorCommandResult.Success(Guid.Empty, data, meta);
    }
}
#endif
