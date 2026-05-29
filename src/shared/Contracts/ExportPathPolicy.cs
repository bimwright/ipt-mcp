using System;
using System.IO;

namespace Bimwright.Ipt.Shared.Contracts;

public static class ExportPathPolicy
{
    public static bool TryRejectPath(string? outputPath, out string rejection)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            rejection = "output_path is required.";
            return true;
        }

        string full;
        try
        {
            full = Path.GetFullPath(outputPath);
        }
        catch (Exception ex)
        {
            rejection = "output_path is not a valid path: " + ex.Message;
            return true;
        }

        if (!Path.IsPathRooted(full) || string.IsNullOrEmpty(Path.GetFileName(full)))
        {
            rejection = "output_path must be an absolute file path.";
            return true;
        }

        var dir = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(dir) || !IsUnderAllowedRoot(full))
        {
            rejection = "output_path must be under an allowed output root (user profile or temp directory).";
            return true;
        }

        rejection = "";
        return false;
    }

    private static bool IsUnderAllowedRoot(string fullPath)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath(),
            Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT") ?? ""
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
