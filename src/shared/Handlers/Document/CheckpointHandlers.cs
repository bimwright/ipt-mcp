#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Document;

/// <summary>Shared checks for the checkpoint commands (rvt-mcp parity). Acts on the active document only.</summary>
internal static class CheckpointSupport
{
    internal static InventorCommandResult Refuse(InventorCommandContext ctx, string code, string reason, string message, JObject? data = null)
    {
        data ??= new JObject();
        data["reason"] = reason;
        var result = InventorCommandResult.Fail(Guid.Empty, code, reason + ": " + message,
            new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear });
        result.Data = data;
        return result;
    }

    /// <summary>The active document when it is a saved local file a checkpoint can copy; otherwise a refusal.</summary>
    internal static global::Inventor.Document? LocalFile(InventorCommandContext ctx, Application app, out string path, out InventorCommandResult? refusal)
    {
        path = "";
        refusal = null;
        global::Inventor.Document? doc = null;
        try { doc = app.ActiveDocument; } catch { }
        if (doc == null)
        {
            refusal = Refuse(ctx, InventorErrorCodes.NO_DOCUMENT, "NO_ACTIVE_DOCUMENT", "no active Inventor document.");
            return null;
        }
        try { path = doc.FullFileName ?? ""; } catch { }
        bool? readOnly = null, modifiable = null;
        try { if (System.IO.File.Exists(path)) readOnly = new FileInfo(path).IsReadOnly; } catch { }
        try { modifiable = doc.IsModifiable; } catch { }
        var roots = new[] { Safe(() => app.FileOptions.TemplatesPath), Safe(() => app.DesignProjectManager.ActiveDesignProject.TemplatesPath) };
        var mode = DocumentStatePolicy.Mode(path, readOnly, modifiable, roots);
        if (mode != DocumentStatePolicy.LocalFile)
        {
            refusal = Refuse(ctx, InventorErrorCodes.INVALID_ARGUMENT, mode == DocumentStatePolicy.Unsaved ? "DOCUMENT_NEVER_SAVED" : "CHECKPOINT_UNSUPPORTED_MODE",
                $"checkpoints need a saved local file; the active document is {mode}. See inventor_get_document_state.", new JObject { ["mode"] = mode });
            return null;
        }
        if (!CheckpointPaths.Supported(path))
        {
            refusal = Refuse(ctx, InventorErrorCodes.INVALID_ARGUMENT, "CHECKPOINT_UNSUPPORTED_FILE", "only .ipt, .iam, .idw, .dwg and .ipn files have checkpoints.");
            return null;
        }
        if (!System.IO.File.Exists(path))
        {
            refusal = Refuse(ctx, InventorErrorCodes.INVALID_ARGUMENT, "FILE_MISSING_ON_DISK", "the active document's file is missing on disk.");
            return null;
        }
        return doc;
    }

    internal static string? Safe(Func<string?> read) { try { return read(); } catch { return null; } }

    internal static int ReferencesNotCaptured(global::Inventor.Document doc)
    {
        try { return doc.DocumentType == DocumentTypeEnum.kPartDocumentObject ? 0 : doc.AllReferencedDocuments.Count; }
        catch { return 0; }
    }
}

