#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Threading;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// The dedicated toast STA thread (spike 1a, mode B) and the stack of toast windows on it.
/// Public members may be called from any thread and only post work. Private members run on the toast
/// thread. This class never touches Inventor COM objects; it reads Inventor HWNDs via ToastNative only.
/// </summary>
internal sealed class ToastHost
{
    private const int TrackIntervalMs = 250;

    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;
    private readonly ToastTheme _theme;
    private readonly ToastFeed _feed;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<ToastWindow, ToastCard> _cards = new();
    private readonly List<ToastWindow> _stack = new();   // priority first
    private DispatcherTimer? _refresh;
    private DispatcherTimer? _track;
    private ToastPalette? _stackPalette;                  // one decision per visible stack (spec theme item 4)
    private bool _rethemeDue;
    private volatile InventorUiSnapshot _ui;
    private volatile string? _lastDecision;

    public ToastHost(ToastTheme theme, InventorUiSnapshot ui, ToastFeed feed)
    {
        _theme = theme;
        _ui = ui;
        _feed = feed;
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.UnhandledException += (_, e) => e.Handled = true;   // a toast bug must never take Inventor down
                // Demand-driven pull: feed changes post here; the slow timer only bridges the
                // 500 ms snapshot throttle instead of ticking for the whole Inventor session.
                _refresh = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(ToastFeed.RefreshIntervalMs),
                };
                _refresh.Tick += (_, _) => RenderSignal();
                ready.Set();
                Dispatcher.Run();
            }
            catch
            {
                // thread ends; later posts are ignored
            }
        })
        {
            IsBackground = true,
            Name = "Bimwright.Ipt.Toast",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!ready.Wait(5000) || dispatcher == null)
            throw new InvalidOperationException("toast thread did not start");
        _dispatcher = dispatcher;
        _feed.Changed += () => Post(RenderSignal);
    }

    public string? LastDecision => _lastDecision;

    public void SetSnapshot(InventorUiSnapshot ui) => _ui = ui;

    public void Retheme() => Post(RethemeNow);

    public void DismissAll() => Post(DismissAllNow);

    /// <summary>Called from Deactivate (Inventor STA). Bounded waits: never hangs Inventor's shutdown.</summary>
    public void Shutdown()
    {
        try { _dispatcher.Invoke(DismissAllNow, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(3)); } catch { }
        try { _dispatcher.InvokeShutdown(); } catch { }
        try { _thread.Join(3000); } catch { }
    }

    private void Post(Action action)
    {
        try { _dispatcher.BeginInvoke(action); } catch { }
    }

    /// <summary>
    /// Toast thread. Render what the snapshot allows; while a change is still waiting behind the
    /// throttle keep the bridge timer running, otherwise stop it — nothing ticks while the feed is idle.
    /// </summary>
    private void RenderSignal()
    {
        RenderPending();
        if (_feed.HasPending) _refresh?.Start();
        else _refresh?.Stop();
    }

    private void RenderPending()
    {
        var snapshot = _feed.TakeSnapshot(_clock.ElapsedMilliseconds);
        if (snapshot == null) return;
        var ui = _ui;
        var main = new IntPtr(ui.MainHwnd);
        if (!ToastVisibility.ShouldCreate(ui.AppVisible, ui.MainHwnd) || Anchor(ui, ToastNative.Dpi(main)) == null)
        {
            _feed.Clear();
            DismissAllNow();
            return;
        }

        // Remove obsolete windows BEFORE creating replacements: even during fades the cap is three.
        foreach (var w in _stack.ToArray())
            if (!snapshot.Any(c => c.Id == _cards[w].Id)) w.CloseNow();

        var suppressed = !ToastVisibility.ShouldShow(ToastNative.MainState(main));
        if (snapshot.Count > 0) ApplySample(suppressed);
        var palette = _stackPalette ?? ToastPaletteChooser.Choose(_theme, null, ui.Hint).Palette;
        foreach (var card in snapshot)
        {
            var window = _stack.FirstOrDefault(w => _cards[w].Id == card.Id);
            if (window != null && _cards[window].Revision == card.Revision) continue;
            if (window?.IsClosing == true)
            {
                window.CloseNow(); // revision-aware dismissal preserves the newer producer update
                window = null;
            }
            if (window == null)
            {
                window = new ToastWindow(card.Model, palette, OnClosed);
                if (card.Model.ThumbnailPath != null)
                    window.SetThumbnail(ToastThumbnail.TryLoadBytes(card.Model.ThumbnailPath));
                _cards[window] = card;
                _stack.Add(window);
            }
            else
            {
                window.UpdateModel(card.Model, palette);
                _cards[window] = card;
            }
        }
        var order = snapshot.Select(c => c.Id).ToList();
        _stack.Sort((a, b) => order.IndexOf(_cards[a].Id).CompareTo(order.IndexOf(_cards[b].Id)));
        ApplySuppression(suppressed);
        if (!suppressed)
        {
            Reflow();
            // A card replaced mid-fade emptied the stack and reset the palette; the new window reused the
            // palette read before the loop. Resample beside it now that it has a position.
            if (_stackPalette == null) ApplySample(false);
        }
        if (_stack.Count > 0) StartTracking();
    }

    private void ApplySuppression(bool suppressed)
    {
        foreach (var w in _stack.ToArray())
            w.SetSuppressed(suppressed);   // idempotent per window
    }

    private void Decide(PxRect sampleRect)
    {
        Rgb? sample = null;
        long ms = 0;
        if (_theme == ToastTheme.Auto)
        {
            var clock = Stopwatch.StartNew();
            sample = ScreenSampler.Average(sampleRect);
            ms = clock.ElapsedMilliseconds;
        }
        var decision = ToastPaletteChooser.Choose(_theme, sample, _ui.Hint);
        _stackPalette = decision.Palette;
        _lastDecision = _theme == ToastTheme.Auto ? $"{decision.Reason} (sample {ms} ms)" : decision.Reason;
    }

    private static PxPoint? Anchor(InventorUiSnapshot ui, uint dpi)
    {
        var main = ToastNative.Rect(new IntPtr(ui.MainHwnd));
        if (main == null) return null;
        var view = ui.ViewHwnd != 0 ? ToastNative.VisibleRect(new IntPtr(ui.ViewHwnd)) : null;
        return ToastLayout.Anchor(view, main.Value, dpi);
    }

    private void Reflow()
    {
        var ui = _ui;
        var dpi = ToastNative.Dpi(new IntPtr(ui.MainHwnd));
        var anchor = Anchor(ui, dpi);
        if (anchor == null) return;
        var shown = _stack.FindAll(w => w.IsShown);
        var positions = ToastLayout.Stack(anchor.Value, shown.ConvertAll(w => w.HeightPx), dpi);
        for (var i = 0; i < shown.Count; i++)
            shown[i].MoveTo(positions[i]);
    }

    private void StartTracking()
    {
        if (_track == null)
        {
            _track = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(TrackIntervalMs) };
            _track.Tick += (_, _) => Track();
        }
        if (!_track.IsEnabled) _track.Start();
    }

    /// <summary>
    /// Owner emulation (D3). Toasts are unowned, so we follow the Inventor frame ourselves: hide or
    /// resume with it, and move when it or the view moves.
    /// </summary>
    private void Track()
    {
        if (_stack.Count == 0)
        {
            _track?.Stop();
            return;
        }
        var suppressed = !ToastVisibility.ShouldShow(ToastNative.MainState(new IntPtr(_ui.MainHwnd)));
        ApplySample(suppressed);
        ApplySuppression(suppressed);
        if (!suppressed) Reflow();
    }

    private void OnClosed(ToastWindow window)
    {
        if (_cards.TryGetValue(window, out var card))
        {
            _cards.Remove(window);
            _feed.Dismiss(card.Id, card.Revision);
        }
        if (!_stack.Remove(window)) return;
        if (_stack.Count == 0)
        {
            _stackPalette = null;   // next stack decides afresh
            _rethemeDue = false;
            _track?.Stop();
        }
        else
        {
            Reflow();
        }
    }

    /// <summary>
    /// After an option change or a view switch. Repaint without recreating the cards. While a modal
    /// covers the frame the sample waits, because the pixels under the anchor belong to the dialog.
    /// </summary>
    private void RethemeNow()
    {
        if (_stack.Count == 0)
        {
            _stackPalette = null;
            _rethemeDue = false;
            return;
        }
        // Application Options is modal: the frame is disabled, so the sample would see the dialog.
        _rethemeDue = true;
        var suppressed = !ToastVisibility.ShouldShow(ToastNative.MainState(new IntPtr(_ui.MainHwnd)));
        ApplySample(suppressed);
    }

    /// <summary>
    /// Commits a backdrop sample only when <see cref="ToastSample.Target"/> says the frame is usable.
    /// Hidden cards are not covering the canvas, so a deferred retheme samples the anchor.
    /// </summary>
    private void ApplySample(bool suppressed)
    {
        var onScreen = false;
        foreach (var w in _stack)
            if (w.IsOnScreen) { onScreen = true; break; }

        var target = ToastSample.Target(!suppressed, _stackPalette != null, _rethemeDue, onScreen);
        if (target == ToastSampleTarget.None) return;

        var ui = _ui;
        var dpi = ToastNative.Dpi(new IntPtr(ui.MainHwnd));
        var anchor = Anchor(ui, dpi);
        if (anchor == null) return;

        PxRect rect;
        if (target == ToastSampleTarget.Beside)
        {
            var rects = new List<PxRect>();
            foreach (var w in _stack)
                if (w.IsOnScreen && ToastNative.Rect(w.Hwnd) is { } r) rects.Add(r);
            var bounds = ToastLayout.Union(rects);
            rect = bounds == null
                ? ToastLayout.SampleRect(anchor.Value, dpi)
                : ToastLayout.StripRightOf(bounds.Value, dpi);
        }
        else
        {
            rect = ToastLayout.SampleRect(anchor.Value, dpi);
        }

        Decide(rect);
        _rethemeDue = false;
        foreach (var w in _stack)
            w.ApplyPalette(_stackPalette!);
    }

    private void DismissAllNow()
    {
        var all = _stack.ToArray();
        _stack.Clear();
        foreach (var w in all)
            w.CloseNow();
        _stackPalette = null;
        _rethemeDue = false;
        _track?.Stop();
    }
}
#endif
