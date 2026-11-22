using System;
using System.IO;
using Bimwright.Ipt.Shared.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Logging
{
    /// <summary>
    /// Plugin-side call journal — <c>mcp-calls.jsonl</c> under the ipt-mcp descriptor dir.
    /// One JSON line per executed wire command; powers the History window's "Load past
    /// sessions" view. Ported from rvt-mcp's McpLogger — same line shape (timestamp /
    /// session_id / tool / success / duration_ms / error / code / params / result) so the
    /// loaders stay interchangeable across the family.
    /// </summary>
    public static class McpLogger
    {
        private static string? _logPath;
        private static string? _sessionId;
        public static string CurrentSessionId => _sessionId ?? "";
        /// <summary>Path of the active mcp-calls.jsonl, or null when logging is disabled.</summary>
        public static string? CurrentLogPath => _logPath;
        private const int LogVersion = 1;
        private static readonly object InitializationLock = new object();
        // Field caps feed both the wire log and the History window's past-session view —
        // sized so most calls stay fully inspectable after the fact.
        private const int MaxLoggedParamsLength = 8 * 1024;
        private const int MaxLoggedResultLength = 10 * 1024;
        private const int MaxLoggedErrorLength = 4 * 1024;
        /// <summary>Test seam: redirect the journal dir away from %LOCALAPPDATA%.</summary>
        public static string? LocalAppDataOverride { get; set; }

        /// <summary>
        /// Append, rotation and retention use the same full-path mutex across processes.
        /// A lock failure drops the record; it never appends without the lock.
        /// </summary>
        internal static void AppendLineLocked(string path, string line)
        {
            BoundedLogFile.Append(path, line);
        }

        /// <summary>The product log dir — the journal root for this add-in instance.</summary>
        internal static string LogDir =>
            LocalAppDataOverride ?? Bimwright.Setup.RuntimeLayout.ForCurrentUser("ipt-mcp").DataRoot;

        public static void Initialize()
        {
            lock (InitializationLock)
            {
                _logPath = null;
                _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                              Guid.NewGuid().ToString("N").Substring(0, 4);
                try
                {
                    var path = Path.Combine(LogDir, "mcp-calls.jsonl");
                    BoundedLogFile.WithLock(path, () => { EnsureCurrentVersion(path); return true; });
                    BoundedLogFile.Maintain(path);
                    _logPath = path;
                }
                catch { } // Logging cannot prevent add-in startup or bypass a failed lock.
            }
        }

        public static void RunMaintenance()
        {
            try { BoundedLogFile.Maintain(Path.Combine(LogDir, "mcp-calls.jsonl")); }
            catch { }
        }

        private static void EnsureCurrentVersion(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            var versionFile = Path.Combine(dir, "mcp-calls.version");
            BoundedLogFile.RefuseLink(versionFile);
            var currentVersion = 0;
            if (File.Exists(versionFile) && new FileInfo(versionFile).Length <= 32)
                int.TryParse(File.ReadAllText(versionFile).Trim(), out currentVersion);
            // Preserve the privacy migration: legacy logs may contain source bodies.
            // Keep-all skips the delete but still writes the version, so turning it off later deletes nothing.
            if (currentVersion < LogVersion && !LogRetentionPolicy.KeepAll)
                foreach (var file in BoundedLogFile.FilesForPath(path))
                {
                    BoundedLogFile.RefuseLink(file);
                    File.Delete(file);
                }
            File.WriteAllText(versionFile, LogVersion.ToString());
        }

        public static void Log(string toolName, string? paramsJson, bool success,
                                long durationMs, string? errorMsg = null,
                                string? code = null, string? resultJson = null, bool? enabled = null)
        {
            if (enabled == false) return;
            if (enabled == true && _logPath == null) Initialize();
            if (_logPath == null) return;
            try
            {
                var safePayload = BuildLogSafePayload(toolName, paramsJson, code);

                var entry = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    session_id = _sessionId,
                    tool = toolName,
                    success,
                    duration_ms = durationMs,
                    error = RedactAndTruncate(errorMsg, MaxLoggedErrorLength),
                    code = safePayload.Code,
                    @params = safePayload.Params,
                    result = BuildLogSafeResult(toolName, resultJson)
                };
                var line = JsonConvert.SerializeObject(entry, Formatting.None);
                AppendLineLocked(_logPath, line);
            }
            catch { }
        }

        /// <summary>
        /// Journal line for a command whose Inventor process ended while it ran (an orphaned
        /// <see cref="InflightBreadcrumb"/> marker). Keeps the original session id.
        /// </summary>
        public static void LogCrashSuspected(JObject marker)
        {
            if (_logPath == null) Initialize();
            if (_logPath == null) return;
            try
            {
                AppendLineLocked(_logPath, BuildCrashSuspectedEntry(marker).ToString(Formatting.None));
            }
            catch { }
        }

        internal static JObject BuildCrashSuspectedEntry(JObject marker) => new JObject
        {
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["session_id"] = marker.Value<string>("session_id"),
            ["tool"] = marker.Value<string>("tool"),
            ["success"] = false,
            ["duration_ms"] = null,
            ["error"] = "crash_suspected: Inventor (pid " + marker.Value<int?>("pid") + ") ended while this command was running; started "
                        + marker.Value<string>("started_utc") + ".",
            ["code"] = null,
            ["params"] = marker["params"]?.DeepClone(),
            ["result"] = null,
            ["outcome"] = "crash_suspected",
            ["request_id"] = marker.Value<string>("request_id"),
            ["started_utc"] = marker.Value<string>("started_utc"),
        };

        internal static McpLogSafePayload BuildLogSafePayload(string toolName, string? paramsJson, string? code)
        {
            var sourceField = CallLogPrivacy.SourceField(toolName);
            if (sourceField is null)
            {
                object? safeParams = null;
                try
                {
                    if (paramsJson != null)
                    {
                        var redacted = CallLogPrivacy.Redact(JToken.Parse(paramsJson));
                        var text = redacted.ToString(Formatting.None);
                        safeParams = text.Length <= MaxLoggedParamsLength ? redacted : new JObject {
                            ["truncated"] = true, ["preview"] = text.Substring(0, MaxLoggedParamsLength)
                        };
                    }
                }
                catch (JsonReaderException) { safeParams = RedactAndTruncate(paramsJson, MaxLoggedParamsLength); }
                return new McpLogSafePayload
                {
                    Code = null,
                    Params = safeParams
                };
            }

            var codeBody = ExtractCodeBody(paramsJson, code, sourceField);
            return new McpLogSafePayload
            {
                Code = null,
                Params = new JObject
                {
                    ["code_hash"] = BakeRedactor.HashBody(codeBody),
                    ["code_length"] = codeBody.Length
                }
            };
        }

        internal static string? BuildLogSafeResult(string toolName, string? resultJson)
        {
            if (resultJson == null)
                return null;

            var redactResultFields = CallLogPrivacy.SourceField(toolName) is not null;
            return RedactAndTruncate(resultJson, MaxLoggedResultLength, redactResultFields);
        }

        internal static string? RedactAndTruncate(string? value, int maxLength, bool redactResultFields = false)
        {
            if (value == null)
                return null;

            var redacted = BakeRedactor.RedactForBake(value, redactResultFields);
            return redacted.Length > maxLength ? redacted.Substring(0, maxLength) : redacted;
        }

        private static object? ParseParams(string? paramsJson)
        {
            try { return paramsJson == null ? null : JToken.Parse(paramsJson); }
            catch { return paramsJson; }
        }

        private static string ExtractCodeBody(string? paramsJson, string? code, string sourceField)
        {
            if (!string.IsNullOrEmpty(code))
                return code;
            if (string.IsNullOrEmpty(paramsJson))
                return string.Empty;

            try
            {
                var parsed = JObject.Parse(paramsJson);
                var token = parsed[sourceField];
                if (token == null || token.Type == JTokenType.Null)
                    return string.Empty;
                if (token.Type == JTokenType.String)
                    return token.Value<string>() ?? string.Empty;

                return token.ToString(Formatting.None);
            }
            catch
            {
                return paramsJson;
            }
        }

        internal sealed class McpLogSafePayload
        {
            public object? Params { get; set; }
            public string? Code { get; set; }
        }
    }
}
