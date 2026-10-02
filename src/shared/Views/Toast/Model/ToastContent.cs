using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>What the listener knows about one finished command (built in <c>HandleLine</c>).</summary>
public sealed record ToastEvent(
    string Command,
    bool Ok,
    JToken? Data,
    string? ErrorCode,
    string? ErrorMessage,
    long DurationMs,
    bool? HandlerIsReadOnly,
    string? ToolName = null,
    string? Toolset = null,
    string? Description = null,
    int? TimeoutMs = null);

/// <summary>Display copy for one toast.</summary>
public sealed record ToastModel(
    string Command,
    string Title,
    string Category,
    string Summary,
    string Detail,
    string? ThumbnailPath,
    ToolActivityKind Kind,
    bool Success,
    long DurationMs)
{
    /// <summary>Only set for an explicit agent-reported result, never inferred from activity.</summary>
    public string? TaskId { get; init; }

    /// <summary>The agent-reported outcome (completed, failed, cancelled); null for tool results.</summary>
    public string? Outcome { get; init; }
}

/// <summary>Turns an Inventor command result into toast copy. Cheap: never serializes large payloads.</summary>
public static class ToastContentBuilder
{
    public const int SummaryMax = 120;
    public const int DetailMax = 100;
    private const int PreviewChars = 400;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static ToastModel Build(ToastEvent e)
    {
        var command = e.Command == null || e.Command.Trim().Length == 0 ? "unknown" : e.Command;
        var kind = ToolActivityClassifier.Classify(command, e.HandlerIsReadOnly);
        var data = e.Data as JObject;
        if (e.Ok && command == "report_task_result" && Str(data, "task_id") is { } taskId)
        {
            var outcome = Str(data, "outcome") ?? "failed";
            var title = outcome == "completed" ? "Task completed" : outcome == "cancelled" ? "Task cancelled" : "Task failed";
            return new ToastModel(command, title, "MCP · Task",
                Truncate(FirstLine(Str(data, "summary")) ?? "Agent reported a result", SummaryMax),
                Truncate("Agent reported · " + taskId, DetailMax), null,
                ToolActivityKind.Read, outcome != "failed", 0) { TaskId = taskId, Outcome = outcome };
        }
        var softError = e.Ok ? SoftError(command, data) : null;
        var success = e.Ok && softError == null;

        string summary, detail;
        string? thumb = null;
        if (success)
        {
            (summary, detail, thumb) = Describe(command, data);
            if (detail.Length == 0 && e.Description != null) detail = e.Description;
        }
        else
        {
            summary = FirstLine(softError ?? e.ErrorMessage) ?? "Command failed";
            detail = softError != null ? "Script error" : (e.ErrorCode ?? "ERROR");
        }

        return new ToastModel(
            command,
            ToolNameFormatter.Format(e.ToolName?.StartsWith("inventor_", StringComparison.Ordinal) == true ? e.ToolName.Substring(9) : command),
            ToolActivityClassifier.Category(command, kind, success),
            Truncate(summary, SummaryMax),
            Truncate(detail, DetailMax),
            thumb,
            kind,
            success,
            e.DurationMs);
    }

    /// <summary>
    /// Envelope ok, payload failed: send_code / run_baked_tool <c>"ok": false</c>, or a rolled-back batch.
    /// </summary>
    private static string? SoftError(string command, JObject? data)
    {
        if (data == null) return null;
        if (data["ok"] is JValue { Type: JTokenType.Boolean } ok && !(bool)ok)
            return Str(data["error"] as JObject, "message") ?? Preview(data["error"]) ?? "The tool reported a failure";
        if (command == "batch_execute" && data["rolled_back"] is JValue { Type: JTokenType.Boolean } rb && (bool)rb)
            return "Batch rolled back after a failed step";
        return null;
    }

