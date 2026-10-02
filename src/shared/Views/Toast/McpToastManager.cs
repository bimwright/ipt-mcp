#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
#nullable disable
using System;
using System.Windows;
using System.Windows.Threading;

namespace Bimwright.Ipt.Shared.Views.Toast
{
    /// <summary>
    /// Reconciles the WPF toast surface with the pure <see cref="ActivityAggregator"/>
    /// state machine. There is deliberately one window slot: the aggregator owns the
    /// card lifetime and the manager owns only WPF objects and their dispatcher timer.
    /// Same contract as rvt-mcp's manager. Two Inventor-specific differences: the card is
    /// never owned by an Inventor HWND (the toast thread is not Inventor's UI thread), and
    /// its position comes from the host, which reads the Inventor frame and view.
    /// </summary>
    internal sealed class McpToastManager
    {
        private const double EdgeMargin = 16;
        private const int TickMilliseconds = 100;

        private readonly Dispatcher _dispatcher;
        private readonly ActivityAggregator _aggregator;
        private readonly Func<bool> _isFrameUsable;
        private readonly Action<long> _onClick;
        private readonly Func<bool> _showBranding;
        private readonly Func<string> _instanceIdentity;
        private readonly Func<bool> _motionEnabled;
        private readonly Func<Point?> _position;
        private readonly DispatcherTimer _timer;
        private McpToastWindow _window;
        private Point? _lastPosition;

        /// <param name="isFrameUsable">
        /// Returns whether the Inventor frame can display an activity card. The callback is
        /// evaluated on the toast dispatcher by the timer; it must be cheap and must not
        /// call back into this manager. A missing callback means that the frame is usable.
        /// </param>
        /// <param name="instanceIdentity">
        /// Card title identifying this Inventor instance so cards from parallel
        /// Inventor processes can be told apart. Evaluated when each card is
        /// created; null/empty falls back to the product name.
        /// </param>
        /// <param name="motionEnabled">
        /// Passed to each card window; null follows the Windows animation setting.
        /// </param>
        /// <param name="position">
        /// Top-left of the card in device-independent units, or null when the host has no
        /// placement (the card then sits 16 units from the screen corner).
        /// </param>
        public McpToastManager(
            Dispatcher dispatcher,
            ActivityAggregator aggregator,
            Func<bool> isFrameUsable = null,
            Action<long> onClick = null,
            Func<bool> showBranding = null,
            Func<string> instanceIdentity = null,
            Func<bool> motionEnabled = null,
            Func<Point?> position = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
            _isFrameUsable = isFrameUsable ?? (() => true);
            _onClick = onClick;
            _showBranding = showBranding ?? (() => true);
            _instanceIdentity = instanceIdentity ?? (() => null);
            _motionEnabled = motionEnabled;
            _position = position;

            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(TickMilliseconds)
            };
            _timer.Tick += OnTimerTick;
        }

        /// <summary>True while a card window exists (visible or fading).</summary>
        public bool HasWindow => _window != null;

        /// <summary>Apply the current branding preference to the open card, if any.</summary>
        public void ApplyShowBranding()
        {
            EnsureDispatcher();
            _window?.SetShowBranding(_showBranding());
        }

        /// <summary>
        /// Reconciles the single WPF window to the aggregator's current render state.
        /// Call this after a mutator returns true. Calling it more often is safe because
        /// <see cref="ActivityAggregator.TakeRender"/> always reads current state.
        /// </summary>
        public void Render()
        {
            EnsureDispatcher();

            var render = _aggregator.TakeRender();
            switch (render.Phase)
            {
                case ActivityCardPhase.Visible:
                    ReconcileVisible(render.Card);
                    return;

                case ActivityCardPhase.Closing:
                    ReconcileClosing(render.Card);
                    return;

                default:
                    ForceCloseWindow();
                    StopTimerIfNoWindow();
                    return;
            }
        }

        /// <summary>
        /// Allows the host to supply an explicit frame usability result.
        /// The timer uses the configured callback and calls this overload internally.
        /// </summary>
        internal void Tick(bool frameUsable)
        {
            EnsureDispatcher();
            if (_window == null)
                return;

            if (_aggregator.Tick(frameUsable))
                Render();
        }

        /// <summary>Follow the Inventor frame: move the open card when its anchor moved.</summary>
        public void Reposition()
        {
            EnsureDispatcher();
            var window = _window;
            if (window == null)
                return;

            var target = ResolvePosition();
            if (_lastPosition.HasValue && _lastPosition.Value == target)
                return;
            _lastPosition = target;
            window.SetPosition(target.Y, target.X);
        }

        /// <summary>
        /// Closes the current window synchronously. This is used during Inventor shutdown,
        /// before the dispatcher is torn down, and is also safe for a normal dismiss-all.
        /// </summary>
        public void DismissAllImmediate()
        {
            EnsureDispatcher();

            _timer.Stop();
            var window = _window;
            _window = null; // Ignore the close callback from this forced close.

            if (window != null)
            {
                try { window.CloseImmediate(); }
                catch { }
            }

            // A dismissed card must not be resurrected by a render that was posted
            // before DismissAllImmediate ran. Reset is idempotent and clears Pending too.
            _aggregator.Reset();
            // Reset claims a render request so a concurrent/late notifier cannot leave
            // the coalescing flag stuck with no posted render left to drain it.
            _aggregator.TakeRender();
        }

