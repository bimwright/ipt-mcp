#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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
    private readonly TextBlock _category;
    private readonly TextBlock _summary;
    private readonly TextBlock _detail;
    private readonly TextBlock _duration;
    private readonly TextBlock _brand;
    private readonly Run _brandBim;
    private readonly Run _brandWright;
    private readonly Image _thumb;
    private readonly DispatcherTimer _life;
    private readonly ToastCountdown _count;
    private readonly Stopwatch _clock = new();
    private PxPoint? _pos;
    private bool _hidden;
    private bool _closing;
    private bool _done;
    private bool _lifeRunning;

    public ToastModel Model { get; }
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
            FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14,
            Margin = new Thickness(0, 1, 8, 0), Text = model.Success ? "" : "",
        };
        _title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, Text = model.Title, TextTrimming = TextTrimming.CharacterEllipsis };
        _category = new TextBlock { FontSize = 11, Margin = new Thickness(8, 2, 0, 0), Text = model.Category };
        _duration = new TextBlock { FontSize = 11, Margin = new Thickness(8, 2, 0, 0), Text = model.DurationMs > 0 ? model.DurationMs + " ms" : "" };
        _summary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Text = model.Summary };
        _detail = new TextBlock
        {
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Text = model.Detail,
            Visibility = model.Detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible,
        };
        _thumb = new Image { MaxHeight = 120, Margin = new Thickness(0, 6, 0, 0), Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        _brandBim = new Run("BIM");
        _brandWright = new Run("wright");
        _brand = new TextBlock
        {
            // Logo casing and colours; hidden until hovered, then fades in.
            FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "bimwright ipt-mcp",
            Inlines = { _brandBim, _brandWright },
        };

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_icon, Dock.Left);
        DockPanel.SetDock(_duration, Dock.Right);
        header.Children.Add(_icon);
        header.Children.Add(_duration);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(_title);
        titleRow.Children.Add(_category);
        header.Children.Add(titleRow);

        var body = new StackPanel { Margin = new Thickness(10, 10, 12, 10) };
        body.Children.Add(header);
        body.Children.Add(_summary);
        body.Children.Add(_detail);
        body.Children.Add(_thumb);
        body.Children.Add(_brand);

        _stripe = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(5, 0, 0, 0), Child = body };
        _card = new Border
        {
            Margin = new Thickness(8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = _stripe,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black },
        };
        Content = _card;
        ApplyPalette(palette);

        // The timer restarts its whole interval on Start, so remaining visible time lives in _count.
        _count = new ToastCountdown(model.LifetimeMs);
        _clock.Start();
        _life = new DispatcherTimer();
        _life.Tick += (_, _) => BeginClose();
        MouseEnter += (_, _) => { PauseLife(); FadeBrand(1); };
        MouseLeave += (_, _) => { ResumeLife(); FadeBrand(0); };
        MouseLeftButtonUp += (_, _) =>
        {
            if (Model.ThumbnailPath != null) OpenImage(Model.ThumbnailPath);
            BeginClose();
        };

        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            ToastNative.MakeNoActivate(Hwnd);
        };
    }

    public void ApplyPalette(ToastPalette p)
    {
        _card.Background = Brush(p.Background);
        _card.BorderBrush = Brush(p.Outline);
        var accent = Brush(!Model.Success ? p.AccentError : Model.Kind == ToolActivityKind.Write ? p.AccentWrite : p.AccentRead);
        _stripe.BorderBrush = accent;
        _icon.Foreground = accent;
        _title.Foreground = Brush(p.Title);
        _summary.Foreground = Brush(p.Title);
        var body = Brush(p.Body);
        _category.Foreground = body;
        _detail.Foreground = body;
        _duration.Foreground = body;
        _brandBim.Foreground = Brush(p.BrandBim);
        _brandWright.Foreground = Brush(p.BrandWright);
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
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
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
            Hide();
        }
        else
        {
            Show();   // ShowActivated=false: shown without activation
            if (!IsMouseOver) ResumeLife();
        }
    }

    /// <summary>Brand mark reveal: quick fade-in on hover, slower fade-out on leave.</summary>
    private void FadeBrand(double to)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 180 : 350));
        _brand.BeginAnimation(UIElement.OpacityProperty, anim);
    }

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
