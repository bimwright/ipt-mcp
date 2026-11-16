#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.IO;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>
/// <c>get_document_state</c> — read-only (rvt-mcp parity). For the active document, one named
/// <c>document</c>, or every open document (<c>scope=all</c>): mode, unsaved changes, file state,
/// format migration and dirty references, plus the save policy for the active (or named) document.
/// Works with no document open. Never opens, activates or changes a document.
/// </summary>
public sealed class GetDocumentStateHandler : HandlerBase, IInventorCommand
{
    private const int MaxDirtyReferences = 20;

    public string Name => "get_document_state";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        var scope = (string?)p["scope"] ?? "active";
        if (scope != "active" && scope != "all")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "scope must be active or all.");
        var named = p["document"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)p["document"]);
        if (named && scope == "all")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "document cannot be combined with scope=all.");

        global::Inventor.Document? active = null;
        try { active = app.ActiveDocument; } catch { }
        var templateRoots = TemplateRoots(app);
        var documents = new JArray();
        JObject? policy = null;

        if (scope == "all")
        {
            foreach (global::Inventor.Document doc in app.Documents)
                documents.Add(Describe(ctx, doc, active, templateRoots, out _));
            if (active != null) Describe(ctx, active, active, templateRoots, out policy);
        }
        else
        {
            var doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var failure);
            if (failure != null) return failure;
            if (doc != null) documents.Add(Describe(ctx, doc, active, templateRoots, out policy));
        }

        return Ok(ctx, new JObject
        {
            ["documents"] = documents,
            ["policy"] = policy,
            ["project"] = Project(app),
        });
    }

    private static JObject Describe(InventorCommandContext ctx, global::Inventor.Document doc, global::Inventor.Document? active,
        string?[] templateRoots, out JObject? policy)
    {
        policy = null;
        var warnings = new JArray();
        string? title = null, path = null;
        try { title = doc.DisplayName; } catch { }
        try { path = doc.FullFileName; } catch { }
        if (string.IsNullOrEmpty(path)) path = null;

        bool? exists = null, readOnlyAttribute = null;
        long? bytes = null;
        string? lastWriteUtc = null;
        if (path != null)
        {
            try
            {
                exists = System.IO.File.Exists(path);
                if (exists == true)
                {
                    var info = new FileInfo(path);
                    readOnlyAttribute = info.IsReadOnly;
                    bytes = info.Length;
                    lastWriteUtc = info.LastWriteTimeUtc.ToString("o");
                }
                else warnings.Add("FILE_MISSING_ON_DISK");
            }
            catch { warnings.Add("FILE_ATTRIBUTES_UNAVAILABLE"); }
        }

        bool? modifiable = Try(() => doc.IsModifiable, warnings, "MODIFIABLE_UNAVAILABLE");
        bool? dirty = Try(() => doc.Dirty, warnings, "DIRTY_UNAVAILABLE");
        bool? needsMigrating = Try(() => doc.NeedsMigrating, warnings, "MIGRATION_STATE_UNAVAILABLE");
        bool? requiresUpdate = Try(() => doc.RequiresUpdate, warnings, null);
        string? savedIn = null;
        try { savedIn = doc.SoftwareVersionSaved?.DisplayVersion; } catch { warnings.Add("SAVED_VERSION_UNAVAILABLE"); }
        int? saveCounter = null;
        try { saveCounter = doc.FileSaveCounter; } catch { }

        var isDrawing = false;
        string? type = null;
        try { var t = doc.DocumentType; type = t.ToString(); isDrawing = t == DocumentTypeEnum.kDrawingDocumentObject; } catch { }

        var mode = DocumentStatePolicy.Mode(path, readOnlyAttribute, modifiable, templateRoots);
        var isActive = active != null && ReferenceEquals(doc, active) || active != null && SameDocument(doc, active);
        policy = DocumentStatePolicy.Policy(mode, isDrawing, needsMigrating == true, savedIn, ctx.InventorYear);

        var key = ActiveDocumentStamp.KeyOf(doc) ?? path ?? title ?? "";
        return new JObject
        {
            ["title"] = title,
            ["path"] = path,
            ["document_id"] = DocumentIdTable.Shared.IdFor(key),
            ["is_active"] = isActive,
            ["document_type"] = type,
            ["mode"] = mode,
            ["is_modified"] = dirty,
            ["dirty_references"] = DirtyReferences(doc),
            ["modifiable"] = modifiable,
            ["is_read_only"] = readOnlyAttribute,
            ["exists_on_disk"] = exists,
            ["file_bytes"] = bytes,
            ["last_write_utc"] = lastWriteUtc,
            ["saved_in_version"] = savedIn,
            ["host_year"] = ctx.InventorYear,
            ["needs_migrating"] = needsMigrating,
            ["requires_update"] = requiresUpdate,
            ["file_save_counter"] = saveCounter,
            ["warnings"] = warnings,
        };
    }

    private static bool SameDocument(global::Inventor.Document a, global::Inventor.Document b)
    {
        try { return a.InternalName == b.InternalName; } catch { return false; }
    }

    private static JObject DirtyReferences(global::Inventor.Document doc)
    {
        var items = new JArray();
        var count = 0;
        try
        {
            foreach (global::Inventor.Document r in doc.AllReferencedDocuments)
            {
                bool dirty;
                try { dirty = r.Dirty; } catch { continue; }
                if (!dirty) continue;
                count++;
                if (items.Count >= MaxDirtyReferences) continue;
                string? path = null, title = null;
                try { path = r.FullFileName; } catch { }
                try { title = r.DisplayName; } catch { }
                items.Add(new JObject { ["title"] = title, ["path"] = string.IsNullOrEmpty(path) ? null : path });
            }
        }
        catch
        {
            return new JObject { ["count"] = null, ["items"] = items, ["available"] = false };
        }
        return new JObject { ["count"] = count, ["items"] = items, ["truncated"] = count > items.Count };
    }

    private static string?[] TemplateRoots(Application app)
    {
        var roots = new List<string?>();
        try { roots.Add(app.FileOptions.TemplatesPath); } catch { }
        try { roots.Add(app.DesignProjectManager.ActiveDesignProject.TemplatesPath); } catch { }
        return roots.ToArray();
    }

    private static JObject? Project(Application app)
    {
        try
        {
            var project = app.DesignProjectManager.ActiveDesignProject;
            int? keep = null;
            try { keep = project.OldVersionsToKeep; } catch { }
            return new JObject { ["name"] = project.Name, ["path"] = project.FullFileName, ["old_versions_to_keep"] = keep };
        }
        catch { return null; }
    }

    private static bool? Try(Func<bool> read, JArray warnings, string? warning)
    {
        try { return read(); }
        catch { if (warning != null) warnings.Add(warning); return null; }
    }
}
#endif
