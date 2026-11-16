using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Reports;

/// <summary>
/// Family contract FC-RPT-05 (rvt-mcp AgentReportReminder parity): a response of a write that changed a model carries
/// <c>_report</c> until the agent sends a report, the first in full and the rest short. The writes since the last
/// report are kept (bounded) and saved with the next report as its evidence.
/// </summary>
internal sealed class ReportTracker
{
    internal const string Text = "This job changed an Inventor model. Before your final answer, call inventor_report_task_result once: "
        + "outcome completed, failed or cancelled, a one-line summary and an optional Markdown body. Not after each tool.";
    internal const string ShortText = "Report still due: call inventor_report_task_result once before your final answer.";
    internal const string ScriptText = "If this script changed the model, call inventor_report_task_result once before your final answer. Not after each tool.";
    internal const int MaxCalls = 256;

    // Writes that do not change a model: opening, closing, saving and copying files, and exports.
    private static readonly HashSet<string> NoModelChange = new(StringComparer.Ordinal)
    {
        "open_document", "open_documents", "close_document", "close_documents", "save_document", "save_all",
        "save_checkpoint", "manage_checkpoints",
    };

    private readonly object _gate = new();
    private readonly Func<string, bool?> _isReadOnly;
    private List<JObject> _calls = new();
    private bool _truncated;
    private bool _changeReminded;
    private bool _scriptReminded;

    /// <param name="isReadOnly">The tool annotation of a wire command; null when it has no MCP tool.</param>
    internal ReportTracker(Func<string, bool?> isReadOnly) => _isReadOnly = isReadOnly;

    /// <summary>Records a successful command and returns the reminder its response carries, or null.</summary>
    internal string? Record(string command, Newtonsoft.Json.Linq.JObject? parameters, string requestId, string? documentTitle)
    {
        if (_isReadOnly(command) != false || NoModelChange.Contains(command) || command.StartsWith("export_", StringComparison.Ordinal))
            return null;
        var script = command == "send_code";
        if (script && parameters?["compile_only"] is JValue { Type: JTokenType.Boolean } c && (bool)c) return null;
        lock (_gate)
        {
            if (_calls.Count < MaxCalls)
            {
                var call = new JObject
                {
                    ["request_id"] = requestId, ["tool"] = "inventor_" + command,
                    ["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["document_title"] = documentTitle,
                };
                if (script) call["unconfirmed_change"] = true;   // the server cannot tell whether a script changed the model
                _calls.Add(call);
            }
            else _truncated = true;
            if (script)
            {
                // A script may or may not have changed the model: one conditional reminder per report interval.
                if (_changeReminded || _scriptReminded) return null;
                _scriptReminded = true;
                return ScriptText;
            }
            var text = _changeReminded ? ShortText : Text;
            _changeReminded = true;
            return text;
        }
    }

    /// <summary>The writes since the last report; clears them and the reminder state.</summary>
    internal (JArray Calls, bool Truncated) TakeCalls()
    {
        lock (_gate)
        {
            var calls = new JArray(_calls);
            var truncated = _truncated;
            _calls = new List<JObject>();
            _truncated = false;
            _changeReminded = false;
            _scriptReminded = false;
            return (calls, truncated);
        }
    }
}
