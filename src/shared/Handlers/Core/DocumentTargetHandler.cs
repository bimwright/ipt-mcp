#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>The active document's lifetime id (document_target_v1). Read on Inventor's thread.</summary>
public static class ActiveDocumentStamp
{
    public static (string Id, string? Title, string? Path) Read(object? application)
    {
        try
        {
            var doc = (application as global::Inventor.Application)?.ActiveDocument;
            if (doc is null) return (DocumentTargetPolicy.NoDocument, null, null);
            string? title = null, path = null;
            try { title = doc.DisplayName; } catch { }
            try { path = doc.FullFileName; } catch { }
            return (DocumentIdTable.Shared.IdFor(KeyOf(doc) ?? path ?? title ?? ""), title, path);
        }
        catch { return (DocumentTargetPolicy.NoDocument, null, null); }
    }

    /// <summary>The table key (InternalName + path, see <see cref="DocumentIdTable.Key"/>); reopening gets a new id via Forget.</summary>
    public static string? KeyOf(global::Inventor.Document doc)
    {
        string internalName;
        try { internalName = doc.InternalName; } catch { return null; }
        string? path = null;
        try { path = doc.FullFileName; } catch { }
        return DocumentIdTable.Key(internalName, path);
    }
}

/// <summary><c>document_target</c>: what inventor_switch_target(verify=true) pins. Read-only, internal (no MCP tool).</summary>
public sealed class DocumentTargetHandler : IInventorCommand
{
    public string Name => "document_target";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };
        var (id, title, path) = ActiveDocumentStamp.Read(ctx.Application);
        return InventorCommandResult.Success(Guid.Empty, new JObject
        {
            ["protocol"] = DocumentTargetPolicy.Protocol,
            ["document_id"] = id,
            ["document_title"] = title,
            ["document_path"] = path,
        }, meta);
    }
}
#endif