/// <summary>
/// <c>save_checkpoint</c>: saves the active local document if it has unsaved changes, then copies the file
/// into "&lt;name&gt;.checkpoints\" and verifies the copy (size + SHA-256) before indexing it.
/// </summary>
public sealed class SaveCheckpointHandler : HandlerBase, IInventorCommand
{
    public string Name => "save_checkpoint";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        var label = (string?)p["label"];
        var reason = (string?)p["reason"];
        if (string.IsNullOrWhiteSpace(label) || label!.Length > 80) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "label must contain 1-80 characters.");
        if (reason != null && reason.Length > 500) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "reason must not exceed 500 characters.");
        var allowUpgrade = p["allow_format_upgrade"]?.Type == JTokenType.Boolean && (bool)p["allow_format_upgrade"]!;

        var doc = CheckpointSupport.LocalFile(ctx, app, out var path, out var refusal);
        if (doc == null) return refusal!;
        bool dirty = false, migrating = false;
        try { dirty = doc.Dirty; } catch { }
        try { migrating = doc.NeedsMigrating; } catch { }
        if (dirty && migrating && !allowUpgrade)
        {
            string? savedIn = null;
            try { savedIn = doc.SoftwareVersionSaved?.DisplayVersion; } catch { }
            return CheckpointSupport.Refuse(ctx, InventorErrorCodes.INVALID_ARGUMENT, "FORMAT_UPGRADE_REQUIRES_APPROVAL",
                $"the document has unsaved changes and saving converts it from Inventor {savedIn ?? "(older)"} to Inventor {ctx.InventorYear} format. "
                + "Ask the user, then pass allow_format_upgrade=true.");
        }

        var folder = CheckpointPaths.Directory(path);
        var indexPath = CheckpointPaths.Index(path);
        var fileName = System.IO.Path.GetFileName(path);
        CheckpointIndexData index;
        try
        {
            index = CheckpointIndex.Load(indexPath, fileName);
            if (Directory.Exists(folder) && (System.IO.File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                return CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, "CHECKPOINT_INDEX_UNREADABLE", "the checkpoints folder is a reparse point.");
        }
        catch (TimeoutException) { return CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, "CHECKPOINT_INDEX_BUSY", "another process holds the checkpoint index."); }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
        { return CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, "CHECKPOINT_INDEX_UNREADABLE", "the checkpoint index cannot be read."); }

        if (Space(path) is { } spaceRefusal) return CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, spaceRefusal, "not enough free disk space for the copy.");

        var savedFirst = false;
        if (dirty)
        {
            var internalName = SafeInternalName(doc);
            using (SilentOperationScope.Enter(app, on: true))
            {
                try
                {
#if INVENTOR2027
                    if (doc is DrawingDocument drawing) drawing.Save2(false);   // never save dirty referenced models as a side effect
                    else
#endif
                    doc.Save();
                    savedFirst = true;
                }
                catch (Exception ex)
                {
                    return CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, "SAVE_FAILED", "saving before the checkpoint failed: " + ex.Message,
                        new JObject { ["saved_first"] = false });
                }
            }
            global::Inventor.Document? now = null;
            try { now = app.ActiveDocument; } catch { }
            if (now == null || SafeInternalName(now) != internalName)
                return Failure(ctx, "DOCUMENT_CHANGED", "the active document changed while saving; no checkpoint was taken.", savedFirst, null, null);
        }

        if (Space(path) is { } afterSave) return Failure(ctx, afterSave, "not enough free disk space for the copy.", savedFirst, null, null);
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        { return Failure(ctx, "CHECKPOINT_COPY_FAILED", "the checkpoints folder cannot be created.", savedFirst, null, null); }

        CheckpointName? name = null;
        string? target = null;
        var createdLocal = DateTimeOffset.Now;
        try
        {
            for (var attempt = 0; attempt < 10000 && target == null; attempt++)
            {
                name = CheckpointNaming.Next(folder, System.IO.Path.GetExtension(path), createdLocal, index.Checkpoints);
                var candidate = System.IO.Path.Combine(folder, name.File);
                try { CheckpointFiles.CopyNew(path, candidate); target = candidate; }
                catch (IOException ex) when (CheckpointFiles.IsExists(ex)) { }
            }
            if (target == null) return Failure(ctx, "CHECKPOINT_COPY_FAILED", "no free checkpoint name.", savedFirst, null, null);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        { return Failure(ctx, "CHECKPOINT_COPY_FAILED", "copying the file failed: " + ex.Message, savedFirst, null, target); }

        string sourceHash;
        long copiedBytes;
        try
        {
            sourceHash = CheckpointFiles.Sha256(path);
            copiedBytes = new FileInfo(target).Length;
            if (new FileInfo(path).Length != copiedBytes || CheckpointFiles.Sha256(target) != sourceHash)
                return Failure(ctx, "CHECKPOINT_VERIFY_FAILED", "the copy does not match the file.", savedFirst, Cleanup(target), target);
        }
        catch { return Failure(ctx, "CHECKPOINT_VERIFY_FAILED", "the copy could not be verified.", savedFirst, Cleanup(target), target); }

        int counter = 0;
        try { counter = doc.FileSaveCounter; } catch { }
        string user = System.Environment.UserName;
        try { var u = app.GeneralOptions.UserName; if (!string.IsNullOrWhiteSpace(u)) user = u; } catch { }
        var entry = new CheckpointEntry
        {
            Id = name!.Id, File = name.File, Label = label, Reason = reason,
            Milestone = p["milestone"]?.Type == JTokenType.Boolean && (bool)p["milestone"]!,
            CreatedUtc = createdLocal.ToUniversalTime().ToString("o"), Bytes = copiedBytes,
            Source = new CheckpointSource { FileSaveCounter = Math.Max(0, counter), Sha256 = sourceHash },
            InventorYear = ctx.InventorYear > 0 ? ctx.InventorYear : 1, User = user ?? "",
            ReferencesNotCaptured = CheckpointSupport.ReferencesNotCaptured(doc),
        };
        CheckpointIndexData updated;
        try { updated = CheckpointIndex.Update(indexPath, fileName, data => data.Checkpoints.Add(entry)); }
        catch (TimeoutException) { return Failure(ctx, "CHECKPOINT_INDEX_BUSY", "another process holds the checkpoint index.", savedFirst, Cleanup(target), target); }
        catch { return Failure(ctx, "CHECKPOINT_INDEX_WRITE_FAILED", "the checkpoint index cannot be written.", savedFirst, Cleanup(target), target); }

        var warnings = new JArray();
        if (entry.ReferencesNotCaptured > 0) warnings.Add("REFERENCES_NOT_CAPTURED");
        var nonMilestones = updated.Checkpoints.Count(c => !c.Milestone);
        return Ok(ctx, new JObject
        {
            ["checkpoint"] = JObject.FromObject(entry),
            ["path"] = target,
            ["saved_first"] = savedFirst,
            ["totals"] = new JObject { ["count"] = updated.Checkpoints.Count, ["total_bytes"] = updated.Checkpoints.Sum(c => c.Bytes) },
            ["prune_hint"] = nonMilestones > 5 ? "More than 5 non-milestone checkpoints; preview pruning (inventor_manage_checkpoints action=prune, dry_run=true) if the user approves." : null,
            ["note"] = entry.ReferencesNotCaptured > 0
                ? $"Only this file was copied; its {entry.ReferencesNotCaptured} referenced documents were not. A restored copy opens with their current versions."
                : null,
            ["warnings"] = warnings,
        });
    }

    private static string? Space(string path)
    {
        try { return CheckpointSpace.HasEnough(new FileInfo(path).Length, CheckpointSpace.AvailableBytes(path)) ? null : "INSUFFICIENT_DISK_SPACE"; }
        catch { return "FREE_SPACE_UNAVAILABLE"; }
    }

    private static string? SafeInternalName(global::Inventor.Document doc) { try { return doc.InternalName; } catch { return null; } }

    private static bool Cleanup(string path)
    {
        try { System.IO.File.Delete(path); return !System.IO.File.Exists(path); }
        catch { return false; }
    }

    private static InventorCommandResult Failure(InventorCommandContext ctx, string reason, string message, bool savedFirst, bool? copyRemoved, string? copyPath)
        => CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, reason,
            message + (savedFirst ? " The document WAS saved first." : ""),
            new JObject { ["saved_first"] = savedFirst, ["checkpoint_copy_removed"] = copyRemoved, ["checkpoint_path"] = copyPath });
}

