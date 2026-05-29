#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Document;

/// <summary>
/// <c>new_assembly</c> — creates and activates a new assembly document (.iam). An optional
/// <c>template</c> path may be supplied; otherwise the standard assembly template is resolved.
/// </summary>
public sealed class NewAssemblyHandler : HandlerBase, IInventorCommand
{
    public string Name => "new_assembly";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        string template = (p["template"]?.Type == JTokenType.String) ? (string)p["template"]! : "";
        if (string.IsNullOrWhiteSpace(template))
            template = app.FileManager.GetTemplateFile(DocumentTypeEnum.kAssemblyDocumentObject);

        global::Inventor.Document doc;
        try
        {
            doc = app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject, template, true);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to create assembly: " + ex.Message);
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
