using System;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Visible time left on one toast. The WPF timer restarts its whole interval on each start, so hover
/// and hide account for the running slice here instead.
/// </summary>
public sealed class ToastCountdown
{
    private long _runningSinceMs = -1;

    public ToastCountdown(int lifetimeMs)
    {
        RemainingMs = lifetimeMs < 0 ? 0 : lifetimeMs;
    }

    public int RemainingMs { get; private set; }

    /// <summary>Starts a slice at <paramref name="nowMs"/>. A second call does not move the mark.</summary>
    public void Start(long nowMs)
    {
        if (_runningSinceMs >= 0) return;
        _runningSinceMs = nowMs;
    }

    /// <summary>Subtracts the slice that was running. A call while already stopped does nothing.</summary>
    public void Pause(long nowMs)
    {
        if (_runningSinceMs < 0) return;
        var elapsed = nowMs - _runningSinceMs;
        if (elapsed < 0) elapsed = 0;
        if (elapsed >= RemainingMs) RemainingMs = 0;
        else RemainingMs -= (int)elapsed;
        _runningSinceMs = -1;
    }
}
