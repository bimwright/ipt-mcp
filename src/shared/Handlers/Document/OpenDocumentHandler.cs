#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>
/// <c>open_document</c> — opens an existing document from a full file path and makes it active
/// (<c>visible=false</c> loads it without a window). Runs under <see cref="SilentOperationScope"/>.
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

        var visible = !(p["visible"]?.Type == JTokenType.Boolean && !(bool)p["visible"]!);
        global::Inventor.Document doc;
        var silent = SilentOperationScope.Enter(app, p);
        try
        {
            doc = app.Documents.Open(path, visible);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to open document: " + ex.Message);
        }
        finally
        {
            silent.Dispose();
        }

        string? fullPath = null;
        try { fullPath = doc.FullFileName; } catch { /* ignore */ }

        return Ok(ctx, new JObject
        {
            ["title"] = doc.DisplayName,
            ["path"] = string.IsNullOrEmpty(fullPath) ? path : fullPath,
            ["document_type"] = doc.DocumentType.ToString(),
            ["visible"] = visible,
            ["silent"] = silent.Applied,
        });
    }
}
#endif
