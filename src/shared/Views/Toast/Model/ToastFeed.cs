using System;
using System.Diagnostics;

namespace Bimwright.Ipt.Shared.Views.Toast;

public enum ToastCardKind { Activity, Status, TaskResult }
public enum ToastPhase { Hidden, Visible, Closing }

/// <summary>Immutable rendering state of the single card. Counts belong to this card, not an inferred job.</summary>
public sealed record ToastCard(long Id, ToastCardKind Kind, string Title, string Body,
    int Succeeded, int Failed, int Captures, bool LatestSuccess, bool HasFailure, string? Outcome = null);
public sealed record ToastRender(ToastPhase Phase, ToastCard? Card);

/// <summary>
/// Single-card activity state, matching the RVT idle/hover/counter contract without host dependencies.
/// Mutators return whether the caller must post a render. A burst claims only one pending render.
/// Pending cards retain their data without consuming their lifetime while the frame is unusable.
/// </summary>
public sealed class ToastFeed
{
    public const int DefaultIdleSeconds = 20;
    private enum Phase { None, Pending, Visible, Closing }
    private readonly object _gate = new();
    private readonly Func<TimeSpan> _now;
    private readonly Func<int> _idleSeconds;
    private Phase _phase;
    private ToastCard? _card;
    private long _nextId;
    private bool _renderPending;
    private bool _hovering;
    private int _statusSeconds;
    private TimeSpan _deadline;

    public ToastFeed(Func<int>? idleSeconds = null, Func<TimeSpan>? now = null)
    {
        var clock = Stopwatch.StartNew();
        _now = now ?? (() => clock.Elapsed);
        _idleSeconds = idleSeconds ?? (() => DefaultIdleSeconds);
    }

    /// <summary>The host must keep tracking even when Pending has no WPF window.</summary>
    public bool HasWork { get { lock (_gate) return _phase != Phase.None; } }

    public bool Record(ToastModel model, bool frameUsable)
    {
        lock (_gate)
        {
            // Explicit agent reports replace the slot; they are not tool counts or inferred completion.
            // Unlike a connection status they must not be silently refused by an open activity card.
            if (model.TaskId != null)
            {
                Start(ToastCardKind.TaskResult, frameUsable);
                _statusSeconds = 8;
                _card = _card! with { Title = model.Title, Body = "Agent reported · " + model.Summary + " · " + model.TaskId,
                    LatestSuccess = model.Success, HasFailure = !model.Success, Outcome = model.Outcome };
            }
            else
            {
                if (_card?.Kind != ToastCardKind.Activity || (_phase != Phase.Visible && _phase != Phase.Pending))
                    Start(ToastCardKind.Activity, frameUsable);
                _card = _card! with
                {
                    Title = model.Title,
                    Body = model.Summary + (model.Detail.Length == 0 ? "" : " · " + model.Detail),
                    Succeeded = _card.Succeeded + (model.Success ? 1 : 0),
                    Failed = _card.Failed + (model.Success ? 0 : 1),
                    Captures = _card.Captures + (model.Success && (model.Command == "capture_view" || model.ThumbnailPath != null) ? 1 : 0),
                    LatestSuccess = model.Success,
                    HasFailure = _card.HasFailure || !model.Success,
                };
            }
            _phase = frameUsable ? Phase.Visible : Phase.Pending;
            if (_phase == Phase.Visible) Rearm();
            else _hovering = false;
            return RequestRender();
        }
    }

    /// <summary>Connection status never replaces activity or an explicit task report, or changes counts.</summary>
    public bool ShowStatus(string title, string body, int seconds, bool frameUsable)
    {
        lock (_gate)
        {
            if (_card?.Kind != ToastCardKind.Status && (_phase == Phase.Visible || _phase == Phase.Pending))
                return false;
            Start(ToastCardKind.Status, frameUsable);
            _card = _card! with { Title = title, Body = body };
            _statusSeconds = Math.Max(1, seconds);
            if (frameUsable) Rearm();
            return RequestRender();
        }
    }

    /// <summary>Reset is atomic with notification toggle handling at the notifier boundary.</summary>
    public bool Reset()
    {
        lock (_gate)
        {
            _phase = Phase.None;
            _card = null;
            _hovering = false;
            return RequestRender();
        }
    }

    /// <summary>Independent toast-thread tick: no Inventor Idling/COM needed to restore pending work.</summary>
    public bool Tick(bool frameUsable)
    {
        lock (_gate)
        {
            if (_phase == Phase.Pending && frameUsable)
            {
                _phase = Phase.Visible;
                _hovering = false;
                Rearm();
                return RequestRender();
            }
            if (_phase != Phase.Visible) return false;
            if (!frameUsable)
            {
                _phase = Phase.Pending;
                _hovering = false;
                return RequestRender();
            }
            if (!Expired()) return false;
            _phase = Phase.Closing;
            return RequestRender();
        }
    }

    public bool PointerEntered(long id)
    {
        lock (_gate)
        {
            if (!IsLive(id)) return false;
            if (Expired())
            {
                _phase = Phase.Closing;
                return RequestRender();
            }
            _hovering = true;
            return false;
        }
    }

    public void PointerLeft(long id)
    {
        lock (_gate)
        {
            if (!IsLive(id) || !_hovering) return;
            _hovering = false;
            Rearm();
        }
    }

    public bool Dismiss(long id)
    {
        lock (_gate)
        {
            if (!IsLive(id)) return false;
            _phase = Phase.Closing;
            return RequestRender();
        }
    }

    public void CardClosed(long id)
    {
        lock (_gate)
        {
            if (_card?.Id != id || (_phase != Phase.Visible && _phase != Phase.Closing)) return;
            _phase = Phase.None;
            _card = null;
        }
    }

    public ToastRender TakeRender()
    {
        lock (_gate)
        {
            _renderPending = false;
            if (_phase == Phase.Visible && Expired()) _phase = Phase.Closing;
            return _phase switch
            {
                Phase.Visible => new ToastRender(ToastPhase.Visible, _card),
                Phase.Closing => new ToastRender(ToastPhase.Closing, _card),
                _ => new ToastRender(ToastPhase.Hidden, null),
            };
        }
    }

    private void Start(ToastCardKind kind, bool frameUsable)
    {
        _card = new ToastCard(++_nextId, kind, "", "", 0, 0, 0, true, false);
        _phase = frameUsable ? Phase.Visible : Phase.Pending;
        _hovering = false;
    }
    private bool IsLive(long id) => _card?.Id == id && _phase == Phase.Visible;
    private bool Expired() => !_hovering && _now() >= _deadline;
    private void Rearm()
    {
        if (_hovering) return;
        var seconds = _card?.Kind == ToastCardKind.Activity ? _idleSeconds() : _statusSeconds;
        _deadline = _now() + TimeSpan.FromSeconds(seconds > 0 ? seconds : DefaultIdleSeconds);
    }
    private bool RequestRender()
    {
        if (_renderPending) return false;
        _renderPending = true;
        return true;
    }
}
