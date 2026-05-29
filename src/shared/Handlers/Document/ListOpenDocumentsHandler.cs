#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>
/// <c>list_open_documents</c> — read-only. Enumerates every open Inventor document with its title,
/// full path, document type, and whether it is the active document.
/// </summary>
public sealed class ListOpenDocumentsHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_open_documents";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        string? activePath = null;
        try { activePath = app.ActiveDocument?.FullFileName; } catch { /* none or unsaved */ }
        string? activeName = null;
        try { activeName = app.ActiveDocument?.DisplayName; } catch { /* none */ }

        var docs = new JArray();
        foreach (global::Inventor.Document doc in app.Documents)
        {
            string? path = null;
            try { path = doc.FullFileName; } catch { /* unsaved */ }

            bool isActive = (!string.IsNullOrEmpty(path) && path == activePath)
                            || (string.IsNullOrEmpty(path) && doc.DisplayName == activeName && activePath == null);

            docs.Add(new JObject
            {
                ["title"] = doc.DisplayName,
                ["path"] = string.IsNullOrEmpty(path) ? null : path,
                ["document_type"] = doc.DocumentType.ToString(),
                ["is_active"] = isActive,
            });
        }

        return Ok(ctx, new JObject { ["count"] = docs.Count, ["documents"] = docs });
    }
}
#endif
