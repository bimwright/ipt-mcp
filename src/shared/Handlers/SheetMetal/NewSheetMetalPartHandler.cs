#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>new_sheet_metal_part</c> — create and activate a new sheet-metal part document. An explicit
/// <c>template</c> path always wins; otherwise the first <c>*Sheet Metal*.ipt</c> under
/// <c>FileLocations.TemplatesPath</c> (recursive — the filename is localized, e.g.
/// "Sheet Metal (mm).ipt" vs localized names) is used. Returns the new document's title and the
/// template path actually used so the caller can verify the locale picked a sane default.
/// </summary>
public sealed class NewSheetMetalPartHandler : HandlerBase, IInventorCommand
{
    public string Name => "new_sheet_metal_part";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        var template = (p["template"]?.Type == JTokenType.String) ? (string)p["template"]! : "";
        if (string.IsNullOrWhiteSpace(template))
        {
            try
            {
                template = Directory
                    .EnumerateFiles(app.FileLocations.TemplatesPath, "*Sheet Metal*.ipt", SearchOption.AllDirectories)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? "";
            }
            catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to search the template folder: " + ex.Message); }
            if (string.IsNullOrWhiteSpace(template))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    "no '*Sheet Metal*.ipt' template found under the templates path — pass an explicit template path");
        }
        else if (!System.IO.File.Exists(template))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"template '{template}' does not exist");

        try
        {
            var doc = app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
            return Ok(ctx, new JObject
            {
                ["title"] = doc.DisplayName,
                ["template"] = template,
                ["is_sheet_metal"] = doc is PartDocument pd && pd.ComponentDefinition is SheetMetalComponentDefinition,
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to create the sheet-metal part: " + ex.Message); }
    }
}
#endif
