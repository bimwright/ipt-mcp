#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Dedicated background STA, independent of Inventor's message loop. Reconciles ONE unowned window.
/// No Inventor COM, no Application.Current, no cross-thread owner. Only read-only Win32 on host HWNDs.
/// </summary>
internal sealed class ToastHost
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;
    private readonly ToastTheme _theme;
    private readonly ToastFeed _feed;
    private readonly string _identity;
    private readonly Action _openHistory;
    private readonly Func<InventorUiSnapshot, bool> _usable;
    private readonly Func<bool>? _motion;
    private readonly Func<PxRect, Rgb?> _sample;
    private DispatcherTimer? _track;
    private ToastWindow? _window;
    private ToastPalette? _palette;
    private bool _rethemeDue;
    private volatile bool _showBranding;
    private volatile InventorUiSnapshot _ui;
    private volatile string? _lastDecision;
    private int _stopped;

    public ToastHost(ToastTheme theme, InventorUiSnapshot ui, ToastFeed feed, string identity,
        Action openHistory, bool showBranding = false, Func<InventorUiSnapshot, bool>? usable = null,
        Func<bool>? motion = null, Func<PxRect, Rgb?>? sample = null)
    {
        _theme = theme;
        _ui = ui;
        _feed = feed;
        _identity = identity;
        _openHistory = openHistory;
        _showBranding = showBranding;
        _usable = usable ?? FrameUsable;
        _motion = motion;
        _sample = sample ?? ScreenSampler.Average;
        Dispatcher? dispatcher = null;
        // A timed-out startup may still finish later; do not dispose its signal on the caller thread.
        var ready = new System.Threading.Tasks.TaskCompletionSource<bool>();
        _thread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.UnhandledException += (_, e) => e.Handled = true;
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

    public string? LastDecision => _lastDecision;
    public void SetSnapshot(InventorUiSnapshot ui) => _ui = ui;
    public void RequestRender() => Post(Render);
    public void Retheme() => Post(() => { _rethemeDue = true; Track(); });
    public void SetShowBranding(bool show)
    {
        _showBranding = show;
        Post(() => _window?.SetShowBranding(show));
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
        // Re-evaluate at render time too: a posted result can race minimize/modal changes.
        var ui = _ui;
        var usable = _usable(ui);
        _feed.Tick(usable);
        var render = _feed.TakeRender();
        var card = render.Card;
        if (render.Phase == ToastPhase.Hidden || card == null)
        {
            CloseWindow();
            if (!_feed.HasWork) { _palette = null; _rethemeDue = false; }
        }
        else if (render.Phase == ToastPhase.Closing)
        {
            // Dismissed cards finish immediately under a modal/minimized frame, never park for revival.
            if (usable && _window?.CardId == card.Id) _window.BeginClose();
            else { CloseWindow(); _feed.CardClosed(card.Id); }
        }
        else
        {
            var anchor = Anchor(ui);
            if (anchor == null)
            {
                // No native frame remains. Do not poll forever or leave a topmost orphan behind.
                _feed.Reset();
                _feed.TakeRender();
                CloseWindow();
            }
            else
            {
                if (_window?.CardId != card.Id)
                {
                    CloseWindow(); // old fading HWND must be gone before creating another
                    // The card is not on screen yet, so a needed sample reads the anchor.
                    DecidePalette(anchor.Value, onScreen: false);
                    _palette ??= ToastPaletteChooser.Choose(_theme, null, ui.Hint).Palette;
                    _window = new ToastWindow(card, _palette!, _identity, OnClosed,
                        id => { if (_feed.Dismiss(id)) Render(); }, OnClick,
                        id => { if (_feed.PointerEntered(id)) Render(); }, _feed.PointerLeft, motion: _motion);
                    _window.SetShowBranding(_showBranding);
                    _window.Appear(anchor.Value);
                }
                else _window.Update(card);
            }
        }
        UpdateTimer();
    }

    private void Track()
    {
        var ui = _ui;
        var live = ToastNative.MainState(new IntPtr(ui.MainHwnd));
        if (!live.Exists || !live.Visible || !ui.AppVisible)
        {
            _feed.Reset();
            Render();
            return;
        }
        var usable = _usable(ui);
        if (_feed.Tick(usable) || (!usable && _window != null)) Render();
        if (_window != null && !_window.IsClosing && Anchor(ui) is { } anchor)
        {
            _window.MoveTo(anchor);
            if (_rethemeDue) DecidePalette(anchor, onScreen: true);
        }
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        // Pending state (no HWND) still needs a Win32 tracker. Inventor has no Idling event.
        if (_feed.HasWork || _window != null) _track?.Start();
        else _track?.Stop();
    }

    private PxPoint? Anchor(InventorUiSnapshot ui)
    {
        var main = new IntPtr(ui.MainHwnd);
        var rect = ToastNative.Rect(main);
        if (rect == null) return null;
        var view = ToastNative.VisibleRect(new IntPtr(ui.ViewHwnd));
        return ToastLayout.Anchor(view, rect.Value, ToastNative.Dpi(main));
    }

    /// <summary>
    /// <see cref="ToastSample.Target"/> is the only sampling policy. A committed palette is kept
    /// across park/restore, and a card that is already painted is never its own backdrop.
    /// </summary>
    private void DecidePalette(PxPoint anchor, bool onScreen)
    {
        var target = ToastSample.Target(_usable(_ui), _palette != null, _rethemeDue, onScreen);
        if (target == ToastSampleTarget.None) return;
        var dpi = ToastNative.Dpi(new IntPtr(_ui.MainHwnd));
        PxRect rect;
        if (target == ToastSampleTarget.Beside)
        {
            // No measured card: leave the retheme pending rather than sampling under the toast.
            if (_window == null || ToastNative.Rect(_window.Hwnd) is not { } bounds) return;
            rect = ToastLayout.StripRightOf(bounds, dpi);
        }
        else
            rect = ToastLayout.SampleRect(anchor, dpi);
        var clock = Stopwatch.StartNew();
        var sample = _theme == ToastTheme.Auto ? _sample(rect) : null;
        var decision = ToastPaletteChooser.Choose(_theme, sample, _ui.Hint);
        _palette = decision.Palette;
        _lastDecision = decision.Reason + (_theme == ToastTheme.Auto ? $" (sample {clock.ElapsedMilliseconds} ms)" : "");
        _rethemeDue = false;
        _window?.ApplyPalette(_palette);
    }

    private void OnClick(long id)
    {
        if (_window?.CardId != id) return;
        try { _openHistory(); } catch { }
        if (_feed.Dismiss(id)) Render();
    }
    private void OnClosed(ToastWindow window)
    {
        if (!ReferenceEquals(_window, window)) return;
        _window = null;
        _feed.CardClosed(window.CardId);
        _palette = null;
        UpdateTimer();
    }
    private void CloseWindow()
    {
        var window = _window;
        _window = null; // fence callbacks: parking/replacing must not discard newer state
        try { window?.CloseNow(); } catch { }
    }
}
#endif
