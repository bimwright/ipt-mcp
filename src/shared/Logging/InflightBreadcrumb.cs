using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Logging
{
    /// <summary>
    /// Marks the wire command that is running on Inventor's STA thread in <c>inflight-&lt;pid&gt;.json</c>.
    /// The call journal is only written after a command returns, so a command that kills Inventor
    /// leaves no line. The marker survives the crash; the next add-in start turns it into a
    /// <c>crash_suspected</c> journal line. Written only when the call log is on. Best effort.
    /// </summary>
    public sealed class InflightBreadcrumb
    {
        private readonly string _path;
        private readonly int _pid;
        private readonly DateTime _processStartUtc;

        public InflightBreadcrumb(string dir, int pid, DateTime processStartUtc)
        {
            _path = PathFor(dir, pid);
            _pid = pid;
            _processStartUtc = processStartUtc;
        }

        internal static string PathFor(string dir, int pid) => Path.Combine(dir, "inflight-" + pid + ".json");

        /// <summary>Writes the marker before dispatch. Params are made log-safe exactly like the call journal.</summary>
        public void Begin(string requestId, string tool, string? paramsJson)
        {
            try
            {
                if (McpLogger.CurrentLogPath == null) McpLogger.Initialize(); // the journal session id this command will log under
                var safe = McpLogger.BuildLogSafePayload(tool, paramsJson, null);
                var marker = new JObject
                {
                    ["request_id"] = requestId,
                    ["tool"] = tool,
                    ["started_utc"] = DateTime.UtcNow.ToString("o"),
                    ["session_id"] = McpLogger.CurrentSessionId,
                    ["pid"] = _pid,
                    ["process_start_utc"] = _processStartUtc.ToString("o"),
                    ["params"] = safe.Params is null ? null : JToken.FromObject(safe.Params),
                };
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, marker.ToString(Formatting.None));
            }
            catch { }
        }

        public void End()
        {
            try { File.Delete(_path); } catch { }
        }

        /// <summary>
        /// Markers left by Inventor processes that are gone (or whose pid now belongs to another
        /// process). Each one is returned and deleted. Markers of live processes are kept.
        /// </summary>
        public static IReadOnlyList<JObject> TakeOrphans(string dir, int currentPid, Func<int, DateTime?, bool> isSameProcessRunning)
        {
            var orphans = new List<JObject>();
            try
            {
                if (!Directory.Exists(dir)) return orphans;
                foreach (var file in Directory.GetFiles(dir, "inflight-*.json"))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 64 * 1024) { File.Delete(file); continue; }
                        // Keep ISO timestamps as strings; default parsing turns them into culture-formatted dates.
                        var marker = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(file),
                            new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })
                            ?? throw new JsonReaderException("empty marker");
                        var pid = marker.Value<int?>("pid") ?? 0;
                        if (pid == currentPid) continue;
                        DateTime? start = DateTime.TryParse(marker.Value<string>("process_start_utc"), null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var s) ? s : (DateTime?)null;
                        if (pid > 0 && isSameProcessRunning(pid, start)) continue;
                        orphans.Add(marker);
                        File.Delete(file);
                    }
                    catch (JsonException) { try { File.Delete(file); } catch { } }
                    catch { }
                }
            }
            catch { }
            return orphans;
        }

        /// <summary>True when <paramref name="pid"/> runs and, if known, started at <paramref name="startUtc"/>.</summary>
        public static bool IsSameProcessRunning(int pid, DateTime? startUtc)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return false;
                if (startUtc is null) return true;
                return Math.Abs((p.StartTime.ToUniversalTime() - startUtc.Value.ToUniversalTime()).TotalSeconds) < 2;
            }
            catch { return false; }
        }
    }
}
