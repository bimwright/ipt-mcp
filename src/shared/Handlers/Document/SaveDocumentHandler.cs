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
/// <c>save_document</c> — saves the target document (optional <c>document</c>, else the active one)
/// under <see cref="SilentOperationScope"/> (<c>silent</c>, default true), enforcing the policy that
/// <c>get_document_state</c> reports (<see cref="DocumentSavePolicy"/>, rvt-mcp parity). Without a <c>path</c> it
/// saves in place: refused for an unsaved, template or read-only document or a file missing on disk; a no-op when
/// nothing changed; an older-format file needs <c>allow_format_upgrade</c>. With a <c>path</c> it performs a Save-As
/// to a new local file of the document's type; an existing file is never overwritten.
/// </summary>
public sealed class SaveDocumentHandler : HandlerBase, IInventorCommand
{
    public string Name => "save_document";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var targetFailure);
        if (targetFailure != null) return targetFailure;
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        string path = (p["path"]?.Type == JTokenType.String) ? (string)p["path"]! : "";
        var allowUpgrade = p["allow_format_upgrade"]?.Type == JTokenType.Boolean && (bool)p["allow_format_upgrade"]!;
        var mode = CheckpointSupport.ModeOf(app, doc, out var current);
        var isDrawing = doc is DrawingDocument;

        if (!string.IsNullOrWhiteSpace(path))
        {
            var type = "";
            try
            {
                type = doc.DocumentType switch
                {
                    DocumentTypeEnum.kPartDocumentObject => "Part",
                    DocumentTypeEnum.kAssemblyDocumentObject => "Assembly",
                    DocumentTypeEnum.kDrawingDocumentObject => "Drawing",
                    DocumentTypeEnum.kPresentationDocumentObject => "Presentation",
                    _ => "",
                };
            }
            catch { }
            var reason = DocumentSavePolicy.CheckSaveAsPath(path, DocumentSavePolicy.ExtensionsFor(type),
                System.IO.File.Exists, Directory.Exists, IsNetworkDrive);
            if (reason != null)
                return CheckpointSupport.Refuse(ctx, InventorErrorCodes.INVALID_ARGUMENT, reason, SaveAsMessage(reason, type),
                    new JObject { ["mode"] = mode });
            path = System.IO.Path.GetFullPath(path.Trim());
        }
        else
        {
            bool dirty = false, migrating = false;
            try { dirty = doc.Dirty; } catch { }
            try { migrating = doc.NeedsMigrating; } catch { }
            // A part or assembly save also saves its dirty references; a drawing save does not (Save2(false)).
            var modified = dirty || (!isDrawing && HasDirtyReference(doc));
            var reason = DocumentSavePolicy.CheckSave(mode, System.IO.File.Exists(current), modified, migrating, allowUpgrade);
            if (reason == DocumentSavePolicy.NoChanges)
                return Ok(ctx, new JObject
                {
                    ["title"] = doc.DisplayName, ["path"] = current, ["saved"] = false, ["reason"] = reason,
                    ["note"] = "Nothing to save: the document and its referenced documents have no unsaved changes.",
                });
            if (reason != null)
            {
                string? savedIn = null;
                try { savedIn = doc.SoftwareVersionSaved?.DisplayVersion; } catch { }
                return CheckpointSupport.Refuse(ctx, InventorErrorCodes.INVALID_ARGUMENT, reason, SaveMessage(reason, savedIn, ctx.InventorYear),
                    new JObject { ["mode"] = mode });
            }
        }

        var silent = SilentOperationScope.Enter(app, p);
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
#if INVENTOR2027
                // Drawing saves must not persist dirty referenced models as a side effect.
                if (doc is DrawingDocument drawing)
                {
                    var options = app.TransientObjects.CreateNameValueMap();
                    options.Add("SaveDependents", false);
                    drawing.SaveAs2(path, false, options);
                }
                else
#endif
                doc.SaveAs(path, false);
            }
            else
            {
#if INVENTOR2027
                if (doc is DrawingDocument drawing) drawing.Save2(false);
                else
#endif
                doc.Save();
            }
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to save document: " + ex.Message);
        }
        finally
        {
            silent.Dispose();
        }

        string? saved = null;
        try { saved = doc.FullFileName; } catch { /* ignore */ }

        return Ok(ctx, new JObject
        {
            ["title"] = doc.DisplayName,
            ["path"] = string.IsNullOrEmpty(saved) ? null : saved,
            ["saved"] = true,
            ["silent"] = silent.Applied,
        });
    }

    private static bool HasDirtyReference(global::Inventor.Document doc)
    {
        try
        {
            foreach (global::Inventor.Document r in doc.AllReferencedDocuments)
            {
                try { if (r.Dirty) return true; } catch { }
            }
        }
        catch { }
        return false;
    }

    private static bool IsNetworkDrive(string root)
    {
        try { return new DriveInfo(root).DriveType == DriveType.Network; } catch { return false; }
    }

    private static string SaveMessage(string reason, string? savedIn, int year) => reason switch
    {
        "DOCUMENT_NEVER_SAVED" => "the document has never been saved. Save it with path set to a new file the user named; never choose the path yourself.",
        "TEMPLATE_SAVE_AS_REQUIRED" => "the document is a template file and is not overwritten. Save with path set to a new file the user named.",
        "FILE_READ_ONLY" => "the file is read-only, or Inventor reports it cannot be modified (library or Vault checked-in file). Ask the user.",
        "FILE_MISSING_ON_DISK" => "the document's file is missing on disk. Ask the user; Save-As with path to a new file the user named.",
        "FORMAT_UPGRADE_REQUIRES_APPROVAL" => $"saving converts the file from Inventor {savedIn ?? "(older)"} to Inventor {year} format, which older Inventor versions cannot open. Ask the user, then pass allow_format_upgrade=true.",
        _ => "the save was refused. See inventor_get_document_state.",
    };

    private static string SaveAsMessage(string reason, string type) => reason switch
    {
        "SAVE_AS_PATH_NOT_LOCAL" => "path must be on a local drive; network paths are not written.",
        "SAVE_AS_PATH_NOT_ABSOLUTE" => "path must be a full local path that starts with a drive letter.",
        "SAVE_AS_WRONG_EXTENSION" => $"path must end with {string.Join(" or ", DocumentSavePolicy.ExtensionsFor(type))} for this {type.ToLowerInvariant()} document.",
        "SAVE_AS_TARGET_EXISTS" => "a file already exists at path and is never overwritten. To save this document in place, omit path; otherwise ask the user for a new file name.",
        "SAVE_AS_FOLDER_MISSING" => "the folder of path does not exist.",
        _ => "path was refused.",
    };
}
#endif
