#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Localization;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Listener-side front door. All host data is captured as a DTO on Inventor's STA; notifications
/// and task reports never wait for the Inventor command queue.
/// </summary>
internal sealed class ToastNotifier : IDisposable
{
    private const int ConnectionSeconds = 6;
    private const int ToggleSeconds = 3;

    private readonly object _gate = new();
    private readonly ActivityAggregator _activity;
    private volatile int _idleSeconds;
    private readonly string _identity;
    private readonly Action _openHistory;
    private readonly string? _configPath;
    private volatile string? _agentName;
    private ToastHost? _host;
    private bool _disposed;
    private volatile bool _enabled;
    private volatile bool _showBranding;
    private ToastPositionOptions _position = new();
    public Func<ToastPositionOptions, bool>? SavePosition { get; set; }
    public ToastPositionOptions Position
    {
        get => _position;
        set { lock (_gate) { _position = value; _host?.SetPosition(value); } }
    }

    public bool SetPosition(ToastPositionOptions value)
    {
        Position = value;
        return SavePosition?.Invoke(value) ?? false;
    }
    private volatile InventorUiSnapshot _ui = InventorUiSnapshot.Empty;
    private string? _pendingConnection;

    public ToastNotifier(bool enabled, string identity, Action openHistory,
        int idleSeconds = ToastConfigStore.DefaultIdleSeconds, string? configPath = null)
    {
        _idleSeconds = ToastConfigStore.NormalizeIdleSeconds(idleSeconds);
        _activity = new ActivityAggregator(() => _idleSeconds);
        _enabled = enabled;
        _identity = identity;
        _openHistory = openHistory;
        _configPath = configPath;
    }

    public int IdleSeconds => _idleSeconds;
    public bool IsPaused => ToastSnooze.IsActive(_configPath);
    private bool IsActiveGate => _enabled && !IsPaused;

    /// <summary>
    /// Agent of the MCP call about to run, from the gateway's clientInfo.name ("claude-code" -> "Claude").
    /// A gateway that sends no name clears it, so the title never shows a stale agent.
    /// </summary>
    public void SetAgent(string? clientName)
    {
        var agent = AgentDisplayName.For(clientName);
        _agentName = agent;
        _host?.SetAgentName(agent);
    }

    /// <summary>Status "Pause for today": saved for every Inventor on this machine; the open card closes now.</summary>
    public bool PauseForToday()
    {
        var saved = ToastSnooze.PauseForToday(_configPath);
        lock (_gate)
        {
            if (_disposed) return saved;
            _pendingConnection = null;
            if (_activity.Reset()) _host?.RequestRender();
        }
        return saved;
    }

    /// <summary>
    /// The expanded card's Pause / Resume: saved for every Inventor on this machine. The open card stays, so a
    /// mis-click can be undone with Resume; while paused, new results do not reach it.
    /// </summary>
    private bool SetPausedFromCard(bool paused)
    {
        var saved = paused ? ToastSnooze.PauseForToday(_configPath) : ToastSnooze.Resume(_configPath);
        if (!saved)
            System.Diagnostics.Trace.WriteLine(
                $"[ipt-mcp] Toast {(paused ? "pause" : "resume")} could not be saved to '{_configPath}'.");
        return ToastSnooze.IsActive(_configPath);
    }

    /// <summary>Ends a pause early (Status checkbox, or ribbon toggle while paused).</summary>
    public bool ResumeFromPause()
    {
        var saved = ToastSnooze.Resume(_configPath);
        NotifyToggle(saved);
        return saved;
    }

    // The card reads the setting when a result or pointer leave rearms its deadline.
    public void SetIdleSeconds(int seconds) => _idleSeconds = ToastConfigStore.NormalizeIdleSeconds(seconds);

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
                if (_activity.Reset()) _host?.RequestRender();
            }
        }
    }
    public bool ShowBranding
    {
        get => _showBranding;
        set { lock (_gate) { _showBranding = value; _host?.SetShowBranding(value); } }
    }
    public bool HasVisibleSnapshot => _ui.AppVisible && _ui.MainHwnd != 0;

    public void UpdateSnapshot(InventorUiSnapshot ui)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _ui = ui;
            _host?.SetSnapshot(ui);
            if (_pendingConnection != null && CanShowStatus())
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
                if (!IsActiveGate || _disposed) return false;
                if (!CanShowStatus()) { _pendingConnection = connectionInfo; return false; }
                var host = EnsureHost();
                var summary = L.T("toast.connected.summary") + " · " + connectionInfo;
                var post = _activity.ShowConnectionStatus(L.T("toast.connected.title"), summary, ConnectionSeconds);
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
                var key = _enabled ? "toast.status.enabled" : "toast.status.disabled";
                var body = L.T(key + ".summary") + (persisted ? "" : " · " + L.T("toast.status.saveFailed"));
                _activity.ShowStatus(L.T(key), body, ToggleSeconds);
                // Reset and status can share one coalesced render, but startup may not have had a host
                // when Reset claimed it. Always post here so the status cannot get stranded.
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
        if (!IsActiveGate || e.Command == "health") return false;
        try
        {
            lock (_gate)
            {
                if (!IsActiveGate || _disposed || !CanCreate()) return false;
                var model = ToastContentBuilder.Build(e); // includes soft-failure normalization
                var host = EnsureHost();
                if (_activity.Record(model, ToastHost.FrameUsable(_ui))) host.RequestRender();
                return true;
            }
        }
        catch { return false; }
    }

    private bool CanCreate() => ToastVisibility.ShouldCreate(_ui.AppVisible, _ui.MainHwnd,
        ToastNative.MainState(new IntPtr(_ui.MainHwnd)));

    /// <summary>A status card is not parked, so it waits for a frame that is on screen and usable.</summary>
    private bool CanShowStatus() => CanCreate() && ToastHost.FrameUsable(_ui);

    private ToastHost EnsureHost()
    {
        if (_host != null) return _host;
        _host = new ToastHost(_ui, _activity, _identity, _openHistory, _showBranding,
            agentName: _agentName,
            isPaused: _configPath == null ? null : () => ToastSnooze.IsActive(_configPath),
            setPaused: _configPath == null ? null : SetPausedFromCard);
        _host.SetPosition(_position, value => SetPosition(value));
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
            _activity.Reset();
            host = _host;
            _host = null;
        }
        host?.Shutdown();
    }
}
#endif
