#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// RVT-style activity presentation on Inventor's dedicated toast STA. Code-only for the isolated
/// Inventor 2027 load context; unowned/no-activate so a busy Inventor STA cannot stall creation.
/// The feed owns lifetime. This window only renders and reports pointer/click/close events.
/// </summary>
internal sealed class ToastWindow : Window
{
    private readonly Border _card;
    private readonly Border _outline;
    internal readonly Border _closeHost;
    private readonly TextBlock _closeGlyph;
    internal readonly TextBlock _title;
    internal readonly TextBlock _summary;
    private readonly TextBlock _icon;
    internal readonly TextBlock _identity;
    internal readonly Viewbox _counterRow;
    internal readonly RollingToastNumber _successCount = new();
    internal readonly RollingToastNumber _failedCount = new();
    internal readonly RollingToastNumber _captureCount = new();
    private readonly TextBlock[] _labels;
    private readonly TextBlock[] _separators;
    private readonly Grid _brandCell;
    private readonly TextBlock _brand;
    private readonly TextBlock _shine;
    private readonly Run _brandBim;
    private readonly Run _brandWright;
    private readonly Run _shineBim;
    private readonly Run _shineWright;
    internal readonly TranslateTransform _brandSweep = new(-0.75, 0);
    internal readonly TranslateTransform _shineSweep = new(-0.75, 0);
    private readonly TranslateTransform _slide = new(-24, 0);
    private readonly ScaleTransform _scale = new(0.96, 0.96);
    private readonly Func<Point> _cursor;
    private readonly Func<bool> _motion;
    private Action<ToastWindow>? _closed;
    private Action<long>? _dismiss;
    private Action<long>? _click;
    private Action<long>? _enter;
    private Action<long>? _leave;
    private DispatcherTimer? _brandTimer;
    private ToastCard _model;
    private ToastPalette _palette;
    private Point _lastPointer;
    private bool _hasPointer;
    private bool _pointerOver;
    private bool _realHover;
    private bool _brandRevealed;
    private bool _brandHiding;
    private bool _showBranding;
    private bool _done;
    private int _brandGeneration;
    private PxPoint? _position;

    public long CardId => _model.Id;
    public IntPtr Hwnd { get; private set; }
    public bool IsClosing { get; private set; }

    public ToastWindow(ToastCard model, ToastPalette palette, string identity,
        Action<ToastWindow> closed, Action<long> dismiss, Action<long> click,
        Action<long> enter, Action<long> leave, Func<Point>? cursor = null, Func<bool>? motion = null)
    {
        _model = model;
        _palette = palette;
        _closed = closed;
        _dismiss = dismiss;
        _click = click;
        _enter = enter;
        _leave = leave;
        _cursor = cursor ?? ToastNative.CursorPosition;
        _motion = motion ?? (() => SystemParameters.ClientAreaAnimation);
        Title = "IPT-MCP activity";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = ToastLayout.CardWidthDip;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        FontFamily = new FontFamily("Segoe UI, Noto Sans, Arial");

        _icon = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
            Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        _closeGlyph = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _closeHost = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Close", Child = _closeGlyph };
        AutomationProperties.SetName(_closeHost, "Close");
        _closeHost.MouseLeftButtonUp += OnCloseClick;
        _title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_icon, Dock.Left);
        DockPanel.SetDock(_closeHost, Dock.Right);
        header.Children.Add(_icon);
        header.Children.Add(_closeHost);
        header.Children.Add(_title);

