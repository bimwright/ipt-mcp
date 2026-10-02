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
            ["toast.category.query"] = "MCP · Query",
            ["toast.category.modified"] = "MCP · Modified",
            ["toast.category.script"] = "MCP · Script",
            ["toast.category.export"] = "MCP · Export",
            ["toast.category.snapshot"] = "MCP · Snapshot",
            ["toast.category.connected"] = "MCP · Connected",
            ["toast.category.failed"] = "MCP · Failed",
            ["toast.activity.header"] = "MCP · Activity",
            ["toast.activity.latest"] = "Latest",
            ["toast.activity.counts.tooltip"] = "Success / failed / images",
            ["toast.activity.recent"] = "Activity · {count:n} calls",
            ["toast.generic.fields"] = "Fields: {count:n}",
            ["toast.survey.partial"] = "Survey: coverage incomplete",
            ["toast.survey.complete"] = "Survey: supported checks complete",
            ["toast.survey.review"] = "Review scope and blind spots before changes",
            ["toast.failed.default"] = "Tool call failed",
            ["toast.capture.saved"] = "Saved {fileName}",
            ["toast.capture.savedPx"] = "Saved {fileName} · {pixelSize}px {format}",
            ["toast.capture.viewId"] = "View id {viewId}",
            ["toast.capture.clickToOpen"] = "Click to open",
            ["toast.capture.imageFallback"] = "image",
            ["toast.currentView.active"] = "Active view",
            ["toast.currentView.scale"] = "Scale 1:{scale}",
            ["toast.rooms.none"] = "No rooms in model",
            ["toast.rooms.found"] = "Rooms found: {count:n}",
            ["toast.rooms.complete"] = "Room query complete",
            ["toast.rooms.placedStats"] = "Placed {placed:n}, unplaced {unplaced:n}",
            ["toast.filters.allLevelsPhases"] = "All levels · all phases",
            ["toast.filters.allLevels"] = "All levels",
            ["toast.filters.level"] = "Level: {level}",
            ["toast.filters.allPhases"] = "All phases",
            ["toast.filters.phase"] = "Phase: {phase}",
            ["toast.filters.status"] = "Status: {status}",
            ["toast.sheets.none"] = "No sheets in project",
            ["toast.sheets.found"] = "Drawing sheets: {count:n}",
            ["toast.sheets.ready"] = "Sheet list ready",
            ["toast.sheets.first"] = "First: {number} {name}",
            ["toast.sheets.more"] = "(+{count:n} more)",
            ["toast.sheets.fallback"] = "Sheets",
            ["toast.worksets.notWorkshared"] = "Model is not workshared",
            ["toast.worksets.none"] = "No worksets to list",
            ["toast.worksets.found"] = "Worksets: {count:n}",
            ["toast.worksets.complete"] = "Workset query complete",
            ["toast.worksets.active"] = "Active: {name}",
            ["toast.filter.matched"] = "Matched {count:n} {category}",
            ["toast.filter.elementsFallback"] = "elements",
            ["toast.filter.complete"] = "Element filter complete",
            ["toast.selected.count"] = "Elements selected: {count:n}",
            ["toast.selected.done"] = "Selection read",
            ["toast.sendCode.finished"] = "Script finished",
            ["toast.sendCode.detail"] = "Custom C# executed in Revit",
            ["toast.generic.completed"] = "Completed successfully",
            ["toast.generic.results"] = "Results: {count:n}",
            ["toast.generic.items"] = "Items: {count:n}",
            ["toast.generic.rows"] = "Rows: {count:n}",
            ["toast.generic.fileFallback"] = "file",
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
