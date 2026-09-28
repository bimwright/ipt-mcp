#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Listener-side front door. All host data is captured as a DTO on Inventor's STA; notifications
/// and task reports never wait for the Inventor command queue. No localization dependency.
/// </summary>
internal sealed class ToastNotifier : IDisposable
{
    private readonly object _gate = new();
    private readonly ToastTheme _theme;
    private readonly ToastFeed _feed = new();
    private readonly string _identity;
    private readonly Action _openHistory;
    private ToastHost? _host;
    private bool _disposed;
    private volatile bool _enabled;
    private volatile bool _showBranding;
    private volatile InventorUiSnapshot _ui = InventorUiSnapshot.Empty;
    private string? _pendingConnection;

    public ToastNotifier(ToastSettings settings, string identity, Action openHistory)
    {
        _enabled = settings.EnableToast;
        _theme = settings.Theme;
        _identity = identity;
        _openHistory = openHistory;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_gate)
            {
                if (_disposed) return;
                _enabled = value;
                _pendingConnection = null;
                if (_feed.Reset()) _host?.RequestRender();
            }
        }
    }
    public bool ShowBranding
    {
        get => _showBranding;
        set { lock (_gate) { _showBranding = value; _host?.SetShowBranding(value); } }
    }
    public bool HasVisibleSnapshot => _ui.AppVisible && _ui.MainHwnd != 0;
    public string? LastPaletteDecision { get { lock (_gate) return _host?.LastDecision; } }

    public void UpdateSnapshot(InventorUiSnapshot ui)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _ui = ui;
            _host?.SetSnapshot(ui);
            if (_pendingConnection != null && CanCreate())
            {
                var info = _pendingConnection;
                _pendingConnection = null;
                NotifyConnection(info);
            }
        }
    }

    /// <summary>Separate status, never an operation count and never covers open activity.</summary>
    public bool NotifyConnection(string connectionInfo)
    {
        try
        {
            lock (_gate)
            {
                if (!_enabled || _disposed) return false;
                if (!CanCreate()) { _pendingConnection = connectionInfo; return false; }
                var host = EnsureHost();
                var post = _feed.ShowStatus("Agent connected", "ipt-mcp is ready · " + connectionInfo, 6, ToastHost.FrameUsable(_ui));
                if (post) host.RequestRender();
                return post;
            }
        }
        catch { return false; }
    }

    /// <summary>One on/off confirmation is allowed even when notifications have just been disabled.</summary>
    public void NotifyToggle(bool persisted)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || !CanCreate()) return;
                var host = EnsureHost();
                // Reset and status can share one coalesced render, but startup may not have had a host
                // when Reset claimed it. Always post here so the status cannot get stranded.
                _feed.ShowStatus(_enabled ? "Toast notifications enabled" : "Toast notifications disabled",
                    (_enabled ? "New activity will appear here." : "New activity is hidden until enabled.")
                    + (persisted ? "" : " Preference could not be saved; this session still uses the new state."),
                    3, ToastHost.FrameUsable(_ui));
                host.RequestRender();
            }
        }
        catch { }
    }

    /// <summary>
    /// True means retained for display (including while minimized/modal/background), not already painted.
    /// This is the wire contract of report_task_result.toast_shown. Off/invisible/disposed returns false.
    /// </summary>
    public bool Notify(ToastEvent e)
    {
        if (!_enabled || e.Command == "health") return false;
        try
        {
            lock (_gate)
            {
                if (!_enabled || _disposed || !CanCreate()) return false;
                var model = ToastContentBuilder.Build(e); // includes soft-failure normalization
                var host = EnsureHost();
                if (_feed.Record(model, ToastHost.FrameUsable(_ui))) host.RequestRender();
                return true;
            }
        }
        catch { return false; }
    }

    public void Retheme() { lock (_gate) _host?.Retheme(); }
    private bool CanCreate() => ToastVisibility.ShouldCreate(_ui.AppVisible, _ui.MainHwnd,
        ToastNative.MainState(new IntPtr(_ui.MainHwnd)));
    private ToastHost EnsureHost()
    {
        if (_host != null) return _host;
        _host = new ToastHost(_theme, _ui, _feed, _identity, _openHistory, _showBranding);
        _host.RequestRender(); // drains a Reset that claimed the render slot before the host existed
        return _host;
    }

    public void Dispose()
    {
        ToastHost? host;
        lock (_gate)
        {
            _disposed = true;
            _pendingConnection = null;
            _feed.Reset();
            host = _host;
            _host = null;
        }
        host?.Shutdown();
    }
}
#endif
