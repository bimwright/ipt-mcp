using System;
using System.IO;
using System.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// The save policy <c>save_document</c> enforces (rvt-mcp parity: DocumentSavePolicy / SaveAsPathPolicy),
/// the same rules <c>get_document_state</c> reports. Host-free so it is unit-tested; the handler reads
/// Inventor and the file system and passes the facts in. Results are stable <c>data.reason</c> codes.
/// </summary>
public static class DocumentSavePolicy
{
    /// <summary>A successful no-op: nothing to save.</summary>
    public const string NoChanges = "no_changes";

    /// <summary>Save in place. Null to save, <see cref="NoChanges"/> for a no-op, else the refusal reason.</summary>
    /// <param name="mode">A <see cref="DocumentStatePolicy"/> mode.</param>
    /// <param name="modified">The document, or for a part/assembly one of its referenced documents, has unsaved changes.</param>
    public static string? CheckSave(string mode, bool fileExists, bool modified, bool needsMigrating, bool allowFormatUpgrade)
    {
        switch (mode)
        {
            case DocumentStatePolicy.Unsaved: return "DOCUMENT_NEVER_SAVED";
            case DocumentStatePolicy.Template: return "TEMPLATE_SAVE_AS_REQUIRED";
            case DocumentStatePolicy.ReadOnly: return "FILE_READ_ONLY";
            case DocumentStatePolicy.LocalFile: break;
            default: throw new ArgumentException("Unknown document mode.", nameof(mode));
        }
        if (!fileExists) return "FILE_MISSING_ON_DISK";
        if (!modified) return NoChanges;
        if (needsMigrating && !allowFormatUpgrade) return "FORMAT_UPGRADE_REQUIRES_APPROVAL";
        return null;
    }

    /// <summary>File extensions Save-As may use for an Inventor document type name (DocumentTypeEnum without the k/Object wrapping).</summary>
    public static string[] ExtensionsFor(string documentType) => documentType switch
    {
        "Part" => new[] { ".ipt" },
        "Assembly" => new[] { ".iam" },
        "Drawing" => new[] { ".idw", ".dwg" },
        "Presentation" => new[] { ".ipn" },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// Save-As to <paramref name="target"/>: null when it is a new file on a local drive with the right extension in an
    /// existing folder, else the refusal reason. Existing files are never overwritten (save in place omits the path).
    /// </summary>
    public static string? CheckSaveAsPath(string? target, string[] extensions, Func<string, bool> fileExists,
        Func<string, bool> folderExists, Func<string, bool> isNetworkDrive)
    {
        var t = (target ?? "").Trim();
        if (t.StartsWith(@"\\", StringComparison.Ordinal) || t.StartsWith("//", StringComparison.Ordinal))
            return "SAVE_AS_PATH_NOT_LOCAL";
        // Path.IsPathRooted also accepts "\file.ipt" and "C:file.ipt", which resolve against Inventor's current folder.
        if (t.Length < 3 || !char.IsLetter(t[0]) || t[1] != ':' || (t[2] != '\\' && t[2] != '/'))
            return "SAVE_AS_PATH_NOT_ABSOLUTE";
        string full;
        try { full = Path.GetFullPath(t); } catch { return "SAVE_AS_PATH_NOT_ABSOLUTE"; }
        if (isNetworkDrive(full.Substring(0, 3))) return "SAVE_AS_PATH_NOT_LOCAL";
        if (!extensions.Any(e => full.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return "SAVE_AS_WRONG_EXTENSION";
        if (fileExists(full)) return "SAVE_AS_TARGET_EXISTS";
        if (!folderExists(Path.GetDirectoryName(full) ?? "")) return "SAVE_AS_FOLDER_MISSING";
        return null;
    }
}
