using System;
using System.IO;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Thumbnail source for capture toasts. The path comes from a successful capture_view result (so it
/// already passed the handler's path policy); this only checks it is a local image we can show.
/// </summary>
public static class ToastThumbnail
{
    public const long MaxBytes = 4 * 1024 * 1024;
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    public static string? PathIfImage(string? path)
    {
        if (path == null || path.Trim().Length == 0) return null;
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return null;   // UNC: no network I/O on the toast thread
            var ext = (Path.GetExtension(full) ?? "").ToLowerInvariant();
            if (Array.IndexOf(Extensions, ext) < 0) return null;
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryLoadBytes(string? path, long maxBytes = MaxBytes)
    {
        if (path == null) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > maxBytes) return null;
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }
}
