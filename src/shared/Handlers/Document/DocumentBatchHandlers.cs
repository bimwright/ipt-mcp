#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>
/// <c>save_all</c> (E6) — update a root document and save it together with every dirty document it
/// references, under <see cref="SilentOperationScope"/> (the unattended Save dialog that timed out
/// historical scripts). Reports each file as saved / clean / read_only / error.
/// </summary>
public sealed class SaveAllHandler : HandlerBase, IInventorCommand
{
    public string Name => "save_all";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        var root = ActiveDocumentSupport.ResolveTarget(ctx, new JObject { ["document"] = p["root"] }, out var targetFailure);
        if (targetFailure != null) return targetFailure;
        if (root is null) return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        string? rootPath = null;
        try { rootPath = root.FullFileName; } catch { }
        if (string.IsNullOrEmpty(rootPath))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "root document has never been saved; use inventor_save_document with a path first");

        var update = p["update"]?.Type != JTokenType.Boolean || (bool)p["update"]!;
        var dryRun = p["dry_run"]?.Type == JTokenType.Boolean && (bool)p["dry_run"]!;

        var docs = new List<global::Inventor.Document> { root };
        foreach (global::Inventor.Document d in root.AllReferencedDocuments) docs.Add(d);

        var rows = new JArray();
        var toSave = new List<(global::Inventor.Document doc, JObject row)>();
        foreach (var d in docs)
        {
            var row = new JObject { ["path"] = Path(d) };
            bool dirty;
            try { dirty = d.Dirty; } catch { dirty = true; }
            var readOnly = IsReadOnlyFile((string?)row["path"]);
            if (!dirty && !(update && ReferenceEquals(d, root))) row["status"] = "clean";
            else if (readOnly) row["status"] = "read_only";
            else { row["status"] = dryRun ? "would_save" : "pending"; toSave.Add((d, row)); }
            rows.Add(row);
        }

        if (!dryRun)
        {
            using var silent = SilentOperationScope.Enter(app, p);
            if (update)
            {
                try { root.Update2(false); } catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, "update failed: " + ex.Message); }
            }
            try
            {
                root.Save2(true);
            }
            catch (Exception ex)
            {
                foreach (var (_, row) in toSave) row["status"] = "error";
                var failed = new JObject { ["saved"] = 0, ["error"] = "save failed: " + ex.Message };
                ResponseSpillWriter.AttachResults(Name, failed, rows, ResponseSpillWriter.ForContext(ctx));
                return Ok(ctx, failed);
            }
            foreach (var (d, row) in toSave)
            {
                bool still;
                try { still = d.Dirty; } catch { still = false; }
                row["status"] = still ? "error" : "saved";
                if (still) row["error"] = "still dirty after save";
            }
        }

        var data = new JObject
        {
            ["root"] = rootPath,
            ["dry_run"] = dryRun,
            ["saved"] = rows.Count(r => (string?)r["status"] == "saved"),
            ["clean"] = rows.Count(r => (string?)r["status"] == "clean"),
            ["read_only"] = rows.Count(r => (string?)r["status"] == "read_only"),
            ["errors"] = rows.Count(r => (string?)r["status"] == "error"),
        };
        // Clean files are the bulk on big assemblies — list only what changed or needs attention.
        var interesting = new JArray(rows.Where(r => (string?)r["status"] != "clean"));
        ResponseSpillWriter.AttachResults(Name, data, interesting, ResponseSpillWriter.ForContext(ctx));
        return Ok(ctx, data);
    }

    internal static string? Path(global::Inventor.Document d)
    {
        try { var f = d.FullFileName; return string.IsNullOrEmpty(f) ? d.DisplayName : f; } catch { return null; }
    }

    private static bool IsReadOnlyFile(string? path)
    {
        try
        {
            return path != null && System.IO.File.Exists(path)
                && (System.IO.File.GetAttributes(path) & System.IO.FileAttributes.ReadOnly) != 0;
        }
        catch { return false; }
    }
}