    private static (string Summary, string Detail, string? Thumb) Describe(string command, JObject? data)
    {
        switch (command)
        {
            case "capture_view":
            case "capture_sheet":
                return Capture(data);
            case "get_drawing_info":
                return ("Drawing " + (Str(data,"document") ?? "") + " · " + (Int(data?["sheets"] as JObject,"total") ?? 0) + " sheets", "", null);
            case "new_drawing":
                return ((data?.Value<bool?>("created") == false ? "Reused " : "Created ") + (Str(data,"document") ?? "drawing"), "Unsaved drawing", null);
            case "add_sheet":
                return ((data?.Value<bool?>("created") == false ? "Reused sheet " : "Added sheet ") + (Str(data,"name") ?? ""), Str(data,"code") ?? "", null);
            case "set_title_block":
                return ("Updated " + (Str(data,"title_block") ?? "title block"), Str(data,"sheet") ?? "", null);
            case "add_drawing_view": case "add_section_view": case "edit_drawing_view":
                return ((command == "edit_drawing_view" ? "Updated view " : data?.Value<bool?>("created") == false ? "Reused view " : "Added view ") + (Str(data,"name") ?? ""), "Scale " + Num(Dbl(data,"scale"),"0.###"), null);
            case "add_drawing_dimension": case "add_balloon":
                return ((Int(data,"created_count") ?? 0) + " created · " + (Int(data,"count") ?? 0) + (command == "add_balloon" ? " balloons" : " dimensions"), "", null);
            case "export_drawing":
                return ((Int(data,"completed_count") ?? 0) + " files exported", Str(data,"format") ?? "", null);
            case "add_drawing_note": case "add_drawing_table":
                return ((data?.Value<bool?>("created") == false ? "Reused " : "Added ") + (command == "add_drawing_note" ? "note " : "table ") + (Str(data,"name") ?? ""), command == "add_drawing_note" ? Str(data,"kind") ?? "" : Str(data,"title") ?? "", null);
            case "send_code":
                return (Preview(data?["result"]) ?? Preview(data?["stdout"]) ?? "Script finished", "C# script ran in Inventor", null);
            case "run_baked_tool":
                return (Preview(data?["result"]) ?? Preview(data?["stdout"]) ?? "Baked tool finished", "Baked tool ran in Inventor", null);
            case "batch_execute":
            {
                var n = Int(data, "executed") ?? 0;
                return ($"{n} command{Plural(n)} ran", "", null);
            }
            case "check_interference":
            {
                var pairs = Int(data, "count") ?? 0;
                return pairs == 0
                    ? ("No interference", "", null)
                    : ($"{pairs} interfering pair{Plural(pairs)}", "Total " + Num(Dbl(data, "total_volume_mm3"), "0.##") + " mm³", null);
            }
            case "get_mass_properties":
            {
                var g = Dbl(data, "mass_g");
                var mass = g >= 1000 ? Num(g / 1000, "0.###") + " kg" : Num(g, "0.###") + " g";
                return ("Mass " + mass, "Volume " + Num(Dbl(data, "volume_mm3"), "0.##") + " mm³", null);
            }
        }

        var file = Str(data, "output_path") ?? Str(data, "path");
        if (file != null) return ("Saved " + FileName(file), "", null);
        var count = Int(data, "count") ?? Int(data, "total");
        if (count != null) return ($"{count.Value} item{Plural(count.Value)}", "", null);
        var name = Str(data, "name") ?? Str(data, "feature_name");
        if (name != null) return (name, "", null);
        if (data != null)
        {
            foreach (var property in data.Properties())
                if (property.Value is JArray items)
                    return ($"{items.Count} {property.Name.Replace('_', ' ')}", "", null);
            foreach (var key in new[] { "count", "total", "affected_count", "created_count", "updated_count", "deleted_count" })
                if (Int(data, key) is { } fieldCount) return ($"{fieldCount} {key.Replace('_', ' ')}", "", null);
        }
        return (Str(data, "message") ?? ToolNameFormatter.Format(command), "", null);
    }

    /// <summary>
    /// The card only ever loads a thumbnail from a local image file: a real, non-UNC path with an image
    /// extension. The path comes from a successful capture_view result, so the handler's path policy has run.
    /// </summary>
    internal static bool IsSafeImagePath(string? path) => ToastThumbnail.PathIfImage(path) != null;

    private static (string Summary, string Detail, string? Thumb) Capture(JObject? data)
    {
        var w = Int(data, "width");
        var h = Int(data, "height");
        var size = w != null && h != null ? $" · {w.Value}×{h.Value}" : "";
        if (data?["base64"] != null) return ("Captured image" + size, "Returned inline", null);

        var path = Str(data, "path") ?? Str(data, "output_path");
        var thumb = ToastThumbnail.PathIfImage(path);
        return ("Saved " + FileName(path) + size, thumb != null ? "Click to open History" : "", thumb);
    }

    /// <summary>One short line from a result token, without serializing large arrays/objects.</summary>
    private static string? Preview(JToken? t)
    {
        switch (t)
        {
            case null:
                return null;
            case JValue { Type: JTokenType.Null }:
                return null;
            case JValue { Type: JTokenType.String } s:
                return FirstLine(Head((string?)s));
            case JValue v:
                return Convert.ToString(v.Value, Inv);
            case JArray a:
                return $"Result: {a.Count} item{Plural(a.Count)}";
            case JObject o:
                return $"Result: {o.Count} field{Plural(o.Count)}";
            default:
                return FirstLine(Head(t.ToString(Formatting.None)));
        }
    }

    private static string? Str(JObject? o, string key) => o?[key] is JValue { Type: JTokenType.String } v ? (string?)v : null;
    private static int? Int(JObject? o, string key) => o?[key] is JValue { Type: JTokenType.Integer } v ? (int?)v : null;
    private static double Dbl(JObject? o, string key)
        => o?[key] is JValue { Type: JTokenType.Float or JTokenType.Integer } v ? (double)v : 0;
    private static string Num(double d, string format) => d.ToString(format, Inv);
    private static string Plural(int n) => n == 1 ? "" : "s";

    private static string FileName(string? path)
    {
        if (path == null) return "file";
        try
        {
            var name = Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? "file" : name;
        }
        catch
        {
            return "file";
        }
    }

    private static string? Head(string? s) => s == null ? null : s.Length > PreviewChars ? s.Substring(0, PreviewChars) : s;

    private static string? FirstLine(string? s)
    {
        if (s == null) return null;
        foreach (var raw in s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length > 0) return line;
        }
        return null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
}