        /// <summary>Stop the timer before the host dies.</summary>
        public void Dispose()
        {
            if (_dispatcher.CheckAccess())
                _timer.Stop();
        }

        private void ReconcileVisible(ActivitySnapshot card)
        {
            if (card == null)
            {
                ForceCloseWindow();
                StopTimerIfNoWindow();
                return;
            }

            if (_window != null && _window.CardId == card.CardId)
            {
                // Updating an existing card must not replay its enter/brand animation or
                // alter its measured height.
                _window.Update(card);
                EnsureTimer();
                return;
            }

            // A result arriving while the old card fades starts a new CardId. Force-close
            // the old HWND first so there can never be two topmost windows.
            ForceCloseWindow();
            CreateWindow(card);
        }

        private void ReconcileClosing(ActivitySnapshot card)
        {
            if (card == null)
            {
                StopTimerIfNoWindow();
                return;
            }

            if (_window != null && _window.CardId == card.CardId)
            {
                // BeginClose is idempotent in the window. Repeated stale renders cannot
                // restart or reverse the fade.
                _window.BeginClose();
                EnsureTimer();
                return;
            }

            // Pending render for a card whose window was already closed (or replaced).
            // Complete the aggregator transition immediately; a late callback from an
            // older window is ignored by OnWindowClosed's identity + CardId fence.
            ForceCloseWindow();
            _aggregator.CardClosed(card.CardId);
            StopTimerIfNoWindow();
        }

        private void CreateWindow(ActivitySnapshot card)
        {
            var window = new McpToastWindow(
                card,
                OnWindowClosed,
                OnDismissRequested,
                OnCardClicked,
                OnPointerEntered,
                OnPointerLeft,
                motionEnabled: _motionEnabled,
                instanceIdentity: _instanceIdentity());

            _window = window;
            window.SetShowBranding(_showBranding());

            // WPF initializes Window.Top/Left to NaN. Set finite coordinates before Show
            // so an early Loaded/close callback cannot animate from an invalid value.
            var position = ResolvePosition();
            _lastPosition = position;
            window.SetPosition(position.Y, position.X);
            window.CapturePointerBaseline();
            window.Show();
            window.PlayEnterAnimation();
            EnsureTimer();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_window == null)
            {
                _timer.Stop();
                return;
            }

            bool frameUsable;
            try { frameUsable = _isFrameUsable(); }
            catch { frameUsable = true; }
            Tick(frameUsable);
        }

        private void OnWindowClosed(McpToastWindow window, long cardId)
        {
            EnsureDispatcher();
            if (!ReferenceEquals(_window, window) || window.CardId != cardId)
                return;

            _window = null;
            _aggregator.CardClosed(cardId);
            StopTimerIfNoWindow();
        }

        private void OnDismissRequested(long cardId)
        {
            EnsureDispatcher();
            if (_window == null || _window.CardId != cardId)
                return;

            if (_aggregator.Dismiss(cardId))
                Render();
        }

        private void OnPointerEntered(long cardId)
        {
            EnsureDispatcher();
            if (_window != null && _window.CardId == cardId)
                _aggregator.PointerEntered(cardId);
        }

        private void OnPointerLeft(long cardId)
        {
            EnsureDispatcher();
            if (_window != null && _window.CardId == cardId)
                _aggregator.PointerLeft(cardId);
        }

        private void ForceCloseWindow()
        {
            var window = _window;
            if (window == null)
                return;

            _window = null;
            try { window.CloseImmediate(); }
            catch { }
            StopTimerIfNoWindow();
        }

        private void OnCardClicked(long cardId)
        {
            EnsureDispatcher();
            if (_window == null || _window.CardId != cardId)
                return;

            // Opening History is supplied by the host, because the History window lives
            // in the add-in and is absent from the WPF harness.
            try { _onClick?.Invoke(cardId); }
            catch { }

            if (_aggregator.Dismiss(cardId))
                Render();
        }

        private void EnsureTimer()
        {
            if (!_timer.IsEnabled && _window != null)
                _timer.Start();
        }

        private void StopTimerIfNoWindow()
        {
            if (_window == null)
                _timer.Stop();
        }

        private Point ResolvePosition()
        {
            Point? placed = null;
            try { placed = _position?.Invoke(); }
            catch { }

            // Guard both the host and fallback paths. Invalid DPI/rect data must never
            // leak NaN or infinity into WPF dependency properties.
            if (placed.HasValue && IsFinite(placed.Value.X) && IsFinite(placed.Value.Y))
                return placed.Value;
            return new Point(EdgeMargin, EdgeMargin);
        }

        private void EnsureDispatcher()
        {
            if (!_dispatcher.CheckAccess())
                throw new InvalidOperationException("McpToastManager must run on the toast dispatcher thread.");
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
#endif
