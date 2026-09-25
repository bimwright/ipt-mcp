#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// One toast card. Built in code: the add-in can load in an Inventor AssemblyLoadContext where XAML
/// resources and pack URIs don't resolve. Created, shown and closed on the toast thread only.
/// </summary>
internal sealed class ToastWindow : Window
{
    private readonly Action<ToastWindow> _closed;
    private readonly Border _card;
    private readonly Border _stripe;
    private readonly TextBlock _icon;
    private readonly TextBlock _title;
    internal readonly TextBlock _category;
    internal readonly TextBlock _summary;
    internal readonly TextBlock _detail;
    internal readonly TextBlock _duration;
    internal readonly Border _closeHost;
    private readonly TextBlock _closeGlyph;
    private Brush _closeHover = Brushes.Transparent;
    private readonly TextBlock _brand;
    internal readonly Run _brandBim;
    internal readonly Run _brandWright;
    private readonly TextBlock _brandShine;
    internal readonly Run _shineBim;
    internal readonly Run _shineWright;
    internal readonly TranslateTransform _brandSweep = new(BrandMotion.SweepFrom, 0);
    internal readonly TranslateTransform _shineSweep = new(BrandMotion.SweepFrom, 0);
    private AnimationClock? _brandClock;
    private AnimationClock? _shineClock;
    private readonly Image _thumb;
    private readonly DispatcherTimer _life;
    private ToastCountdown _count;
    private readonly Stopwatch _clock = new();
    private PxPoint? _pos;
    private bool _hidden;
    private bool _closing;
    private bool _done;
    private bool _lifeRunning;

    public ToastModel Model { get; private set; }
    public bool IsClosing => _closing || _done;
    public IntPtr Hwnd { get; private set; }
    public bool IsShown { get; private set; }
    /// <summary>Painted right now. A hidden card is not covering the backdrop the sampler reads.</summary>
    public bool IsOnScreen => IsShown && !_hidden;
    public int HeightPx => ToastNative.Rect(Hwnd)?.Height ?? 0;

