#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly List<ToastWindow> _stack = new();   // newest first
    private DispatcherTimer? _track;
    private ToastPalette? _stackPalette;                  // one decision per visible stack (spec theme item 4)
    private volatile InventorUiSnapshot _ui;
    private volatile string? _lastDecision;

    public ToastHost(ToastTheme theme, InventorUiSnapshot ui)
    {
        _theme = theme;
        _ui = ui;
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.UnhandledException += (_, e) => e.Handled = true;   // a toast bug must never take Inventor down
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
    }

    public string? LastDecision => _lastDecision;

    public void SetSnapshot(InventorUiSnapshot ui) => _ui = ui;

    public void Show(ToastModel model) => Post(() => ShowNow(model));

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

    private void ShowNow(ToastModel model)
    {
        var ui = _ui;
        if (!ToastVisibility.ShouldCreate(ui.AppVisible, ui.MainHwnd)) return;
        var main = new IntPtr(ui.MainHwnd);
        var dpi = ToastNative.Dpi(main);
        var anchor = Anchor(ui, dpi);
        if (anchor == null) return;

        if (_stackPalette == null)
            Decide(ToastLayout.SampleRect(anchor.Value, dpi));

        while (_stack.Count >= ToastLayout.MaxToasts)
        {
            var oldest = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            oldest.CloseNow();
        }

        var window = new ToastWindow(model, _stackPalette!, OnClosed);
        if (model.ThumbnailPath != null)
            window.SetThumbnail(ToastThumbnail.TryLoadBytes(model.ThumbnailPath));
        _stack.Insert(0, window);

        // Created while a modal dialog is up: stays unshown and appears when the dialog closes (D1).
        // Applied to the whole stack so older hidden toasts come back together with the new one.
        ApplySuppression(!ToastVisibility.ShouldShow(ToastNative.MainState(main)));
        Reflow();
        StartTracking();
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
        ApplySuppression(suppressed);
        if (!suppressed) Reflow();
    }

    private void OnClosed(ToastWindow window)
    {
        if (!_stack.Remove(window)) return;
        if (_stack.Count == 0)
        {
            _stackPalette = null;   // next stack decides afresh
            _track?.Stop();
        }
        else
        {
            Reflow();
        }
    }

    /// <summary>
    /// After an option change or a view switch: re-sample a strip beside the stack (the toasts cover their
    /// own backdrop) and repaint the toasts without recreating them.
    /// </summary>
    private void RethemeNow()
    {
        var shown = _stack.FindAll(w => w.IsShown);
        if (shown.Count == 0)
        {
            if (_stack.Count == 0) _stackPalette = null;
            return;
        }
        var rects = new List<PxRect>();
        foreach (var w in shown)
            if (ToastNative.Rect(w.Hwnd) is { } r) rects.Add(r);
        var bounds = ToastLayout.Union(rects);
        if (bounds == null) return;
        Decide(ToastLayout.StripRightOf(bounds.Value, ToastNative.Dpi(new IntPtr(_ui.MainHwnd))));
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
        _track?.Stop();
    }
}
#endif
