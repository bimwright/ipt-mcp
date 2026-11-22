using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Reports;

/// <summary>
/// Saved agent reports (family contract FC-RPT-04; rvt-mcp ReportStore parity): one JSON file per report in
/// <c>%LOCALAPPDATA%\Bimwright\ipt-mcp\reports\</c>, named <c>yyyyMMddTHHmmssfffZ-&lt;id&gt;.json</c> so names sort by time.
/// Resending a task_id from this gateway process replaces its report. The newest <see cref="Keep"/> files are kept.
/// </summary>
internal sealed class ReportStore
{
    internal const int Keep = 500;
    private const int MaxListLimit = 50;

    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Id, string Path, string Created)> _tasks = new(StringComparer.Ordinal);

    internal ReportStore(string directory) => _directory = Path.GetFullPath(directory);

    internal string Directory => _directory;

    /// <summary>Saves <paramref name="report"/> (its report_id, created_utc and updated_utc are set here).</summary>
    internal (string Id, bool Replaced, string Path) Save(JObject report)
    {
        var taskId = (string?)report["task_id"] ?? throw new ArgumentException("task_id is required.");
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(_directory);
            var now = DateTime.UtcNow;
            var replaced = _tasks.TryGetValue(taskId, out var saved);
            if (!replaced)
            {
                var id = Guid.NewGuid().ToString("N");
                saved = (id, Path.Combine(_directory, now.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + id + ".json"),
                    now.ToString("o", CultureInfo.InvariantCulture));
            }
            // A resend keeps the writes the first report recorded (they were taken from the tracker then).
            if (replaced && Read(saved.Path)?["calls"] is JArray earlier && earlier.Count > 0)
            {
                var merged = new JArray(earlier);
                foreach (var call in report["calls"] as JArray ?? new JArray()) merged.Add(call);
                report["calls"] = merged;
            }
            report["report_id"] = saved.Id;
            report["created_utc"] = saved.Created;
            report["updated_utc"] = now.ToString("o", CultureInfo.InvariantCulture);
            var temp = saved.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(report.ToString(Formatting.Indented));
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(saved.Path)) File.Replace(temp, saved.Path, null);
                else File.Move(temp, saved.Path);
                _tasks[taskId] = saved;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            if (!replaced && !Bimwright.Ipt.Shared.Logging.LogRetentionPolicy.KeepAll) Prune();
            return (saved.Id, replaced, saved.Path);
        }
    }

    /// <summary>Newest first: a page of report summaries (no body, no calls), plus the total count.</summary>
    internal (JArray Items, int Total) List(int offset, int limit)
    {
        var files = Files();
        var items = new JArray();
        foreach (var path in files.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, MaxListLimit)))
        {
            if (Read(path) is not { } r) { items.Add(new JObject { ["file"] = Path.GetFileName(path), ["unreadable"] = true }); continue; }
            items.Add(new JObject
            {
                ["report_id"] = r["report_id"], ["task_id"] = r["task_id"], ["outcome"] = r["outcome"], ["summary"] = r["summary"],
                ["created_utc"] = r["created_utc"], ["updated_utc"] = r["updated_utc"],
                ["has_body"] = r["body"] is JValue { Type: JTokenType.String },
                ["call_count"] = (r["calls"] as JArray)?.Count ?? 0,
                ["target_id"] = r["source"]?["target_id"],
            });
        }
        return (items, files.Length);
    }

    /// <summary>The full report with this 32-hex id, or null.</summary>
    internal JObject? Get(string reportId)
    {
        if (!IsReportId(reportId)) return null;
        var path = Files().FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).EndsWith("-" + reportId, StringComparison.OrdinalIgnoreCase));
        return path == null ? null : Read(path);
    }

    internal static bool IsReportId(string? id) =>
        id is { Length: 32 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private string[] Files()
    {
        lock (_gate)
        {
            if (!System.IO.Directory.Exists(_directory)) return Array.Empty<string>();
            return System.IO.Directory.GetFiles(_directory, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();
        }
    }

    private static JObject? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return JObject.Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) { return null; }
    }

    private void Prune()
    {
        foreach (var old in System.IO.Directory.GetFiles(_directory, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(Keep))
            try { File.Delete(old); } catch { /* next save retries */ }
    }
}
