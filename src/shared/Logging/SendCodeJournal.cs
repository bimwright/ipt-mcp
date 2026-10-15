using System;
using System.Collections.Generic;
using System.IO;
using Bimwright.Ipt.Shared.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Logging
{
    /// <summary>
    /// Opt-in journal of bake-redacted send_code bodies — lets History recover a re-runnable
    /// body for entries whose ParamsJson was privacy-redacted to {code_hash, code_length}.
    /// Ported from rvt-mcp: same file layout (send-code-journal.jsonl + timestamped archives
    /// under the ipt-mcp descriptor dir), same TTL/purge rules via IptPrivacyConfig.
    /// </summary>
    public static class SendCodeJournal
    {
        public const long MaxFileSize = 5 * 1024 * 1024; // 5MB
        /// <summary>Test seam: redirect the journal dir away from %LOCALAPPDATA%.</summary>
        public static string? LocalAppDataOverride { get; set; }

        private static string RootDir =>
            LocalAppDataOverride ?? McpLogger.LogDir;

        public static string JournalPath => Path.Combine(RootDir, "send-code-journal.jsonl");

        public static void RunMaintenance(IptPrivacyConfig? config, DateTimeOffset? now = null)
        {
            try
            {
                BoundedLogFile.WithLock(JournalPath, () =>
                {
                    var utc = now ?? DateTimeOffset.UtcNow;
                    MaybePurge(RootDir, config != null && config.IsPersistSendCodeBodiesActive(utc), utc);
                    BoundedLogFile.MaintainUnderLock(JournalPath, now: utc);
                    return true;
                });
            }
            catch { }
        }

        public static bool TryAppend(
            IptPrivacyConfig? config,
            string sessionId,
            string? rawCode,
            bool success,
            long durationMs,
            string? error,
            string? resultJson,
            DateTimeOffset? now = null)
        {
            try
            {
                return BoundedLogFile.WithLock(JournalPath, () => AppendUnderLock(
                    config, sessionId, rawCode, success, durationMs, error, resultJson, now));
            }
            catch { return false; }
        }

        private static bool AppendUnderLock(IptPrivacyConfig? config, string sessionId, string? rawCode,
            bool success, long durationMs, string? error, string? resultJson, DateTimeOffset? now)
        {
            var utc = now ?? DateTimeOffset.UtcNow;
            var root = RootDir;
            var isActive = config != null && config.IsPersistSendCodeBodiesActive(utc);

            // Maintenance: purge if inactive/expired, remove marker if active
            MaybePurge(root, isActive, utc);

            if (!isActive)
                return false;

            var raw = rawCode ?? string.Empty;
            // Even before redaction/serialization, reject a body that cannot fit a record.
            if (raw.Length >= MaxFileSize) return false;
            var entry = new
            {
                timestamp = utc.ToString("o"),
                session_id = sessionId,
                success,
                duration_ms = durationMs,
                code_hash = BakeRedactor.HashBody(raw),
                code_length = raw.Length,
                error = McpLogger.RedactAndTruncate(error, 2048),
                code = BakeRedactor.RedactForBake(raw, redactResultFields: true),
                result = McpLogger.BuildLogSafeResult("send_code", resultJson)
            };

            try
            {
                var line = JsonConvert.SerializeObject(entry, Formatting.None);
                BoundedLogFile.AppendUnderLock(JournalPath, line, now: utc);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Journal body matching a code_hash, or null. Scans the live journal then
        /// rotated archives newest-first; first match wins (same hash ⇒ same body).
        /// Bodies are bake-redacted (paths/secrets become placeholders) — callers
        /// re-running a recovered body must surface that caveat.
        /// </summary>
        public static string? TryFindCodeByHash(string? codeHash)
        {
            if (string.IsNullOrEmpty(codeHash)) return null;
            try
            {
                return BoundedLogFile.WithLock(JournalPath, () =>
                {
                    foreach (var file in JournalFilesNewestFirst())
                    {
                        var body = FindInFile(file, codeHash);
                        if (body != null) return body;
                    }
                    return (string?)null;
                });
            }
            catch { return null; }
        }

        private static IEnumerable<string> JournalFilesNewestFirst()
        {
            var dir = Path.GetDirectoryName(JournalPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;

            var files = BoundedLogFile.FilesForPath(JournalPath);
            var current = Path.GetFullPath(JournalPath);
            if (Array.Exists(files, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase))) yield return current;

            // Archive names carry a timestamp → ordinal-desc = newest first.
            var archives = Array.FindAll(files, p => !string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
            Array.Sort(archives, StringComparer.OrdinalIgnoreCase);
            for (var i = archives.Length - 1; i >= 0; i--)
                yield return archives[i];
        }

        private static string? FindInFile(string path, string? codeHash)
        {
            try
            {
                foreach (var line in BoundedLogFile.ReadLinesSafe(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var obj = JObject.Parse(line);
                        if (string.Equals(obj.Value<string>("code_hash"), codeHash, StringComparison.Ordinal))
                            return obj.Value<string>("code");
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static void MaybePurge(string rootDir, bool isActive, DateTimeOffset now)
        {
            var disabledMarker = Path.Combine(rootDir, "send-code-journal.disabled-at");
            BoundedLogFile.RefuseLink(disabledMarker);
            if (isActive)
            {
                if (File.Exists(disabledMarker))
                {
                    try { File.Delete(disabledMarker); } catch { }
                }
                return;
            }

            // Write marker if not present and journal files exist
            var files = BoundedLogFile.FilesForPath(JournalPath);
            bool hasFiles = files.Length > 0;
            if (hasFiles && !File.Exists(disabledMarker))
            {
                try
                {
                    File.WriteAllText(disabledMarker, now.ToString("o"));
                }
                catch { }
            }

            if (File.Exists(disabledMarker))
            {
                try
                {
                    if (new FileInfo(disabledMarker).Length > 128) return;
                    var text = File.ReadAllText(disabledMarker);
                    if (DateTimeOffset.TryParse(text, out var disabledAt))
                    {
                        if (now - disabledAt >= TimeSpan.FromDays(7))
                        {
                            foreach (var f in files)
                            {
                                BoundedLogFile.RefuseLink(f);
                                File.Delete(f);
                            }
                            File.Delete(disabledMarker);
                        }
                    }
                }
                catch { }
            }
        }

    }
}
