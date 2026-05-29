using System;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Security;

public static class ErrorSanitizer
{
    public static string Sanitize(Exception ex)
        => Sanitize(ex.Message ?? ex.GetType().Name);

    public static string Sanitize(string? message)
    {
        var msg = message?.Replace("\r", " ").Replace("\n", " ").Trim() ?? "";
        // strip Windows file paths
        msg = Regex.Replace(msg, @"[A-Za-z]:\\[^ ]+", "<path>");
        return SecretMasker.Mask(msg);
    }
}