/// <summary>
/// <c>open_documents</c> (E6) — open several documents in one call (default without windows, for
/// batch reads/edits through the <c>document</c> parameter of other tools), silently.
/// </summary>
public sealed class OpenDocumentsHandler : HandlerBase, IInventorCommand
{
    public string Name => "open_documents";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (p["paths"] is not JArray paths || paths.Count == 0 || paths.Count > 200 || paths.Any(t => t.Type != JTokenType.String))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "paths must be an array of 1..200 full file paths");
        var visible = p["visible"]?.Type == JTokenType.Boolean && (bool)p["visible"]!;

        var rows = new JArray();
        using (SilentOperationScope.Enter(app, p))
        {
            foreach (var t in paths)
            {
                var path = (string)t!;
                var row = new JObject { ["path"] = path };
                try
                {
                    if (!System.IO.File.Exists(path)) throw new InvalidOperationException("file does not exist");
                    var already = false;
                    var full = System.IO.Path.GetFullPath(path);
                    foreach (global::Inventor.Document d in app.Documents)
                    {
                        try { if (string.Equals(d.FullFileName, full, StringComparison.OrdinalIgnoreCase)) { already = true; break; } } catch { }
                    }
                    var doc = app.Documents.Open(path, visible);
                    row["status"] = already ? "already_open" : "opened";
                    row["title"] = doc.DisplayName;
                    row["document_type"] = doc.DocumentType.ToString();
                }
                catch (Exception ex)
                {
                    row["status"] = "error";
                    row["error"] = ex.Message;
                }
                rows.Add(row);
            }
        }
        var data = new JObject
        {
            ["opened"] = rows.Count(r => (string?)r["status"] != "error"),
            ["errors"] = rows.Count(r => (string?)r["status"] == "error"),
            ["visible"] = visible,
        };
        ResponseSpillWriter.AttachResults(Name, data, rows, ResponseSpillWriter.ForContext(ctx));
        return Ok(ctx, data);
    }
}

/// <summary>
/// <c>close_documents</c> (E6) — close documents by path/name (<c>documents</c>) or every visible
/// document (<c>all=true</c>, keeping the active one unless <c>keep_active=false</c>).
/// <c>save=true</c> saves each first; silent by default.
/// </summary>
public sealed class CloseDocumentsHandler : HandlerBase, IInventorCommand
{
    public string Name => "close_documents";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        var all = p["all"]?.Type == JTokenType.Boolean && (bool)p["all"]!;
        var keepActive = p["keep_active"]?.Type != JTokenType.Boolean || (bool)p["keep_active"]!;
        var save = p["save"]?.Type == JTokenType.Boolean && (bool)p["save"]!;

        var targets = new List<global::Inventor.Document>();
        if (all)
        {
            if (p["documents"] is JArray { Count: > 0 })
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "use either documents or all=true");
            global::Inventor.Document? active = null;
            try { active = app.ActiveDocument; } catch { }
            foreach (global::Inventor.Document d in app.Documents.VisibleDocuments)
                if (!(keepActive && ReferenceEquals(d, active))) targets.Add(d);
        }
        else
        {
            if (p["documents"] is not JArray list || list.Count == 0 || list.Any(t => t.Type != JTokenType.String))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "documents must be an array of paths/names, or pass all=true");
            foreach (var t in list)
            {
                var d = ActiveDocumentSupport.ResolveTarget(ctx, new JObject { ["document"] = t }, out var failure);
                if (failure != null) return failure;
                if (d != null && !targets.Contains(d)) targets.Add(d);
            }
        }

        var rows = new JArray();
        using (SilentOperationScope.Enter(app, p))
        {
            foreach (var d in targets)
            {
                var row = new JObject { ["path"] = SaveAllHandler.Path(d) };
                try
                {
                    if (save)
                    {
                        string? f = null;
                        try { f = d.FullFileName; } catch { }
                        if (string.IsNullOrEmpty(f)) throw new InvalidOperationException("never saved (no path); save it with a path first");
                        d.Save();
                    }
                    d.Close(true);
                    row["status"] = save ? "saved_closed" : "closed";
                }
                catch (Exception ex)
                {
                    row["status"] = "error";
                    row["error"] = ex.Message;
                }
                rows.Add(row);
            }
        }
        var data = new JObject
        {
            ["closed"] = rows.Count(r => (string?)r["status"] != "error"),
            ["errors"] = rows.Count(r => (string?)r["status"] == "error"),
        };
        ResponseSpillWriter.AttachResults(Name, data, rows, ResponseSpillWriter.ForContext(ctx));
        return Ok(ctx, data);
    }
}
#endif