/// <summary><c>manage_checkpoints</c>: list, prune with a user-approved plan hash, or restore a checkpoint as a new file.</summary>
public sealed class ManageCheckpointsHandler : HandlerBase, IInventorCommand
{
    public string Name => "manage_checkpoints";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        var action = (string?)p["action"];
        if (action != "list" && action != "prune" && action != "restore")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "action must be list, prune or restore.");
        var doc = CheckpointSupport.LocalFile(ctx, app, out var path, out var refusal);
        if (doc == null) return refusal!;
        var store = new CheckpointStore(path);
        try
        {
            if (action == "list")
            {
                var list = store.List();
                var entries = new JArray(list.Items.Select(item =>
                {
                    var entry = JObject.FromObject(item.Entry);
                    entry["exists"] = item.Exists;
                    entry["bytes"] = item.Bytes;
                    return entry;
                }));
                return Ok(ctx, new JObject
                {
                    ["file"] = path, ["folder"] = CheckpointPaths.Directory(path),
                    ["checkpoints"] = entries, ["untracked_files"] = new JArray(list.UntrackedFiles), ["index_status"] = list.IndexStatus,
                });
            }
            if (action == "restore")
            {
                var restored = store.Restore((string?)p["checkpoint_id"]);
                if (restored.Error != null)
                    return CheckpointSupport.Refuse(ctx, restored.Error is "CHECKPOINT_ID_REQUIRED" or "CHECKPOINT_NOT_FOUND" or "RESTORE_TARGET_EXISTS"
                        ? InventorErrorCodes.INVALID_ARGUMENT : InventorErrorCodes.API_ERROR, restored.Error, "restore did not create a file.");
                return Ok(ctx, new JObject
                {
                    ["path"] = restored.Path,
                    ["next_steps"] = new JArray(
                        "Open it with inventor_open_document only if the user wants to inspect it; the working file was not changed.",
                        "It is a copy of the same document: close the working file before opening it, and do not place both in one assembly.",
                        "An assembly or drawing copy opens with the current versions of its referenced files.",
                        "To replace the working file, the user closes it in Inventor and renames the files."),
                });
            }

            var keepLast = p["keep_last"]?.Type == JTokenType.Integer ? (int)p["keep_last"]! : 5;
            if (keepLast < 1 || keepLast > 100) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "keep_last must be between 1 and 100.");
            var dryRun = !(p["dry_run"]?.Type == JTokenType.Boolean && !(bool)p["dry_run"]!);
            if (dryRun)
            {
                var plan = store.Plan(keepLast);
                return Ok(ctx, new JObject
                {
                    ["keep_last"] = keepLast,
                    ["delete"] = new JArray(plan.DeleteFiles.Select(c => new JObject { ["id"] = c.Id, ["file"] = c.File, ["bytes"] = c.Bytes, ["last_write_utc"] = c.LastWriteUtc?.ToString("o") })),
                    ["missing"] = new JArray(plan.MissingEntries.Select(c => new JObject { ["id"] = c.Id, ["file"] = c.File })),
                    ["bytes_to_free"] = plan.BytesToFree,
                    ["plan_hash"] = plan.PlanHash,
                    ["next_step"] = "Show this plan to the user; apply only after approval with dry_run=false and this plan_hash.",
                });
            }
            var result = store.Prune(keepLast, (string?)p["plan_hash"]);
            var data = new JObject { ["deleted"] = new JArray(result.DeletedIds), ["missing_removed"] = new JArray(result.MissingIds), ["failed"] = new JArray(result.FailedIds) };
            if (result.Error == null) return Ok(ctx, data);
            return CheckpointSupport.Refuse(ctx, result.Error is "PLAN_HASH_REQUIRED" or "PLAN_CHANGED" ? InventorErrorCodes.INVALID_ARGUMENT : InventorErrorCodes.API_ERROR,
                result.Error, result.Error == "PLAN_CHANGED" ? "the checkpoints changed since the plan; preview again." : "prune did not complete.", data);
        }
        catch (TimeoutException) { return CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, "CHECKPOINT_INDEX_BUSY", "another process holds the checkpoint index."); }
        catch (InvalidDataException) { return CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, "CHECKPOINT_INDEX_UNREADABLE", "the checkpoint index or folder is not valid."); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        { return CheckpointSupport.Refuse(ctx, InventorErrorCodes.API_ERROR, "CHECKPOINT_IO_FAILED", ex.Message); }
    }
}
#endif
