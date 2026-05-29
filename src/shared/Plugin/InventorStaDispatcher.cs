namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

/// <summary>
/// Marshals work onto Inventor's main STA thread. Inventor has no <c>ExternalEvent</c> (unlike Revit),
/// so we use a hidden message-only WinForms <see cref="Control"/> created on the STA thread during
/// <c>Activate</c>; its forced handle lets <see cref="Control.BeginInvoke(Delegate)"/> queue work onto
/// the UI thread. The transport listener thread only ever touches this control via
/// <see cref="InvokeAsync{T}"/> (i.e. via <c>BeginInvoke</c>), never directly.
/// </summary>
public sealed class InventorStaDispatcher : IDisposable
{
    private readonly Control _marshal;       // created on the STA/main thread

    public InventorStaDispatcher()
    {
        // MUST be constructed on Inventor's main STA thread (during Activate).
        _marshal = new Control();
        var _ = _marshal.Handle;             // force handle creation so BeginInvoke works
    }

    public Task<T> InvokeAsync<T>(Func<T> work, int timeoutMs)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_marshal.IsHandleCreated) { tcs.TrySetException(new InvalidOperationException("STA dispatcher not ready")); return tcs.Task; }
        _marshal.BeginInvoke((Action)(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }));
        // caller applies its own timeout around the returned task
        return tcs.Task;
    }

    public void Dispose()
    {
        try { if (_marshal.IsHandleCreated) _marshal.Invoke((Action)(() => _marshal.Dispose())); else _marshal.Dispose(); } catch { }
    }
}
