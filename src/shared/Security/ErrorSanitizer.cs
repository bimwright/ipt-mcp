using System;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

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

    /// <summary>
    /// In-place sanitize of every <c>error</c>/<c>message</c> string field in a JSON tree —
    /// the same policy the dispatcher applies to result data, exposed so pre-spill writers
    /// (ResponseSpillWriter) persist already-clean content instead of waiting for the
    /// dispatcher pass that runs after the spill file is written.
    /// </summary>
    public static void SanitizeErrorFields(JObject obj)
    {
        foreach (var property in obj.Properties())
        {
            if (property.Value is JObject nested)
            {
                SanitizeErrorFields(nested);
                continue;
            }

            if (property.Value is JArray arr)
            {
                foreach (var item in arr.OfType<JObject>())
                    SanitizeErrorFields(item);
                continue;
            }

            if (property.Value.Type == JTokenType.String &&
                (string.Equals(property.Name, "error", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(property.Name, "message", StringComparison.OrdinalIgnoreCase)))
            {
                property.Value = Sanitize((string?)property.Value);
            }
        }
    }
}
