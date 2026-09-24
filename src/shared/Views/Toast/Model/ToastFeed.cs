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

    /// <summary>True while a change is still waiting for the next throttled snapshot.</summary>
    public bool HasPending { get { lock (_gate) return _dirty; } }

    /// <summary>Raised when the feed first becomes dirty; the UI renders on its own thread.</summary>
    public event Action? Changed;

    /// <summary>False when a full stack of strictly higher-priority cards refuses a routine arrival.</summary>
    public bool Publish(ToastModel model)
    {
        var notify = false;
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
                // An explicit task report exists to be seen, so it may evict even the oldest error;
                // routine arrivals keep the drop rule (never push an error out with reads).
                if (victim.Value.Priority > priority && model.TaskId == null) return false;
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
            notify = !_dirty;
            _dirty = true;
        }
        // Notify outside the lock: subscribers marshal to their own thread and must not re-enter here.
        if (notify) Changed?.Invoke();
        return true;
    }

    /// <summary>An old window finishing its fade must not discard a newer producer update.</summary>
    public void Dismiss(long id, long revision)
    {
        var notify = false;
        lock (_gate)
        {
            var match = _cards.FirstOrDefault(x => x.Value.Card.Id == id && x.Value.Card.Revision == revision);
            if (match.Key == null) return;
            _cards.Remove(match.Key);
            notify = !_dirty;
            _dirty = true;
        }
        if (notify) Changed?.Invoke();
    }

    public void Clear()
    {
        var notify = false;
        lock (_gate)
        {
            if (_cards.Count == 0 && !_dirty) return;   // nothing to wake the UI for
            _cards.Clear();
            notify = !_dirty;
            _dirty = true;
        }
        if (notify) Changed?.Invoke();
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
