namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Marshals work onto Inventor's main STA thread. Inventor has no <c>ExternalEvent</c> (unlike Revit),
/// so we use a hidden message-only WinForms <see cref="Control"/> created on the STA thread during
/// <c>Activate</c>; its forced handle lets <see cref="Control.BeginInvoke(Delegate)"/> queue work onto
/// the UI thread. The transport listener thread only ever touches this control via
/// <see cref="InvokeAsync{T}"/> (i.e. via <c>BeginInvoke</c>), never directly.
///
/// Every posted item moves <see cref="Stats"/> through queued → executing → completed, so the
/// <c>health</c> command can report <c>sta_busy</c> / <c>pending_commands</c> even while a
/// long-running <c>send_code</c> occupies the STA thread (spec F2-b).
/// </summary>
public sealed class InventorStaDispatcher : IDisposable
{
    private readonly Control _marshal;       // created on the STA/main thread
    private readonly StaQueueStats _stats = new();

    public InventorStaDispatcher()
    {
        // MUST be constructed on Inventor's main STA thread (during Activate).
        _marshal = new Control();
        var _ = _marshal.Handle;             // force handle creation so BeginInvoke works
    }

    /// <summary>Live queue counters; safe to read from any thread.</summary>
    public StaQueueStats Stats => _stats;

    /// <summary>Posts <paramref name="work"/> onto the STA thread; the caller applies its own wait/timeout.</summary>
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_marshal.IsHandleCreated) { tcs.TrySetException(new InvalidOperationException("STA dispatcher not ready")); return tcs.Task; }
        _stats.OnQueued();
        try
        {
            _marshal.BeginInvoke((Action)(() =>
            {
                _stats.OnStarted();
                try { tcs.TrySetResult(work()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { _stats.OnCompleted(); }
            }));
        }
        catch
        {
            _stats.OnAbandoned();
            throw;
        }
        return tcs.Task;
    }

    public void Dispose()
    {
        try { if (_marshal.IsHandleCreated) _marshal.Invoke((Action)(() => _marshal.Dispose())); else _marshal.Dispose(); } catch { }
    }
}
