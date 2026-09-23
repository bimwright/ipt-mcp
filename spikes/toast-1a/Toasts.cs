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
    internal sealed class ToastWin : Window
    {
        public const double CardWidth = 320;
        private readonly TextBlock _body;
        public IntPtr Hwnd;
        public readonly bool NoActivate;
        public string Id;

        public ToastWin(string id, string title, string body, bool noActivate, bool animate)
        {
            Id = id; NoActivate = noActivate;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height; Width = CardWidth;
            ShowActivated = !noActivate;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000;

            _body = new TextBlock { Text = body, Foreground = Brushes.Gainsboro, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var stack = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            stack.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            stack.Children.Add(_body);
            var card = new Border
            {
                Width = CardWidth - 16, Margin = new Thickness(8), CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), BorderThickness = new Thickness(6, 0, 0, 0),
                Child = stack,
            };
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
        public ToastWin ShowOnThisThread(string id, string title, string body, bool noActivate, bool animate, IntPtr owner, int? x, int? y)
        {
            var w = new ToastWin(id, title, body, noActivate, animate);
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