        var counters = new StackPanel { Orientation = Orientation.Horizontal };
        _labels = new[] { Label("Success"), Label("Failed"), Label("Capture") };
        _separators = new[] { Separator(), Separator() };
        counters.Children.Add(_successCount);
        counters.Children.Add(_labels[0]);
        counters.Children.Add(_separators[0]);
        counters.Children.Add(_failedCount);
        counters.Children.Add(_labels[1]);
        counters.Children.Add(_separators[1]);
        counters.Children.Add(_captureCount);
        counters.Children.Add(_labels[2]);
        _counterRow = new Viewbox { Child = counters, Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
        _summary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var body = new Grid { Margin = new Thickness(24, 5, 0, 0) };
        body.Children.Add(_counterRow);
        body.Children.Add(_summary);

        _brandBim = new Run(BrandAssets.WordmarkLeft);
        _brandWright = new Run(BrandAssets.WordmarkRight);
        _shineBim = new Run(BrandAssets.WordmarkLeft);
        _shineWright = new Run(BrandAssets.WordmarkRight);
        _brand = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold,
            Inlines = { _brandBim, _brandWright }, ToolTip = BrandAssets.ProductTag };
        _shine = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold,
            Inlines = { _shineBim, _shineWright }, IsHitTestVisible = false };
        _brandCell = new Grid { HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        _brandCell.Children.Add(_brand);
        _brandCell.Children.Add(_shine);
        // Identity never hides with branding. Separate columns prevent label/brand overlap.
        var footer = new Grid { Margin = new Thickness(24, 5, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _identity = new TextBlock { Text = identity, ToolTip = identity, FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0) };
        footer.Children.Add(_identity);
        Grid.SetColumn(_brandCell, 1);
        footer.Children.Add(_brandCell);
        ParkBrand();

        var content = new Grid { Margin = new Thickness(10, 10, 12, 10) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(header);
        Grid.SetRow(body, 1);
        content.Children.Add(body);
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);
        _card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(6, 0, 0, 0),
            Child = content, Cursor = Cursors.Hand };
        var transforms = new TransformGroup();
        transforms.Children.Add(_scale);
        transforms.Children.Add(_slide);
        _outline = new Border { Margin = new Thickness(8), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), Child = _card, RenderTransform = transforms,
            RenderTransformOrigin = new Point(0, 0.5),
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.22, Color = Colors.Black } };
        Content = _outline;
        ApplyPalette(palette);
        MouseEnter += OnPointerEnter;
        MouseMove += OnPointerMove;
        MouseLeave += OnPointerLeave;
        MouseLeftButtonUp += OnCardClick;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Hwnd = new WindowInteropHelper(this).Handle;
        ToastNative.MakeNoActivate(Hwnd);
    }

    public void Appear(PxPoint position)
    {
        if (_done || IsVisible) return;
        // Create the HWND while still hidden; position physically before Show/animation.
        new WindowInteropHelper(this).EnsureHandle();
        MoveTo(position);
        _lastPointer = _cursor();
        _hasPointer = ValidPoint(_lastPointer);
        Show();
        if (!_motion())
        {
            _slide.X = 0;
            _scale.ScaleX = _scale.ScaleY = 1;
            Opacity = 1;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(280);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-24, 0, duration) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, duration) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, duration) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }

    public void MoveTo(PxPoint position)
    {
        if (Hwnd == IntPtr.Zero || position == _position) return;
        _position = position;
        ToastNative.MoveNoActivate(Hwnd, position.X, position.Y);
    }

    public void Update(ToastCard model)
    {
        if (_done || IsClosing || model.Id != CardId || model == _model) return;
        _model = model;
        RenderContent(animate: IsVisible && _motion());
    }

    public void ApplyPalette(ToastPalette palette)
    {
        _palette = palette;
        _outline.Background = _card.Background = Brush(palette.Background);
        _outline.BorderBrush = Brush(palette.Outline);
        _title.Foreground = _summary.Foreground = Brush(palette.Title);
        _closeGlyph.Foreground = _identity.Foreground = Brush(palette.Body);
        foreach (var label in _labels) label.Foreground = Brush(palette.Title);
        foreach (var separator in _separators) separator.Foreground = Brush(palette.Outline);
        _brandBim.Foreground = Brush(palette.BrandBim);
        _brandWright.Foreground = Brush(palette.BrandWright);
        var white = new Rgb(255, 255, 255);
        _shineBim.Foreground = Brush(ToastPaletteChooser.Blend(palette.BrandBim, white, 0.55));
        _shineWright.Foreground = Brush(ToastPaletteChooser.Blend(palette.BrandWright, white, 0.55));
        RenderContent(animate: false);
    }

    private void RenderContent(bool animate)
    {
        var p = _palette;
        var accent = _model.HasFailure ? p.AccentError : p.AccentRead; // blue includes writes, like RVT
        var gradient = new LinearGradientBrush(Brush(ToastPaletteChooser.Blend(accent, new Rgb(255, 255, 255), 0.72)).Color,
            Brush(accent).Color, new Point(0, 0), new Point(0, 1));
        gradient.Freeze();
        _card.BorderBrush = gradient;
        _icon.Foreground = Brush(accent);
        _icon.Text = ToastGlyph.For(!_model.LatestSuccess ? ToastIcon.Error
            : _model.Outcome == "cancelled" ? ToastIcon.Neutral : ToastIcon.Success);
        var activity = _model.Kind == ToastCardKind.Activity;
        _title.Text = activity && _showBranding ? "IPT-MCP - " + _model.Title : _model.Title;
        _title.ToolTip = _title.Text;
        _counterRow.Visibility = activity ? Visibility.Visible : Visibility.Collapsed;
        _summary.Visibility = activity ? Visibility.Collapsed : Visibility.Visible;
        _summary.Text = _model.Body;
        _summary.ToolTip = _counterRow.ToolTip = _model.Body;
        _successCount.SetValue(_model.Succeeded, Brush(p.AccentRead), animate);
        _failedCount.SetValue(_model.Failed, Brush(_model.Failed > 0 ? p.AccentError : p.Body), animate);
        _captureCount.SetValue(_model.Captures, Brush(p.AccentRead), animate);
        AutomationProperties.SetName(_counterRow, $"{_model.Succeeded} Success | {_model.Failed} Failed | {_model.Captures} Capture");
    }

    public void SetShowBranding(bool show)
    {
        if (_done || _showBranding == show) return;
        _showBranding = show;
        RenderContent(false);
        if (!show) ParkBrand();
        else if (_pointerOver && _realHover) ScheduleBrand();
    }

    private void OnPointerEnter(object sender, MouseEventArgs e)
    {
        _pointerOver = true;
        if (!PointerMoved()) return; // Show under a stationary cursor is not a real hover.
        EnterRealHover();
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!_pointerOver || !PointerMoved()) return;
        EnterRealHover();
    }
    private void EnterRealHover()
    {
        if (_done || IsClosing) return;
        if (!_realHover) _enter?.Invoke(CardId);
        _realHover = true;
        ScheduleBrand();
    }
    private void OnPointerLeave(object sender, MouseEventArgs e)
    {
        // The window can move away from a stationary cursor; that still ends a real hover.
        PointerMoved();
        if (_realHover) _leave?.Invoke(CardId);
        _pointerOver = _realHover = false;
        HideBrand();
    }
    private bool PointerMoved()
    {
        var point = _cursor();
        if (!ValidPoint(point)) return false;
        var moved = !_hasPointer || (int)point.X != (int)_lastPointer.X || (int)point.Y != (int)_lastPointer.Y;
        _lastPointer = point;
        _hasPointer = true;
        return moved;
    }
    private static bool ValidPoint(Point p) => !double.IsNaN(p.X) && !double.IsNaN(p.Y)
        && !double.IsInfinity(p.X) && !double.IsInfinity(p.Y);

    private void ScheduleBrand()
    {
        if (!_showBranding || !_pointerOver || _done || IsClosing) return;
        if (_brandRevealed && !_brandHiding) return;
        if (_brandHiding) ParkBrand();
        if (!_motion())
        {
            ParkBrand();
            _brandRevealed = true;
            _brand.OpacityMask = new SolidColorBrush(Dim(0.8));
            return;
        }
        if (_brandTimer != null) return;
        _brandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _brandTimer.Tick += OnBrandTimer;
        _brandTimer.Start();
    }
    private void OnBrandTimer(object? sender, EventArgs e)
    {
        CancelBrandTimer();
        if (!_showBranding || !_pointerOver || _done || IsClosing) return;
        ParkBrand();
        _brandRevealed = true;
        var wipe = new DoubleAnimation(-0.75, 0.75, TimeSpan.FromMilliseconds(500))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        _brandSweep.BeginAnimation(TranslateTransform.XProperty, wipe);
        _shineSweep.BeginAnimation(TranslateTransform.XProperty, wipe);
    }
    private void HideBrand()
    {
        CancelBrandTimer();
        // Preserve the currently painted letters while their cell fades. Removing the held
        // sweep at its base (-0.75) would make the wordmark disappear before the fade starts.
        var brandX = _brandSweep.X;
        var shineX = _shineSweep.X;
        StopBrandSweep();
        _brandSweep.X = brandX;
        _shineSweep.X = shineX;
        if (!_motion() || !_brandRevealed) { ParkBrand(); return; }
        _brandHiding = true;
        var generation = ++_brandGeneration;
        var fade = new DoubleAnimation(_brandCell.Opacity, 0, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => { if (generation == _brandGeneration && !_done) ParkBrand(); };
        _brandCell.BeginAnimation(OpacityProperty, fade);
    }
    private void ParkBrand()
    {
        _brandGeneration++;
        _brandHiding = _brandRevealed = false;
        CancelBrandTimer();
        StopBrandSweep();
        _brandCell.BeginAnimation(OpacityProperty, null);
        _brandCell.Opacity = 1;
        _brandSweep.X = _shineSweep.X = -0.75;
        _brand.OpacityMask = BrandMask(_brandSweep, false);
        _shine.OpacityMask = BrandMask(_shineSweep, true);
    }
    private void CancelBrandTimer()
    {
        if (_brandTimer == null) return;
        _brandTimer.Stop();
        _brandTimer.Tick -= OnBrandTimer;
        _brandTimer = null;
    }
    private void StopBrandSweep()
    {
        _brandSweep.BeginAnimation(TranslateTransform.XProperty, null);
        _shineSweep.BeginAnimation(TranslateTransform.XProperty, null);
    }
    private static LinearGradientBrush BrandMask(TranslateTransform transform, bool shine) => new()
    {
        StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), RelativeTransform = transform,
        GradientStops =
        {
            new GradientStop(Dim(shine ? 0 : 0.8), 0),
            new GradientStop(Dim(shine ? 0 : 0.8), 0.36),
            new GradientStop(Dim(shine ? 1 : 0), 0.44),
            new GradientStop(Dim(0), 0.52), new GradientStop(Dim(0), 1),
        },
    };

    private void OnCloseClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsClosing && !_done) _dismiss?.Invoke(CardId);
    }
    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsClosing && !_done) _click?.Invoke(CardId);
    }
    public void BeginClose()
    {
        if (IsClosing || _done) return;
        IsClosing = true;
        CancelBrandTimer();
        if (!_motion() || !IsVisible) { CloseNow(); return; }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => CloseNow();
        BeginAnimation(OpacityProperty, fade);
    }
    public void CloseNow()
    {
        if (_done) return;
        IsClosing = true;
        try { Close(); }
        finally { FinishClose(); }
    }
    private void OnClosed(object? sender, EventArgs e) => FinishClose();
    private void FinishClose()
    {
        if (_done) return;
        _done = IsClosing = true;
        CancelBrandTimer();
        _brandGeneration++;
        StopBrandSweep();
        _brandCell.BeginAnimation(OpacityProperty, null);
        BeginAnimation(OpacityProperty, null);
        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _successCount.StopAnimation();
        _failedCount.StopAnimation();
        _captureCount.StopAnimation();
        MouseEnter -= OnPointerEnter;
        MouseMove -= OnPointerMove;
        MouseLeave -= OnPointerLeave;
        MouseLeftButtonUp -= OnCardClick;
        _closeHost.MouseLeftButtonUp -= OnCloseClick;
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnClosed;
        var closed = _closed;
        _closed = null;
        _dismiss = _click = _enter = _leave = null;
        closed?.Invoke(this);
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
    private static TextBlock Separator() => new() { Text = "|", FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
    private static Color Dim(double opacity) => Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0);
    private static SolidColorBrush Brush(Rgb c)
    {
        var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}
#endif
