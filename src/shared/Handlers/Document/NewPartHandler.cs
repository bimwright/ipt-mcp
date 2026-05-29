#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>
/// <c>new_part</c> — creates and activates a new part document (.ipt). An optional <c>template</c>
/// path may be supplied; otherwise the standard part template is resolved via the file manager.
/// </summary>
public sealed class NewPartHandler : HandlerBase, IInventorCommand
{
    public string Name => "new_part";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        string template = (p["template"]?.Type == JTokenType.String) ? (string)p["template"]! : "";
        if (string.IsNullOrWhiteSpace(template))
            template = app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);

        global::Inventor.Document doc;
        try
        {
            doc = app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to create part: " + ex.Message);
        }

        string? path = null;
        try { path = doc.FullFileName; } catch { /* unsaved */ }

        return Ok(ctx, new JObject
        {
            ["title"] = doc.DisplayName,
            ["path"] = string.IsNullOrEmpty(path) ? null : path,
            ["document_type"] = doc.DocumentType.ToString(),
        });
    }
}
#endif
