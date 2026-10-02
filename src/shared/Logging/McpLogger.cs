using System;
using System.IO;
using System.Threading;
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
        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB
        // Field caps feed both the wire log and the History window's past-session view —
        // sized so most calls stay fully inspectable after the fact.
        private const int MaxLoggedParamsLength = 8 * 1024;
        private const int MaxLoggedResultLength = 10 * 1024;
        private const int MaxLoggedErrorLength = 4 * 1024;
        /// <summary>Test seam: redirect the journal dir away from %LOCALAPPDATA%.</summary>
        public static string? LocalAppDataOverride { get; set; }

        /// <summary>
        /// Append one JSONL line under a named mutex — serializes writers across threads
        /// (listener + STA re-run) AND across processes (two Inventor instances share
        /// mcp-calls.jsonl). Mutex failure degrades to an unlocked append rather than
        /// dropping the line.
        /// </summary>
        internal static void AppendLineLocked(string path, string line)
        {
            Mutex? mutex = null;
            try { mutex = new Mutex(false, @"Local\Bimwright.Ipt." + Path.GetFileName(path)); } catch { }
            var held = false;
            if (mutex != null)
            {
                try { held = mutex.WaitOne(2000); }
                catch (AbandonedMutexException) { held = true; }  // acquired despite the previous owner dying
                catch { }
            }
            try { File.AppendAllText(path, line + "\n"); }
            finally
            {
                if (held) try { mutex!.ReleaseMutex(); } catch { }
                try { mutex?.Dispose(); } catch { }
            }
        }

        /// <summary>The product log dir — the journal root for this add-in instance.</summary>
        internal static string LogDir =>
            LocalAppDataOverride ?? Bimwright.Setup.RuntimeLayout.ForCurrentUser("ipt-mcp").DataRoot;

        public static void Initialize()
        {
            var dir = LogDir;
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "mcp-calls.jsonl");
            _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                          Guid.NewGuid().ToString("N").Substring(0, 4);

            RotateIfNeeded(dir);
        }

        private static void RotateIfNeeded(string dir)
        {
            var versionFile = Path.Combine(dir, "mcp-calls.version");
            int currentVersion = 0;
            if (File.Exists(versionFile))
            {
                int.TryParse(File.ReadAllText(versionFile).Trim(), out currentVersion);
            }

            bool needsRotation = false;
            if (currentVersion < LogVersion)
            {
                // Force rotate: format changed (old logs may lack result or send-code redaction)
                needsRotation = File.Exists(_logPath) || Directory.GetFiles(dir, "mcp-calls-*.jsonl").Length > 0;
            }
            else if (File.Exists(_logPath))
            {
                needsRotation = new FileInfo(_logPath).Length > MaxFileSize;
            }

            if (needsRotation)
            {
                var rotationSucceeded = true;
                try
                {
                    if (currentVersion < LogVersion)
                    {
                        File.Delete(_logPath!);
                        foreach (var archive in Directory.GetFiles(dir, "mcp-calls-*.jsonl"))
                            File.Delete(archive);
                    }
                    else
                    {
                        var archive = Path.Combine(dir,
                            $"mcp-calls-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
                        File.Move(_logPath!, archive);
                    }
                }
                catch
                {
                    rotationSucceeded = false;
                }

                if (!rotationSucceeded && currentVersion < LogVersion)
                {
                    _logPath = null;
                    return;
                }
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

        internal static McpLogSafePayload BuildLogSafePayload(string toolName, string? paramsJson, string? code)
        {
            if (!string.Equals(toolName, "send_code", StringComparison.OrdinalIgnoreCase))
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

            var codeBody = ExtractCodeBody(paramsJson, code);
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

            var redactResultFields = string.Equals(toolName, "send_code", StringComparison.OrdinalIgnoreCase);
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

        private static string ExtractCodeBody(string? paramsJson, string? code)
        {
            if (!string.IsNullOrEmpty(code))
                return code;
            if (string.IsNullOrEmpty(paramsJson))
                return string.Empty;

            try
            {
                var parsed = JObject.Parse(paramsJson);
                var token = parsed["code"];
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
