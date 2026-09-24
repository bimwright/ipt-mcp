#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Front door used by the add-in. <see cref="Notify"/> runs on the transport listener thread,
/// <see cref="UpdateSnapshot"/> on the Inventor STA, and the ribbon toggle sets <see cref="Enabled"/>.
/// The toast thread starts lazily on the first toast, so disabled toasts cost nothing.
/// </summary>
internal sealed class ToastNotifier : IDisposable
{
    private readonly object _gate = new();
    private readonly ToastTheme _theme;
    private readonly ToastFeed _feed = new();
    private ToastHost? _host;
    private bool _disposed;
    private volatile bool _enabled;
    private volatile InventorUiSnapshot _ui = InventorUiSnapshot.Empty;

    public ToastNotifier(ToastSettings settings)
    {
        _enabled = settings.EnableToast;
        _theme = settings.Theme;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_gate)
            {
                _enabled = value;
                if (!value)
                {
                    _feed.Clear();
                    _host?.DismissAll();
                }
            }
        }
    }

    public string? LastPaletteDecision => CurrentHost()?.LastDecision;

    public void UpdateSnapshot(InventorUiSnapshot ui)
    {
        _ui = ui;
        CurrentHost()?.SetSnapshot(ui);
    }

    /// <summary>Never throws and never waits on Inventor. Called after the response was handed back.
    /// Returns whether the card was retained for display, so a report can tell the agent the truth.</summary>
    public bool Notify(ToastEvent e)
    {
        if (!_enabled) return false;
        try
        {
            lock (_gate)
            {
                var ui = _ui;
                // The AppVisible flag comes from the last STA snapshot and may be stale, so also read the
                // frame's live Win32 state. Minimized or modal-blocked still gets a card, held hidden.
                if (!_enabled || _disposed
                    || !ToastVisibility.ShouldCreate(ui.AppVisible, ui.MainHwnd, ToastNative.MainState(new IntPtr(ui.MainHwnd)))) return false;
                var model = ToastContentBuilder.Build(e);   // cheap, but pointless when nothing can show
                _host ??= new ToastHost(_theme, ui, _feed);
                return _feed.Publish(model); // bounded state update; never queues a per-command UI operation
            }
        }
        catch
        {
            return false;   // toasts are best effort
        }
    }

    public void Retheme() => CurrentHost()?.Retheme();

    public void Dispose()
    {
        ToastHost? host;
        lock (_gate)
        {
            _disposed = true;
            _feed.Clear();
            host = _host;
            _host = null;
        }
        host?.Shutdown();
    }

    private ToastHost? CurrentHost()
    {
        lock (_gate) return _host;
    }

}
#endif
