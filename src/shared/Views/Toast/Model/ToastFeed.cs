using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>A retained card, not a queued notification. Id is stable across in-place updates.</summary>
public sealed record ToastCard(long Id, long Revision, ToastModel Model, long Count);

/// <summary>
/// Thread-safe producer/UI boundary. Producers replace at most three retained cards; the UI pulls
/// snapshots at most twice a second, never one dispatcher operation per tool. Counts describe the
/// retained activity window, NOT an inferred agent job. No Inventor, WPF or wall clock dependencies.
/// </summary>
public sealed class ToastFeed
{
    public const int RefreshIntervalMs = 500;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _cards = new(StringComparer.Ordinal);
    private long _lastSnapshotMs = -RefreshIntervalMs;
    private long _nextId;
    private bool _dirty;

    private sealed record Entry(int Priority, ToastCard Card);

    public void Publish(ToastModel model)
    {
        lock (_gate)
        {
            var priority = !model.Success ? 3 : model.TaskId != null ? 2
                : model.ThumbnailPath != null || model.Command == "capture_view" || model.Command.StartsWith("export_", StringComparison.Ordinal) ? 1 : 0;
            var key = model.TaskId != null ? "task\0" + model.TaskId : priority == 3 ? "error\0" + model.Command + "\0" + model.Summary + "\0" + model.Detail
                : priority == 1 ? "result\0" + model.Command : "activity";
            _cards.TryGetValue(key, out var previous);
            if (previous == null && _cards.Count >= ToastLayout.MaxToasts)
            {
                var victim = _cards.OrderBy(x => x.Value.Priority).ThenBy(x => x.Value.Card.Id).First();
                if (victim.Value.Priority > priority) return; // never push an error out with routine reads
                _cards.Remove(victim.Key);
            }

            var count = (previous?.Card.Count ?? 0) + 1;
            var display = model;
            if (priority == 0 && count > 1)
            {
                display = model with
                {
                    Title = "Agent activity", Category = "MCP · Activity",
                    Summary = count + " successful operations",
                    Detail = "Latest: " + model.Title, DurationMs = 0,
                    Kind = previous?.Card.Model.Kind == ToolActivityKind.Write ? ToolActivityKind.Write : model.Kind,
                };
            }
            if (priority == 3 && count > 1 && model.TaskId == null)
                display = model with { Detail = model.Detail + " · " + count + " occurrences" };
            var card = new ToastCard(previous?.Card.Id ?? ++_nextId, count, display, count);
            _cards[key] = new Entry(priority, card);
            _dirty = true;
        }
    }

    /// <summary>An old window finishing its fade must not discard a newer producer update.</summary>
    public void Dismiss(long id, long revision)
    {
        lock (_gate)
        {
            var match = _cards.FirstOrDefault(x => x.Value.Card.Id == id && x.Value.Card.Revision == revision);
            if (match.Key == null) return;
            _cards.Remove(match.Key);
            _dirty = true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _cards.Clear();
            _dirty = true;
        }
    }

    /// <summary>Null means no repaint; an empty snapshot means remove all cards.</summary>
    public IReadOnlyList<ToastCard>? TakeSnapshot(long nowMs)
    {
        lock (_gate)
        {
            if (!_dirty || nowMs - _lastSnapshotMs < RefreshIntervalMs) return null;
            _lastSnapshotMs = nowMs;
            _dirty = false;
            return _cards.Values.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Card.Id)
                .Select(x => x.Card).ToArray();
        }
    }
}
