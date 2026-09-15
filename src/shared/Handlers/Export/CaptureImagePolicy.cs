using System;
using System.IO;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// Pure helpers for the file-mode of <c>capture_view</c>: validating the output extension and
/// resolving its format label. No Inventor dependency, so it is unit tested without Inventor.
/// </summary>
public static class CaptureImagePolicy
{
    /// <summary>Inline base64 budget (spec F3-c): above this, inline mode is rejected with a
    /// hint to use file mode or shrink the capture.</summary>
    public const int MaxInlineBase64Bytes = 256 * 1024;

    private static readonly string[] Supported = { ".png", ".jpg", ".jpeg", ".bmp" };

    /// <summary>
    /// Root for auto-generated capture files (spec F3-c): <c>BIMWRIGHT_INVENTOR_EXPORT_ROOT</c>
    /// when set to an absolute path, else <c>%LOCALAPPDATA%\Bimwright\ipt-mcp</c>. Both sit under
    /// an allowed export root by definition, so generated paths always satisfy
    /// <c>ExportPathPolicy</c>. A relative or unparsable env value falls back to the default —
    /// resolving it against Inventor.exe's CWD would be unpredictable.
    /// </summary>
    public static string ResolveCaptureRoot()
    {
        var env = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT");
        try
        {
            if (!string.IsNullOrWhiteSpace(env) && Path.IsPathRooted(env))
                return Path.GetFullPath(env);
        }
        catch { /* malformed env value — fall back to the default root */ }

        // GetFolderPath can return "" on a broken profile — GetTempPath is still user-scoped.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
        return Path.Combine(local, "Bimwright", "ipt-mcp");
    }

    /// <summary><c>&lt;root&gt;\captures\capture-&lt;yyyyMMdd-HHmmss&gt;-&lt;seq&gt;.png</c></summary>
    public static string DefaultCapturePath(string root, DateTime utcNow, int sequence)
        => Path.Combine(root, "captures",
            $"capture-{utcNow:yyyyMMdd-HHmmss}-{sequence:D3}.png");

    /// <summary>True (reject) when an inline-mode base64 payload exceeds the inline budget.</summary>
    public static bool TryRejectInline(long base64Bytes, out string rejection)
    {
        if (base64Bytes > MaxInlineBase64Bytes)
        {
            rejection =
                $"captured image is {base64Bytes} base64 bytes, over the {MaxInlineBase64Bytes}-byte inline budget; " +
                "use file mode (omit inline) or reduce width/height.";
            return true;
        }
        rejection = "";
        return false;
    }

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
