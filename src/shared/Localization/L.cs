#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bimwright.Ipt.Shared.Localization
{
    /// <summary>
    /// English-only string table for plugin UI strings. Keeps the same call
    /// signature as rvt-mcp's L.T (key + named placeholders, "{name}" or
    /// "{name:n}" for thousands-separated numbers) so a future catalog port
    /// does not touch call sites. Missing keys return the key, never throw.
    /// </summary>
    public static class L
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["toast.connected.title"] = "Agent connected",
            ["toast.connected.summary"] = "ipt-mcp is ready",
            ["toast.activity.success"] = "Success",
            ["toast.activity.failed"] = "Failed",
            ["toast.activity.capture"] = "Capture",
            ["toast.activity.open_image"] = "Open image",
            ["toast.status.enabled"] = "Toast notifications enabled",
            ["toast.status.enabled.summary"] = "New activity will appear here.",
            ["toast.status.disabled"] = "Toast notifications disabled",
            ["toast.status.disabled.summary"] = "New activity is hidden until toast notifications are enabled.",
            ["toast.status.saveFailed"] = "Preference could not be saved; this session is still using the new state.",
        };

        public static string T(string key, params (string Name, object Value)[] args)
        {
            if (key == null)
                return string.Empty;
            if (!En.TryGetValue(key, out var template))
                return key;
            if (args == null || args.Length == 0)
                return template;

            var text = template;
            foreach (var (name, value) in args)
            {
                var formatted = FormatValue(value, text, name);
                text = text.Replace("{" + name + ":n}", formatted)
                           .Replace("{" + name + "}", formatted);
            }
            return text;
        }

        private static string FormatValue(object value, string template, string name)
        {
            if (value == null)
                return string.Empty;
            if (template != null && template.Contains("{" + name + ":n}")
                && value is IConvertible c)
            {
                try { return c.ToDouble(CultureInfo.InvariantCulture).ToString("n0", CultureInfo.InvariantCulture); }
                catch { }
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
