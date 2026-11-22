using System;

namespace Bimwright.Ipt.Shared.Logging
{
    /// <summary>
    /// BIMWRIGHT_KEEP_ALL_LOGS (family switch, same in rvt/dwg/nwd/ipt): when on, automatic deletion of
    /// logs, call records, journals, reports and generated captures is skipped. Rotation still renames to timestamped
    /// archives. It never enables a recorder or changes what is recorded. Read in the server and the add-in.
    /// </summary>
    public static class LogRetentionPolicy
    {
        public const string EnvironmentVariable = "BIMWRIGHT_KEEP_ALL_LOGS";

        /// <summary>Test seam: tests pin the value here and never read the real environment.</summary>
        internal static Func<string?> ReadValue = () => Environment.GetEnvironmentVariable(EnvironmentVariable);

        public static bool KeepAll => IsOn(ReadValue());

        /// <summary>1, true, yes or on (case-insensitive, trimmed); anything else is off.</summary>
        public static bool IsOn(string? value)
        {
            var v = (value ?? "").Trim();
            return v == "1"
                || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
