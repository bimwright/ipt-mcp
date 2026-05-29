using System;
using System.Text.RegularExpressions;

namespace Bimwright.Inventor.Shared.Security;

public static class ErrorSanitizer
{
    public static string Sanitize(Exception ex)
    {
        var msg = ex.Message?.Replace("\r", " ").Replace("\n", " ").Trim() ?? ex.GetType().Name;
        // strip Windows file paths
        msg = Regex.Replace(msg, @"[A-Za-z]:\\[^ ]+", "<path>");
        return SecretMasker.Mask(msg);
    }
}