    public ToastWindow(ToastModel model, ToastPalette palette, Action<ToastWindow> closed)
    {
        Model = model;
        _closed = closed;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;   // spike 1a: mandatory, together with WS_EX_NOACTIVATE below
        SizeToContent = SizeToContent.Height;
        Width = ToastLayout.CardWidthDip;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        FontFamily = new FontFamily("Segoe UI");

        _icon = new TextBlock
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
            Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Top,
        };
        _title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _category = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(24, 2, 0, 0) };
        _duration = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        _summary = new TextBlock
        {
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 4, 0, 0),
            MaxHeight = 64, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _detail = new TextBlock
        {
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 2, 0, 0),
            MaxHeight = 40, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _thumb = new Image { MaxHeight = 120, Margin = new Thickness(24, 6, 0, 0), Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        _brandBim = new Run(BrandAssets.WordmarkLeft);
        _brandWright = new Run(BrandAssets.WordmarkRight);
        _brand = new TextBlock
        {
            // Logo casing and colours. Brightness lives in the OpacityMask: it starts dimmed,
            // then a lit front wipes left→right once after the reader's eye has had time to
            // reach the toast (~1.3 s, WipeBrand). The front carries a full-alpha crest so the
            // eye sees a wave pass; behind it the wordmark settles at BrandMotion.SettleOpacity.
            FontSize = BrandMotion.FontSizeDip, FontWeight = FontWeights.SemiBold,
            ToolTip = BrandAssets.ProductTag,
            Inlines = { _brandBim, _brandWright },
        };
        var brandMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
            RelativeTransform = _brandSweep,
        };
        foreach (var (offset, alpha) in BrandMotion.BrandStops)
            brandMask.GradientStops.Add(new GradientStop(Dim(alpha), offset));
        _brand.OpacityMask = brandMask;
        _shineBim = new Run(BrandAssets.WordmarkLeft);
        _shineWright = new Run(BrandAssets.WordmarkRight);
        _brandShine = new TextBlock
        {
            // The wordmark again in lighter tints, masked to a narrow band that sweeps with the
            // wipe — the wave passes inside the letterforms instead of an object sliding under.
            FontSize = BrandMotion.FontSizeDip, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false,
            Inlines = { _shineBim, _shineWright },
        };
        var shineMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
            RelativeTransform = _shineSweep,
        };
        // Band peak sits on the brand front's crest so the glint and the wipe arrive together.
        foreach (var (offset, alpha) in BrandMotion.ShineStops)
            shineMask.GradientStops.Add(new GradientStop(Dim(alpha), offset));
        _brandShine.OpacityMask = shineMask;
        var brandCell = new Grid { VerticalAlignment = VerticalAlignment.Center };
        brandCell.Children.Add(_brand);
        brandCell.Children.Add(_brandShine);

        _closeGlyph = new TextBlock
        {
            Text = "\uE711",   // ChromeClose — escaped, not literal (see ToastGlyph)
            FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _closeHost = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Close", Child = _closeGlyph,
        };
        AutomationProperties.SetName(_closeHost, "Close");
        _closeHost.MouseEnter += (_, _) => _closeHost.Background = _closeHover;
        _closeHost.MouseLeave += (_, _) => _closeHost.Background = Brushes.Transparent;
        _closeHost.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;   // only dismiss — the card's click-to-open must not see this
            BeginClose();
        };

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_icon, Dock.Left);
        DockPanel.SetDock(_closeHost, Dock.Right);
        header.Children.Add(_icon);
        header.Children.Add(_closeHost);
        header.Children.Add(_title);

        var footer = new DockPanel { Margin = new Thickness(24, 6, 0, 0) };
        DockPanel.SetDock(brandCell, Dock.Right);
        footer.Children.Add(brandCell);
        footer.Children.Add(_duration);

        var body = new StackPanel { Margin = new Thickness(10, 10, 12, 10) };
        body.Children.Add(header);
        body.Children.Add(_category);
        body.Children.Add(_summary);
        body.Children.Add(_detail);
        body.Children.Add(_thumb);
        body.Children.Add(footer);

        _stripe = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(5, 0, 0, 0), Child = body };
        _card = new Border
        {
            Margin = new Thickness(8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = _stripe,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black },
        };
        Content = _card;
        ApplyContent(model);   // before ApplyPalette: the accent depends on Model.Success/Kind
        ApplyPalette(palette);

        // The timer restarts its whole interval on Start, so remaining visible time lives in _count.
        _count = new ToastCountdown(model.LifetimeMs);
        _clock.Start();
        _life = new DispatcherTimer();
        _life.Tick += (_, _) => BeginClose();
        MouseEnter += (_, _) =>
        {
            PauseLife();
            WipeBrand(BrandMotion.HoverDelayMs);   // the eye is already there — replay quickly
        };
        MouseLeave += (_, _) => ResumeLife();
        MouseLeftButtonUp += (_, e) =>
        {
            // × already marked its click handled — never let it reach the card's open path.
            if (ReferenceEquals(e.OriginalSource, _closeHost) || ReferenceEquals(e.OriginalSource, _closeGlyph)) return;
            CardClickCount++;
            if (Model.ThumbnailPath != null) OpenImage(Model.ThumbnailPath);
            BeginClose();
        };

        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            ToastNative.MakeNoActivate(Hwnd);
        };
    }

    /// <summary>Text and icon for the current model — shared by the constructor and UpdateModel.</summary>
    private void ApplyContent(ToastModel model)
    {
        Model = model;
        _icon.Text = ToastGlyph.For(model.Icon);
        _title.Text = model.Title;
        _category.Text = model.Category;
        _category.Visibility = model.Category.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _summary.Text = model.Summary;
        _detail.Text = model.Detail;
        _detail.Visibility = model.Detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _duration.Text = model.DurationMs > 0 ? model.DurationMs + " ms" : "";
    }

    public void ApplyPalette(ToastPalette p)
    {
        _card.Background = Brush(p.Background);
        _card.BorderBrush = Brush(p.Outline);
        var accent = Brush(!Model.Success ? p.AccentError : Model.Kind == ToolActivityKind.Write ? p.AccentWrite : p.AccentRead);
        _stripe.BorderBrush = accent;
        _icon.Foreground = accent;
        var white = new Rgb(255, 255, 255);
        _shineBim.Foreground = Brush(ToastPaletteChooser.Blend(p.BrandBim, white, BrandMotion.ShineBlendWeight));
        _shineWright.Foreground = Brush(ToastPaletteChooser.Blend(p.BrandWright, white, BrandMotion.ShineBlendWeight));
        _title.Foreground = Brush(p.Title);
        _summary.Foreground = Brush(p.Title);
        var body = Brush(p.Body);
        _category.Foreground = body;
        _detail.Foreground = body;
        _duration.Foreground = body;
        _closeGlyph.Foreground = body;
        _closeHover = Brush(ToastPaletteChooser.Blend(p.Body, p.Background, 0.08));
        _brandBim.Foreground = Brush(p.BrandBim);
        _brandWright.Foreground = Brush(p.BrandWright);
    }

    /// <summary>Refresh a retained card without recreating its HWND or replaying its entrance fade.</summary>
    public void UpdateModel(ToastModel model, ToastPalette palette)
    {
        if (IsClosing) return;
        PauseLife();
        ApplyContent(model);
        _thumb.Source = null;
        _thumb.Visibility = Visibility.Collapsed;
        if (model.ThumbnailPath != null) SetThumbnail(ToastThumbnail.TryLoadBytes(model.ThumbnailPath));
        ApplyPalette(palette);
        _count = new ToastCountdown(model.LifetimeMs);
        UpdateLayout(); // height changes must be visible to the stack's physical-pixel reflow
        if (!IsMouseOver) ResumeLife();
    }

    public void SetThumbnail(byte[]? bytes)
    {
        if (bytes == null) return;
        try
        {
            var img = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = 300;
                img.StreamSource = ms;
                img.EndInit();
            }
            img.Freeze();
            _thumb.Source = img;
            _thumb.Visibility = Visibility.Visible;
        }
        catch
        {
            // not a decodable image: text-only toast
        }
    }

    /// <summary>First show: off-screen and transparent; the host moves it into the stack right after.</summary>
    public void Appear()
    {
        if (IsShown || _done) return;
        Show();
        IsShown = true;
        if (MotionEnabled)
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        else
            Opacity = 1;
        WipeBrand();
        if (!IsMouseOver) ResumeLife();
    }

    /// <summary>
    /// Hidden while Inventor is minimized or a modal dialog is open; the lifetime pauses meanwhile.
    /// Idempotent: the host calls it for every toast on every tracking tick.
    /// </summary>
    public void SetSuppressed(bool suppressed)
    {
        if (_done) return;
        if (!IsShown)
        {
            if (!suppressed) Appear();   // created while suppressed: first show now
            return;
        }
        if (suppressed == _hidden) return;
        _hidden = suppressed;
        if (suppressed)
        {
            PauseLife();
            PauseBrand();   // a hidden sweep finishes unseen — freeze it, resume on restore
            Hide();
        }
        else
        {
            Show();   // ShowActivated=false: shown without activation
            ResumeBrand();
            if (!IsMouseOver) ResumeLife();
        }
    }

    /// <summary>Brand reveal: a lit front wipes left→right once while a narrow band of lighter
    /// letters sweeps through the wordmark in sync, then the wordmark stays lit. The pass starts
    /// ~1.3 s after the card appears — the delay for a reader's eye to land on a fresh toast
    /// (delayMs = EntranceDelayMs). Replayed quickly on hover (delayMs = HoverDelayMs).
    /// Re-applying replaces the pending clock, so repeated hover never queues extra passes.
    /// With Windows' "animate controls and elements" off, the wordmark settles directly —
    /// same end state, no motion.</summary>
    private void WipeBrand(int delayMs = BrandMotion.EntranceDelayMs)
    {
        if (IsClosing || _hidden) return;
        if (!MotionEnabled)
        {
            _brandSweep.X = BrandMotion.SweepTo;
            _shineSweep.X = BrandMotion.SweepTo;
            return;
        }
        BrandWipeCount++;
        LastWipeDelayMs = delayMs;
        var dur = TimeSpan.FromMilliseconds(BrandMotion.SweepMs);
        var start = TimeSpan.FromMilliseconds(delayMs);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        var wipe = new DoubleAnimation(BrandMotion.SweepFrom, BrandMotion.SweepTo, dur) { BeginTime = start, EasingFunction = ease };
        _brandClock = wipe.CreateClock();
        _shineClock = wipe.CreateClock();
        _brandSweep.ApplyAnimationClock(TranslateTransform.XProperty, _brandClock);
        _shineSweep.ApplyAnimationClock(TranslateTransform.XProperty, _shineClock);   // same timeline, two clocks
    }

    /// <summary>Freeze the sweep while the card is hidden — a wipe that runs unseen is wasted.</summary>
    private void PauseBrand()
    {
        try { _brandClock?.Controller.Pause(); } catch { }
        try { _shineClock?.Controller.Pause(); } catch { }
    }

    private void ResumeBrand()
    {
        try { _brandClock?.Controller.Resume(); } catch { }
        try { _shineClock?.Controller.Resume(); } catch { }
    }

    /// <summary>Windows' "animate controls and elements" toggle — off means settle, not sweep.</summary>
    private static bool MotionEnabled => MotionOverrideForTests ?? SystemParameters.ClientAreaAnimation;
    internal static bool? MotionOverrideForTests;   // WPF-test seam: the OS setting can't flip per-test

    /// <summary>WPF-test hook: scheduled wipes and the delay of the last one. UpdateModel,
    /// retheme and reflow must not bump these — only Appear and hover replay may.</summary>
    internal int BrandWipeCount { get; private set; }
    internal int LastWipeDelayMs { get; private set; }

    /// <summary>WPF-test hook: card-level clicks that reached the open/dismiss path.
    /// A × click must leave this at zero — it dismisses without opening the thumbnail.</summary>
    internal int CardClickCount { get; private set; }

    private static Color Dim(double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), 0, 0, 0);

    /// <summary>Freeze the visible-time slice. Idempotent while the timer is already stopped.</summary>
    private void PauseLife()
    {
        if (!_lifeRunning) return;
        _life.Stop();
        _lifeRunning = false;
        _count.Pause(_clock.ElapsedMilliseconds);
    }

    /// <summary>Continue with whatever visible time is left. A full interval restart would add 3–9 s.</summary>
    private void ResumeLife()
    {
        if (_lifeRunning || _closing || _done || _hidden || !IsShown) return;
        if (_count.RemainingMs <= 0)
        {
            BeginClose();
            return;
        }
        _count.Start(_clock.ElapsedMilliseconds);
        _life.Interval = TimeSpan.FromMilliseconds(_count.RemainingMs);
        _lifeRunning = true;
        _life.Start();
    }

    public void MoveTo(PxPoint p)
    {
        if (Hwnd == IntPtr.Zero || _pos == p) return;
        _pos = p;
        ToastNative.MoveNoActivate(Hwnd, p.X, p.Y);
    }

    public void BeginClose()
    {
        if (_closing || _done) return;
        _closing = true;
        PauseLife();
        if (!IsShown)
        {
            CloseNow();
            return;
        }
        if (!MotionEnabled)
        {
            CloseNow();
            return;
        }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => CloseNow();
        BeginAnimation(OpacityProperty, fade);
    }

    public void CloseNow()
    {
        if (_done) return;
        _done = true;
        _closing = true;
        PauseLife();
        try { Close(); } catch { }
        _closed(this);
    }

    private static void OpenImage(string path)
    {
        var safe = ToastThumbnail.PathIfImage(path);
        if (safe == null) return;
        try { Process.Start(new ProcessStartInfo(safe) { UseShellExecute = true }); } catch { }
    }

    private static SolidColorBrush Brush(Rgb c)
    {
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
#endif
