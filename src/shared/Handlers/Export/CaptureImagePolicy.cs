using System;
using System.IO;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// Pure helpers for the file-mode of <c>capture_view</c>: validating the output extension and
/// resolving its format label. No Inventor dependency, so it is unit tested without Inventor.
/// </summary>
public static class CaptureImagePolicy
{
    private static readonly string[] Supported = { ".png", ".jpg", ".jpeg", ".bmp" };

    /// <summary>True (reject) when the path's extension is not a supported raster image type.</summary>
    public static bool TryRejectImageExtension(string? outputPath, out string rejection)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            rejection = "output_path is required and must end in .png, .jpg, .jpeg, or .bmp.";
            return true;
        }

        var ext = Path.GetExtension(outputPath).ToLowerInvariant();
        if (Array.IndexOf(Supported, ext) < 0)
        {
            rejection = "output_path must end in .png, .jpg, .jpeg, or .bmp.";
            return true;
        }

        rejection = "";
        return false;
    }

    /// <summary>Lowercased extension without the leading dot (e.g. "png"); empty string when none.</summary>
    public static string ResolveFormat(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return "";
        return Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
    }
}
