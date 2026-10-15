using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>
/// Which Inventor session this gateway talks to (rvt-mcp #19 parity). A selected session is never
/// replaced implicitly: when its process exits or its pid is reused, calls fail with TARGET_LOST until
/// the agent selects again. Without a selection, a call binds only when exactly one live target
/// matches; several matches fail with AMBIGUOUS_TARGET instead of guessing.
/// </summary>
internal sealed class TargetBinding
{
    internal readonly record struct ProcessProbe(bool Running, DateTime? StartUtc);

    private sealed record Selection(string TargetId, int ProcessId, DateTime? StartUtc, string SelectedBy);

    private readonly object _gate = new();
    private readonly string? _configSelector;
    private readonly Func<int, ProcessProbe> _probe;
    private Selection? _current;
    private Selection? _lost;

    internal TargetBinding(string? configSelector, Func<int, ProcessProbe>? probe = null)
    {
        _configSelector = string.IsNullOrWhiteSpace(configSelector) ? null : configSelector.Trim();
        _probe = probe ?? ProbeProcess;
    }

    /// <summary>Target id of the live selection, without probing or binding.</summary>
    internal string? SelectedTargetId { get { lock (_gate) return _current?.TargetId; } }

    internal string? SelectedBy { get { lock (_gate) return _current?.SelectedBy; } }

    /// <summary>The descriptor for this call; binds on first use only when the choice is unambiguous.</summary>
    internal TargetDescriptor Resolve(IReadOnlyList<TargetDescriptor> live)
    {
        lock (_gate)
        {
            if (_current is { } cur)
            {
                if (!SameProcess(cur))
                {
                    _lost = cur;
                    _current = null;
                    throw Lost(cur, live);
                }
                return Find(cur, live) ?? throw Fail(InventorErrorCodes.TARGET_UNAVAILABLE,
                    $"Selected Inventor target {cur.TargetId} is still running, but its add-in is not advertising a live endpoint. " +
                    "The selection is kept and no other Inventor will be used; retry after the add-in restarts, or call inventor_switch_target.",
                    cur.TargetId, live);
            }
            if (_lost is { } lost) throw Lost(lost, live);

            var matches = _configSelector is null ? live.ToList() : live.Where(t => Matches(_configSelector, t)).ToList();
            if (matches.Count == 0)
                throw Fail(InventorErrorCodes.NO_TARGET, _configSelector is null
                    ? "No live Inventor target. Start Inventor with the bimwright add-in loaded."
                    : $"No live Inventor target matches --target {_configSelector}. Call inventor_list_available_targets, then inventor_switch_target.",
                    null, live);
            if (matches.Count > 1)
                throw Fail(InventorErrorCodes.AMBIGUOUS_TARGET,
                    $"{matches.Count} Inventor sessions match{(_configSelector is null ? "" : " --target " + _configSelector)}; the gateway will not guess. " +
                    "Ask the user which model to use if unclear, then call inventor_switch_target with pid:<n> or id:<target_id>.",
                    null, matches);
            return Bind(matches[0], _configSelector is null ? "auto" : "config");
        }
    }

    /// <summary>Explicit selection. A failed switch leaves the current selection unchanged.</summary>
    internal TargetDescriptor Switch(string selector, IReadOnlyList<TargetDescriptor> live)
    {
        var key = (selector ?? "").Trim();
        if (key.Length == 0)
            throw new InventorGatewayException(InventorErrorCodes.INVALID_ARGUMENT, "target is required: pid:<n>, id:<target_id>, a 4-digit year, or a pipe name.");
        lock (_gate)
        {
            var matches = live.Where(t => Matches(key, t)).ToList();
            if (matches.Count == 0)
                throw Fail(InventorErrorCodes.NO_TARGET, $"No live Inventor target matches '{key}'. The current selection is unchanged.",
                    _current?.TargetId, live);
            if (matches.Count > 1)
                throw Fail(InventorErrorCodes.AMBIGUOUS_TARGET,
                    $"{matches.Count} Inventor sessions match '{key}'. Use pid:<n> or id:<target_id>. The current selection is unchanged.",
                    _current?.TargetId, matches);
            return Bind(matches[0], "switch_target");
        }
    }

    /// <summary>The selected target's live descriptor for best-effort side traffic; never binds or changes state.</summary>
    internal TargetDescriptor? Peek(IReadOnlyList<TargetDescriptor> live)
    {
        lock (_gate) return _current is { } cur ? Find(cur, live) : null;
    }

    internal static bool Matches(string selector, TargetDescriptor t)
    {
        var s = selector.Trim();
        if (s.StartsWith("pid:", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(s.Substring(4).Trim(), out var pid) && t.ProcessId == pid;
        if (s.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            return string.Equals(t.TargetId, s.Substring(3).Trim(), StringComparison.OrdinalIgnoreCase);
        if (string.Equals(t.TargetId, s, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.PipeName, s, StringComparison.OrdinalIgnoreCase)) return true;
        if (int.TryParse(s, out var n))
            return n is >= 2022 and <= 2027 ? t.InventorYear == n : t.ProcessId == n;
        return false;
    }

    internal static object Summary(TargetDescriptor t) => new
    {
        target_id = t.TargetId,
        inventor_year = t.InventorYear,
        process_id = t.ProcessId,
        document_title = t.DocumentTitle,
    };

    private TargetDescriptor Bind(TargetDescriptor t, string selectedBy)
    {
        var probe = _probe(t.ProcessId);
        if (!probe.Running)
            throw Fail(InventorErrorCodes.TARGET_UNAVAILABLE, $"Inventor target {t.TargetId} exited while it was being selected.", null, Array.Empty<TargetDescriptor>());
        _current = new Selection(t.TargetId, t.ProcessId, probe.StartUtc, selectedBy);
        _lost = null;
        return t;
    }

    // Same pid alone is not enough: Windows reuses pids, so compare the start time when both are known.
    private bool SameProcess(Selection s)
    {
        var probe = _probe(s.ProcessId);
        if (!probe.Running) return false;
        return s.StartUtc is not { } then || probe.StartUtc is not { } now || Math.Abs((then - now).TotalSeconds) < 1;
    }

    private static TargetDescriptor? Find(Selection s, IReadOnlyList<TargetDescriptor> live) =>
        live.FirstOrDefault(t => t.ProcessId == s.ProcessId && string.Equals(t.TargetId, s.TargetId, StringComparison.OrdinalIgnoreCase));

    private static InventorGatewayException Lost(Selection s, IReadOnlyList<TargetDescriptor> live) =>
        Fail(InventorErrorCodes.TARGET_LOST,
            $"Selected Inventor target {s.TargetId} has exited or been replaced. No other Inventor is used automatically; " +
            "confirm which model to use, then call inventor_switch_target.",
            s.TargetId, live);

    private static InventorGatewayException Fail(string code, string message, string? selected, IReadOnlyList<TargetDescriptor> candidates) =>
        new(code, message, new JObject
        {
            ["selected_target_id"] = selected,
            ["candidates"] = JArray.FromObject(candidates.Select(Summary)),
        });

    private static ProcessProbe ProbeProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return new ProcessProbe(false, null);
            try { return new ProcessProbe(true, p.StartTime.ToUniversalTime()); }
            catch { return new ProcessProbe(true, null); }
        }
        catch { return new ProcessProbe(false, null); }
    }
}
