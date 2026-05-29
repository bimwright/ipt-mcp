#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Document;

/// <summary>
/// <c>open_document</c> — opens an existing document from a full file path and makes it active.
/// </summary>
public sealed class OpenDocumentHandler : HandlerBase, IInventorCommand
{
    public string Name => "open_document";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        string path = (p["path"]?.Type == JTokenType.String) ? (string)p["path"]! : "";
        if (string.IsNullOrWhiteSpace(path))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "path is required");
        if (!System.IO.File.Exists(path))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "file does not exist: " + path);

        global::Inventor.Document doc;
        try
        {
            doc = app.Documents.Open(path, true);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to open document: " + ex.Message);
        }

        string? fullPath = null;
        try { fullPath = doc.FullFileName; } catch { /* ignore */ }

        return Ok(ctx, new JObject
        {
            ["title"] = doc.DisplayName,
            ["path"] = string.IsNullOrEmpty(fullPath) ? path : fullPath,
            ["document_type"] = doc.DocumentType.ToString(),
        });
    }
}
#endif
