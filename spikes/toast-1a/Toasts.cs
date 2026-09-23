// PROTOTYPE — throwaway toast compatibility spike (roadmap Phase 1a).
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ToastSpike
{
    /// Code-built toast window (no XAML / pack URIs). Mirrors rvt-mcp's look only roughly.
    /// Toast colour set. "auto-inverse" is resolved against Inventor's active theme (Dark -> light toast).
    internal sealed class Palette
    {
        public string Name; public Color Bg, Title, Body, Outline, Accent; public bool Shadow;

        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        public static Palette Get(string name)
        {
            switch (name)
            {
                case "light": return new Palette { Name = name, Bg = C("#F5F5F5"), Title = C("#1E293B"), Body = C("#475569"), Outline = C("#F5F5F5"), Accent = C("#007ACC") };
                case "light-elevated": return new Palette { Name = name, Bg = C("#FFFFFF"), Title = C("#0F172A"), Body = C("#334155"), Outline = C("#94A3B8"), Accent = C("#007ACC"), Shadow = true };
                case "dark-elevated": return new Palette { Name = name, Bg = C("#111827"), Title = C("#F9FAFB"), Body = C("#D1D5DB"), Outline = C("#9CA3AF"), Accent = C("#3B82F6"), Shadow = true };
                default: return new Palette { Name = "dark", Bg = C("#2B2F36"), Title = C("#FFFFFF"), Body = C("#DCDCDC"), Outline = C("#2B2F36"), Accent = C("#3B82F6") };
            }
        }
        /// Inverse of Inventor's theme: dark UI -> elevated light toast, light UI -> elevated dark toast.
        public static Palette Inverse(string inventorTheme) =>
            (inventorTheme ?? "").IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0 ? Get("light-elevated") : Get("dark-elevated");
    }

    internal sealed class ToastWin : Window
    {
        public const double CardWidth = 320;
        private readonly TextBlock _title, _body;
        private readonly Border _outer, _card;
        public IntPtr Hwnd;
        public readonly bool NoActivate;
        public string Id;
        public bool AutoTheme;          // follows Inventor theme changes (inverse)
        public string PaletteName;

        public ToastWin(string id, string title, string body, bool noActivate, bool animate, Palette palette = null)
        {
            Id = id; NoActivate = noActivate;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height; Width = CardWidth;
            ShowActivated = !noActivate;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000;

            _body = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            _title = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13 };
            var stack = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            stack.Children.Add(_title);
            stack.Children.Add(_body);
            _card = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(6, 0, 0, 0), Child = stack };
            _outer = new Border
            {
                Width = CardWidth - 16, Margin = new Thickness(8), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), Child = _card,
            };
            var card = _outer;
            ApplyPalette(palette ?? Palette.Get("dark"));
            Content = card;
            if (animate)
            {
                var a = new DoubleAnimation(1.0, 0.55, TimeSpan.FromMilliseconds(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                card.BeginAnimation(OpacityProperty, a);
            }
            SourceInitialized += (s, e) =>
            {
                Hwnd = new WindowInteropHelper(this).Handle;
                if (noActivate)
                {
                    var ex = Native.ExStyle(Hwnd) | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                    Native.SetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
                }
            };
        }

        public void SetBody(string s) => _body.Text = s;

        /// Must run on this window's dispatcher.
        public void ApplyPalette(Palette p)
        {
            PaletteName = p.Name;
            _outer.Background = new SolidColorBrush(p.Bg);
            _outer.BorderBrush = new SolidColorBrush(p.Outline);
            _card.BorderBrush = new SolidColorBrush(p.Accent);
            _title.Foreground = new SolidColorBrush(p.Title);
            _body.Foreground = new SolidColorBrush(p.Body);
            _outer.Effect = p.Shadow ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black } : null;
        }
    }

    /// A toast host = the dispatcher that owns the toast windows.
    /// "A" = Inventor's own STA thread (WPF Dispatcher.CurrentDispatcher on it).
    /// "B" = dedicated STA thread running Dispatcher.Run().
    internal sealed class ToastHost
    {
        public readonly string Mode;
        public Dispatcher D;
        public Thread Thread;
        public uint Win32ThreadId;
        public readonly ConcurrentDictionary<string, ToastWin> Toasts = new ConcurrentDictionary<string, ToastWin>();

        private ToastHost(string mode) { Mode = mode; }

        public static ToastHost CreateOnCurrentThread()
        {
            return new ToastHost("A") { D = Dispatcher.CurrentDispatcher, Thread = Thread.CurrentThread, Win32ThreadId = Native.GetCurrentThreadId() };
        }

        public static ToastHost CreateDedicated()
        {
            var h = new ToastHost("B");
            var ready = new ManualResetEventSlim(false);
            h.Thread = new Thread(() =>
            {
                h.D = Dispatcher.CurrentDispatcher;
                h.Win32ThreadId = Native.GetCurrentThreadId();
                ready.Set();
                Dispatcher.Run();
            }) { IsBackground = true, Name = "ToastSpike.B" };
            h.Thread.SetApartmentState(ApartmentState.STA);
            h.Thread.Start();
            if (!ready.Wait(5000)) throw new TimeoutException("dedicated toast thread did not start");
            return h;
        }

        /// Must run on this host's dispatcher thread.
        public ToastWin ShowOnThisThread(string id, string title, string body, bool noActivate, bool animate, IntPtr owner, int? x, int? y, Palette palette = null, bool autoTheme = false)
        {
            var w = new ToastWin(id, title, body, noActivate, animate, palette) { AutoTheme = autoTheme };
            if (owner != IntPtr.Zero) new WindowInteropHelper(w).Owner = owner;
            w.Closed += (s, e) => Toasts.TryRemove(id, out _);
            w.Show();
            if (x.HasValue && y.HasValue)
                Native.SetWindowPos(w.Hwnd, IntPtr.Zero, x.Value, y.Value, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Toasts[id] = w;
            return w;
        }

        public void Shutdown()
        {
            try
            {
                D.Invoke(() => { foreach (var t in Toasts.Values) t.Close(); }, TimeSpan.FromSeconds(3));
                if (Mode == "B") D.InvokeShutdown();
            }
            catch { }
        }
    }
}
