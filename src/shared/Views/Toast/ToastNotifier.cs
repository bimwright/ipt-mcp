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
            _enabled = value;
            if (!value) CurrentHost()?.DismissAll();
        }
    }

    public string? LastPaletteDecision => CurrentHost()?.LastDecision;

    public void UpdateSnapshot(InventorUiSnapshot ui)
    {
        _ui = ui;
        CurrentHost()?.SetSnapshot(ui);
    }

    /// <summary>Never throws and never waits on Inventor. Called after the response was handed back.</summary>
    public void Notify(ToastEvent e)
    {
        if (!_enabled) return;
        try
        {
            var ui = _ui;
            if (!ToastVisibility.ShouldCreate(ui.AppVisible, ui.MainHwnd)) return;   // invisible Inventor: no window
            var model = ToastContentBuilder.Build(e);
            GetOrCreateHost()?.Show(model);
        }
        catch
        {
            // toasts are best effort
        }
    }

    public void Retheme() => CurrentHost()?.Retheme();

    public void Dispose()
    {
        ToastHost? host;
        lock (_gate)
        {
            _disposed = true;
            host = _host;
            _host = null;
        }
        host?.Shutdown();
    }

    private ToastHost? CurrentHost()
    {
        lock (_gate) return _host;
    }

    private ToastHost? GetOrCreateHost()
    {
        lock (_gate)
        {
            if (_disposed) return null;
            return _host ??= new ToastHost(_theme, _ui);
        }
    }
}
#endif
