#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Dedicated background STA, independent of Inventor's message loop. Runs the same card manager as
/// rvt-mcp with ONE unowned window. No Inventor COM, no Application.Current, no cross-thread owner.
/// Only read-only Win32 on host HWNDs. Inventor has no Idling event, so a 100 ms tracker on this
/// thread restores parked cards and keeps the card anchored to the Inventor frame.
/// </summary>
internal sealed class ToastHost
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;
    private readonly ActivityAggregator _activity;
    private readonly string _identity;
    private readonly Action _openHistory;
    private readonly Func<InventorUiSnapshot, bool> _usable;
    private readonly Func<bool>? _motion;
    private readonly Func<bool>? _isPaused;
    private readonly Func<bool, bool>? _setPaused;
    private McpToastManager? _manager;
    private DispatcherTimer? _track;
    private volatile bool _showBranding;
    private volatile string? _agentName;
    private volatile ToastPositionOptions _position = new();
    private Action<ToastPositionOptions>? _positionChanged;
    private volatile InventorUiSnapshot _ui;
    private int _stopped;

    public ToastHost(InventorUiSnapshot ui, ActivityAggregator activity, string identity,
        Action openHistory, bool showBranding = false, Func<InventorUiSnapshot, bool>? usable = null,
        Func<bool>? motion = null, string? agentName = null,
        Func<bool>? isPaused = null, Func<bool, bool>? setPaused = null)
    {
        _ui = ui;
        _activity = activity;
        _identity = identity;
        _openHistory = openHistory;
        _showBranding = showBranding;
        _usable = usable ?? FrameUsable;
        _motion = motion;
        _agentName = string.IsNullOrWhiteSpace(agentName) ? null : agentName;
        _isPaused = isPaused;
        _setPaused = setPaused;
        Dispatcher? dispatcher = null;
        // A timed-out startup may still finish later; do not dispose its signal on the caller thread.
        var ready = new System.Threading.Tasks.TaskCompletionSource<bool>();
        _thread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.UnhandledException += (_, e) => e.Handled = true;
                _manager = new McpToastManager(dispatcher, _activity,
                    isFrameUsable: () => _usable(_ui),
                    onClick: OnClick,
                    showBranding: () => _showBranding,
                    instanceIdentity: IdentityTitle,
                    motionEnabled: _motion,
                    positionOptions: () => _position,
                    positionChanged: value => { _position = value; _positionChanged?.Invoke(value); },
                    isPaused: _isPaused,
                    setPaused: _setPaused);
                _track = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                    { Interval = TimeSpan.FromMilliseconds(100) };
                _track.Tick += (_, _) => Track();
                ready.TrySetResult(true);
                if (Volatile.Read(ref _stopped) == 0) Dispatcher.Run();
            }
            catch { ready.TrySetResult(false); }
            finally { _track?.Stop(); CloseWindow(); }
        }) { IsBackground = true, Name = "Bimwright.Ipt.Toast" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!ready.Task.Wait(5000) || !ready.Task.Result || dispatcher == null)
        {
            Interlocked.Exchange(ref _stopped, 1);
            try { dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send); } catch { }
            throw new InvalidOperationException("toast thread did not start");
        }
        _dispatcher = dispatcher;
    }

    public void SetSnapshot(InventorUiSnapshot ui) => _ui = ui;
    public void SetAgentName(string? agent)
    {
        _agentName = string.IsNullOrWhiteSpace(agent) ? null : agent;
        Post(() => _manager?.RefreshIdentity());
    }
    private string IdentityTitle()
    {
        var agent = _agentName;
        if (agent == null) return _identity;
        return (string.IsNullOrWhiteSpace(_identity) ? BrandAssets.ProductName : _identity) + " · " + agent;
    }
    public void SetPosition(ToastPositionOptions options, Action<ToastPositionOptions>? changed = null)
    {
        _position = options;
        if (changed != null) _positionChanged = changed;
        Post(() => _manager?.ApplyPosition());
    }
    public void RequestRender() => Post(Render);
    public void SetShowBranding(bool show)
    {
        _showBranding = show;
        Post(() => _manager?.ApplyShowBranding());
    }

    internal static bool FrameUsable(InventorUiSnapshot ui) =>
        ToastVisibility.ShouldShow(ToastNative.MainState(new IntPtr(ui.MainHwnd)));

    public void Shutdown()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        try
        {
            _dispatcher.Invoke(() => { _track?.Stop(); CloseWindow(); },
                DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
        }
        catch { }
        try { _manager?.Dispose(); } catch { }
        // InvokeShutdown synchronously from another STA could wait forever; shutdown request is asynchronous.
        try { _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); } catch { }
        if (Thread.CurrentThread != _thread) _thread.Join(2000);
    }

    private void Post(Action action)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        try { _dispatcher.BeginInvoke(action); } catch { }
    }

    private void Render()
    {
        var manager = _manager;
        if (manager == null) return;
        // Re-evaluate at render time too: a posted result can race minimize/modal changes.
        var usable = _usable(_ui);
        manager.SetOwnerHandle(new IntPtr(_ui.MainHwnd));
        _activity.Tick(usable);
        manager.Render();
        manager.Reposition();
        UpdateTimer();
    }

    private void Track()
    {
        var manager = _manager;
        if (manager == null) return;
        var ui = _ui;
        manager.SetOwnerHandle(new IntPtr(ui.MainHwnd));
        var live = ToastNative.MainState(new IntPtr(ui.MainHwnd));
        if (!live.Exists || !live.Visible || !ui.AppVisible)
        {
            // No native frame remains. Do not poll forever or leave a topmost orphan behind.
            _activity.Reset();
            manager.Render();
            UpdateTimer();
            return;
        }
        // The manager's own timer ticks an open card; this restores one parked while the frame was unusable.
        if (_activity.FlushIfUsable(_usable(ui))) manager.Render();
        manager.Reposition();
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        // Pending state (no HWND) still needs a Win32 tracker. Inventor has no Idling event.
        if (_activity.HasUnrenderedResults || _manager?.HasWindow == true) _track?.Start();
        else _track?.Stop();
    }

    private void OnClick(long id)
    {
        try { _openHistory(); } catch { }
    }

    private void CloseWindow()
    {
        try { _manager?.DismissAllImmediate(); } catch { }
    }
}
#endif
