#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Bimwright.Ipt.Shared.Views
{
    /// <summary>
    /// Dedicated UI thread for the History window — the same pattern as <c>ToastHost</c>:
    /// a background STA running a WPF dispatcher so the window never shares Inventor's
    /// message loop (it stays responsive while a modal dialog holds the main thread and
    /// cannot touch Inventor COM objects). Unlike ToastHost this thread starts eagerly at
    /// Activate: the session log marshals its collection mutations through
    /// <see cref="Post"/>, so the dispatcher must exist before the first command lands.
    /// </summary>
    internal sealed class HistoryHost
    {
        private readonly Thread _thread;
        private readonly Dispatcher _dispatcher;
        private Window? _window;

        public HistoryHost()
        {
            Dispatcher? dispatcher = null;
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                try
                {
                    dispatcher = Dispatcher.CurrentDispatcher;
                    dispatcher.UnhandledException += (_, e) => e.Handled = true;   // a window bug must never take Inventor down
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
                Name = "Bimwright.Ipt.History",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!ready.Wait(5000) || dispatcher == null)
                throw new InvalidOperationException("history thread did not start");
            _dispatcher = dispatcher;
        }

        /// <summary>Posts <paramref name="action"/> onto the history UI thread; safe from any thread.</summary>
        public void Post(Action action)
        {
            try { _dispatcher.BeginInvoke(action); } catch { }
        }

        /// <summary>
        /// Show-or-focus singleton: creates the window via <paramref name="factory"/> on first
        /// call (the factory runs on the history thread), re-shows it after a hide, otherwise
        /// brings it forward. Safe to call from any thread.
        /// </summary>
        public void ShowOrFocus(Func<Window> factory)
        {
            Post(() =>
            {
                try
                {
                    if (_window == null || !_window.IsLoaded)
                    {
                        _window = factory();
                        _window.Closed += (_, _) => _window = null;
                        _window.Show();
                    }
                    else if (!_window.IsVisible)
                    {
                        _window.Show();
                    }
                    else
                    {
                        _window.Activate();
                    }
                }
                catch { }
            });
        }

        /// <summary>Called from Deactivate (Inventor STA). Bounded waits: never hangs Inventor's shutdown.</summary>
        public void Shutdown()
        {
            try
            {
                _dispatcher.Invoke(() =>
                {
                    try { _window?.Close(); } catch { }
                    _window = null;
                }, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(3));
            }
            catch { }
            try { _dispatcher.InvokeShutdown(); } catch { }
            try { _thread.Join(3000); } catch { }
        }
    }
}
#endif
