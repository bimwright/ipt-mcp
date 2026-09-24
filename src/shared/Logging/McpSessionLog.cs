using System;
using System.Collections.ObjectModel;
using System.Threading;
using Bimwright.Ipt.Shared.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Logging
{
    public class McpCallEntry
    {
        public int Index { get; set; }
        public DateTime Timestamp { get; set; }
        public string ToolName { get; set; } = "";
        public bool Success { get; set; }
        public long DurationMs { get; set; }
        public string? ParamsJson { get; set; }
        public string? ErrorMessage { get; set; }
        public string? CodeSnippet { get; set; }
        public string? ResultJson { get; set; }
        public string? Summary { get; set; }
        public string? ToolDescription { get; set; }
        public int? RerunOfIndex { get; set; }
        /// <summary>ParamsJson exceeded the in-memory cap and was truncated — entry cannot be re-run.</summary>
        public bool ParamsTruncated { get; set; }
        /// <summary>Loaded from mcp-calls.jsonl (a previous session) — read-only, never re-runnable.</summary>
        public bool IsHistorical { get; set; }
        /// <summary>Short session tag for historical entries (e.g. "0923-1442").</summary>
        public string? SessionTag { get; set; }
        /// <summary>
        /// The handler's IsReadOnly at capture time — drives the History "Kind" filter.
        /// Null for unknown commands / historical rows (resolved via the dispatcher then).
        /// ipt-mcp uses the handler flag (authoritative) where rvt-mcp classifies by name.
        /// </summary>
        public bool? IsReadOnly { get; set; }
        /// <summary>Grid label: historical rows get a date prefix to separate them from live rows.</summary>
        public string TimeLabel => IsHistorical
            ? Timestamp.ToString("MM-dd HH:mm")
            : Timestamp.ToString("HH:mm:ss");
    }

    /// <summary>
    /// Per-session in-memory call log behind the History window. Ported from rvt-mcp's
    /// McpSessionLog. Wire commands complete on the transport listener thread while the
    /// bound collection lives on the history window's UI thread, so <see cref="Add"/>
    /// prepares the entry on the caller thread and marshals the collection mutation
    /// through the optional <paramref name="marshal"/> delegate (tests pass null → inline).
    /// </summary>
    public class McpSessionLog
    {
        private const int MaxParamsJsonLength = 64 * 1024;
        private const int MaxCodeSnippetLength = 128 * 1024;
        private const int MaxEntries = 1000;
        private int _nextIndex = 1;
        private readonly Action<Action> _marshal;
        /// <summary>Test seam: swap the privacy config source.</summary>
        public static Func<IptPrivacyConfig> ConfigLoader = IptPrivacyConfig.Load;

        public McpSessionLog(Action<Action>? marshal = null)
        {
            _marshal = marshal ?? (a => a());
        }

        public ObservableCollection<McpCallEntry> Entries { get; } = new ObservableCollection<McpCallEntry>();

        public event Action<McpCallEntry>? EntryAdded;

        public void Add(McpCallEntry entry)
        {
            ApplyPrivacyPolicy(entry);
            entry.Index = Interlocked.Increment(ref _nextIndex) - 1;
            if (entry.Timestamp == default)
                entry.Timestamp = DateTime.Now;
            // Bound session memory: evict the oldest beyond the cap. Runs on the UI
            // thread via _marshal so the bound DataGrid never sees a cross-thread write.
            _marshal(() =>
            {
                while (Entries.Count >= MaxEntries)
                    Entries.RemoveAt(0);
                Entries.Add(entry);
                EntryAdded?.Invoke(entry);
            });
        }

        public void Clear()
        {
            // "Clear Session" drops live rows only — historical rows loaded from
            // the file log are not part of this session and stay visible.
            for (var i = Entries.Count - 1; i >= 0; i--)
                if (!Entries[i].IsHistorical)
                    Entries.RemoveAt(i);
            Interlocked.Exchange(ref _nextIndex, 1);
        }

        public int Count => Entries.Count;

        private static void ApplyPrivacyPolicy(McpCallEntry entry)
        {
            if (entry == null)
                return;

            var isSendCode = string.Equals(entry.ToolName, "send_code", StringComparison.OrdinalIgnoreCase);
            entry.ErrorMessage = McpResponsePrivacy.RedactErrorForResponse(entry.ErrorMessage);
            entry.ResultJson = BakeRedactor.RedactForBake(entry.ResultJson, redactResultFields: isSendCode);

            if (!isSendCode)
            {
                // Bound in-memory size for fat payloads (e.g. batch_execute); the
                // file log caps params separately at 8KB.
                if (entry.ParamsJson != null && entry.ParamsJson.Length > MaxParamsJsonLength)
                {
                    entry.ParamsJson = entry.ParamsJson.Substring(0, MaxParamsJsonLength) + "... (truncated)";
                    entry.ParamsTruncated = true;
                }
                return;
            }

            var cacheBodies = false;
            try { cacheBodies = ConfigLoader?.Invoke()?.CacheSendCodeBodiesOrDefault ?? false; }
            catch { }

            if (cacheBodies)
            {
                // ParamsJson keeps the full body so re-run still works; bound only
                // the display copy (CodeSnippet feeds the INPUT code view).
                if (entry.CodeSnippet != null && entry.CodeSnippet.Length > MaxCodeSnippetLength)
                    entry.CodeSnippet = entry.CodeSnippet.Substring(0, MaxCodeSnippetLength) + "... (truncated)";
                return;
            }

            var code = ExtractCodeBody(entry.ParamsJson, entry.CodeSnippet);
            var codeHash = BakeRedactor.HashBody(code);
            entry.ParamsJson = JsonConvert.SerializeObject(new
            {
                code_hash = codeHash,
                code_length = code.Length
            }, Formatting.None);
            entry.CodeSnippet = null;
            entry.Summary = $"send_code body redacted; code_hash={codeHash}; code_length={code.Length}";
        }

        private static string ExtractCodeBody(string? paramsJson, string? codeSnippet)
        {
            if (!string.IsNullOrEmpty(codeSnippet))
                return codeSnippet;
            if (string.IsNullOrEmpty(paramsJson))
                return string.Empty;

            try
            {
                var obj = JObject.Parse(paramsJson);
                var code = obj["code"];
                if (code == null || code.Type == JTokenType.Null)
                    return string.Empty;
                if (code.Type == JTokenType.String)
                    return code.Value<string>() ?? string.Empty;
                return code.ToString(Formatting.None);
            }
            catch
            {
                return paramsJson;
            }
        }
    }
}
