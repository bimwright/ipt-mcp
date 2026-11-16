using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// <c>get_document_state</c> (rvt-mcp parity, <c>revit_get_document_state</c>): the document mode and the
/// save policy an agent follows before saving. Host-free so the rules are unit-tested; the handler only
/// reads Inventor and the file system.
/// </summary>
public static class DocumentStatePolicy
{
    public const string Unsaved = "unsaved";
    public const string LocalFile = "local_file";
    public const string Template = "template";
    public const string ReadOnly = "read_only";

    /// <param name="path">Full file name; empty when never saved.</param>
    /// <param name="readOnlyAttribute">The file's read-only attribute; null when unknown or missing.</param>
    /// <param name="modifiable">Inventor's Document.IsModifiable; null when unknown.</param>
    /// <param name="templateRoots">Template folders (application and active project).</param>
    public static string Mode(string? path, bool? readOnlyAttribute, bool? modifiable, IEnumerable<string?> templateRoots)
    {
        if (string.IsNullOrWhiteSpace(path)) return Unsaved;
        if (IsUnder(path!, templateRoots)) return Template;
        if (readOnlyAttribute == true || modifiable == false) return ReadOnly;
        return LocalFile;
    }

    public static bool IsUnder(string path, IEnumerable<string?> roots)
    {
        string full;
        try { full = Path.GetFullPath(path); } catch { return false; }
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string dir;
            try { dir = Path.GetFullPath(root!).TrimEnd('\\', '/') + Path.DirectorySeparatorChar; } catch { continue; }
            if (full.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Rules and allowed tools for one document mode, plus a migration rule when saving upgrades the file.</summary>
    public static JObject Policy(string mode, bool isDrawing, bool needsMigrating, string? savedInVersion, int hostYear)
    {
        var rules = new List<string>();
        string[] allowed;
        switch (mode)
        {
            case LocalFile:
                rules.Add("inventor_save_document overwrites this file. Inventor moves the previous version into the OldVersions folder next to it, keeping as many as the active project allows (project.old_versions_to_keep).");
                rules.Add(isDrawing
                    ? "Saving a drawing does not save its dirty referenced models (dirty_references); save them separately only if the user wants."
                    : "Saving a part or assembly also saves its dirty referenced documents (dirty_references).");
                rules.Add("Save only after verified work, and report unsaved changes (is_modified, dirty_references) when work ends.");
                allowed = new[] { "inventor_get_document_state", "inventor_save_document", "inventor_save_all", "inventor_close_document" };
                break;
            case Unsaved:
                rules.Add("This document has never been saved. Save it only with inventor_save_document(path) to a new path the user gave; never choose the path yourself.");
                allowed = new[] { "inventor_get_document_state", "inventor_save_document" };
                break;
            case Template:
                rules.Add("This is a template file; do not overwrite it.");
                rules.Add("To keep work based on it, save with inventor_save_document(path) to a new path the user gave; never choose the path yourself.");
                allowed = new[] { "inventor_get_document_state", "inventor_save_document" };
                break;
            case ReadOnly:
                rules.Add("The file is read-only, or Inventor reports it cannot be modified (read-only attribute, library file or a file checked in to Vault). Do not save it; ask the user.");
                allowed = new[] { "inventor_get_document_state" };
                break;
            default:
                throw new ArgumentException("Unknown document mode.", nameof(mode));
        }
        if (needsMigrating && mode != ReadOnly)
            rules.Add($"The file was last saved in an older Inventor ({savedInVersion ?? "unknown version"}). Saving converts it to Inventor {hostYear} format, which older Inventor versions cannot open. Ask the user before saving.");
        return new JObject { ["mode"] = mode, ["rules"] = new JArray(rules), ["allowed"] = new JArray(allowed) };
    }
}
