namespace Bimwright.Ipt.Shared.Infrastructure;

using System.Threading;

/// <summary>
/// Point-in-time queue statistics for the STA dispatcher (spec F2-b). API-agnostic — the
/// counters are plain <see cref="Interlocked"/> ints owned by <c>InventorStaDispatcher</c>;
/// the <c>health</c> command reads them through <see cref="InventorCommandContext.StaQueue"/>
/// so a caller can tell a jammed STA thread apart from a dead one.
/// </summary>
public sealed class StaQueueStats
{
    private int _outstanding;   // posted work items not yet run to completion (queued + executing)
    private int _executing;     // work items currently running on the STA thread

    /// <summary>Posted but not yet completed — includes the command currently executing.</summary>
    public int PendingCommands => Volatile.Read(ref _outstanding);

    /// <summary>Currently executing on the STA thread (0 or 1 in practice).</summary>
    public int ExecutingCommands => Volatile.Read(ref _executing);

    public void OnQueued() => Interlocked.Increment(ref _outstanding);

    public void OnStarted() => Interlocked.Increment(ref _executing);

    public void OnCompleted()
    {
        Interlocked.Decrement(ref _executing);
        Interlocked.Decrement(ref _outstanding);
    }

    /// <summary>The work item never reached the STA thread (post failed) — roll back the count.</summary>
    public void OnAbandoned() => Interlocked.Decrement(ref _outstanding);
}
