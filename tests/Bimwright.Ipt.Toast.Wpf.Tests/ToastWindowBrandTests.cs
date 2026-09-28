using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Wpf.Tests;

public sealed class ToastWindowBrandTests
{
    private static ToastCard Card(int success = 1, int failed = 0, int captures = 0) =>
        new(1, ToastCardKind.Activity, "Extrude", "Latest result", success, failed, captures, true, failed > 0);
    private static ToastWindow Window(ToastCard? card = null, Func<Point>? cursor = null, bool motion = true,
        Action<long>? enter = null, Action<long>? leave = null, Action<long>? click = null, Action<long>? dismiss = null) =>
        new(card ?? Card(), ToastPalette.LightElevated, "Inventor 2027", _ => { },
            dismiss ?? (_ => { }), click ?? (_ => { }), enter ?? (_ => { }), leave ?? (_ => { }),
            cursor ?? (() => new Point(-500, -500)), () => motion);
    private static void Hover(ToastWindow w) => w.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
    private static void Move(ToastWindow w) => w.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent });
    private static void Leave(ToastWindow w) => w.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
    private static void Click(UIElement w) => w.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseUpEvent });

    [Fact]
    public void One_compact_card_has_counts_identity_no_thumbnail_and_stable_height()
        => Sta.Run(() =>
        {
            var w = Window();
            try
            {
                w.Appear(new PxPoint(40, 160));
                Sta.Pump(60);
                var height = w.ActualHeight;
                var hwnd = w.Hwnd;
                Assert.Equal("Extrude", w._title.Text);
                Assert.Equal("Inventor 2027", w._identity.Text);
                Assert.Equal(Visibility.Visible, w._counterRow.Visibility);
                Assert.Equal(Visibility.Collapsed, w._summary.Visibility);
                Assert.Empty(Descendants(w).OfType<Image>());
                for (var i = 2; i <= 100; i++) w.Update(Card(i, 3, 12) with { Title = new string('x', 200) });
                w.UpdateLayout();
                Assert.Equal(100, w._successCount.Value);
                Assert.Equal(3, w._failedCount.Value);
                Assert.Equal(12, w._captureCount.Value);
                Assert.Equal(height, w.ActualHeight);
                Assert.Equal(hwnd, w.Hwnd);
                Assert.False(w.ShowActivated);
                Assert.Null(w.Owner);
                Assert.True(ToastNative.Rect(hwnd)!.Value.Left >= 0);
                Assert.Equal(-0.75, w._brandSweep.X); // no entrance brand effect
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Counters_roll_vertically_replace_bursts_and_release_clocks_on_close()
        => Sta.Run(() =>
        {
            var w = Window();
            try
            {
                w.Appear(new PxPoint(40, 160));
                w.Update(Card(9));
                var current = (TranslateTransform)((Viewbox)w._successCount.Children[1]).RenderTransform;
                var outgoing = (TranslateTransform)((Viewbox)w._successCount.Children[0]).RenderTransform;
                Assert.True(current.HasAnimatedProperties);
                Sta.Pump(70);
                Assert.InRange(current.Y, 0, RollingToastNumber.SlotHeight);
                Assert.True(outgoing.Y < 0);
                w.Update(Card(9999));
                Assert.Equal(9999, w._successCount.Value);
                Assert.Equal(25, w._successCount.Width);
                Sta.Pump(350);
                Assert.Equal(0, current.Y, 3);
                w.CloseNow();
                Assert.False(current.HasAnimatedProperties);
                Assert.False(outgoing.HasAnimatedProperties);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Stationary_enter_does_not_pause_but_first_real_move_inside_does()
        => Sta.Run(() =>
        {
            var point = new Point(10, 10);
            var enters = 0;
            var leaves = 0;
            var w = Window(cursor: () => point, enter: _ => enters++, leave: _ => leaves++);
            try
            {
                w.Appear(new PxPoint(40, 160));
                Hover(w);
                Assert.Equal(0, enters);
                point = new Point(12, 10);
                Move(w);
                Assert.Equal(1, enters);
                point = new Point(14, 10);
                Move(w);
                Assert.Equal(1, enters);
                point = new Point(500, 500);
                Leave(w);
                Assert.Equal(1, leaves);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Stationary_leave_after_real_hover_rearms_idle_and_cancels_brand()
        => Sta.Run(() =>
        {
            var now = TimeSpan.Zero;
            var feed = new ToastFeed(now: () => now);
            feed.Record(new ToastModel("extrude", "Extrude", "Feature", "Result", "", null,
                ToolActivityKind.Write, true, 0), true);
            var point = new Point(10, 10);
            var w = Window(feed.TakeRender().Card, cursor: () => point,
                enter: id => feed.PointerEntered(id), leave: feed.PointerLeft);
            try
            {
                w.SetShowBranding(true);
                w.Appear(new PxPoint(40, 160));
                point = new Point(12, 10);
                Hover(w);
                now = TimeSpan.FromSeconds(60);
                Assert.False(feed.Tick(true));
                // Moving the window away raises MouseLeave without moving the screen cursor.
                Leave(w);
                Sta.Pump(250);
                now += TimeSpan.FromSeconds(19);
                Assert.False(feed.Tick(true));
                now += TimeSpan.FromSeconds(1);
                Assert.True(feed.Tick(true));
                Assert.Equal(ToastPhase.Closing, feed.TakeRender().Phase);
                Assert.False(w._brandSweep.HasAnimatedProperties);
                Assert.Equal(-0.75, w._brandSweep.X);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Brand_is_opt_in_hover_revealed_then_fades_without_hiding_version()
        => Sta.Run(() =>
        {
            var point = new Point(10, 10);
            var w = Window(cursor: () => point);
            try
            {
                w.Appear(new PxPoint(40, 160));
                point = new Point(20, 20);
                Hover(w);
                Sta.Pump(180);
                Assert.False(w._brandSweep.HasAnimatedProperties);
                w.SetShowBranding(true);
                Assert.Equal("IPT-MCP - Extrude", w._title.Text);
                Assert.True(Sta.PumpUntil(() => w._brandSweep.HasAnimatedProperties, 700));
                Sta.Pump(600);
                Assert.Equal(0.75, w._brandSweep.X, 3);
                // Result updates, reflow and re-theme do not replay the wipe.
                w.Update(Card(2));
                w.ApplyPalette(ToastPalette.DarkElevated);
                w.MoveTo(new PxPoint(50, 170));
                Assert.Equal(0.75, w._brandSweep.X, 3);
                point = new Point(600, 600);
                Leave(w);
                Sta.Pump(300);
                Assert.Equal(-0.75, w._brandSweep.X, 3);
                Assert.Equal(Visibility.Visible, w._identity.Visibility);
                Assert.Equal("Inventor 2027", w._identity.Text);
                w.SetShowBranding(false);
                Assert.Equal("Extrude", w._title.Text);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Quick_hover_pass_cancels_delayed_brand_reveal()
        => Sta.Run(() =>
        {
            var point = new Point(1, 1);
            var w = Window(cursor: () => point);
            try
            {
                w.SetShowBranding(true);
                w.Appear(new PxPoint(40, 160));
                point = new Point(2, 2);
                Hover(w);
                point = new Point(3, 3);
                Leave(w);
                Sta.Pump(200);
                Assert.False(w._brandSweep.HasAnimatedProperties);
                Assert.Equal(-0.75, w._brandSweep.X);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Reduced_motion_settles_without_counter_brand_or_enter_clocks()
        => Sta.Run(() =>
        {
            var point = new Point(1, 1);
            var w = Window(cursor: () => point, motion: false);
            try
            {
                w.SetShowBranding(true);
                w.Appear(new PxPoint(40, 160));
                w.Update(Card(9));
                point = new Point(2, 2);
                Hover(w);
                Assert.Equal(1, w.Opacity);
                Assert.False(w.HasAnimatedProperties);
                Assert.False(w._brandSweep.HasAnimatedProperties);
                Assert.False(((TranslateTransform)((Viewbox)w._successCount.Children[1]).RenderTransform).HasAnimatedProperties);
                w.BeginClose();
                Assert.False(w.IsVisible);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Close_control_only_dismisses_while_card_click_routes_to_history_callback()
        => Sta.Run(() =>
        {
            var dismissed = 0;
            var clicked = 0;
            var w = Window(dismiss: _ => dismissed++, click: _ => clicked++);
            try
            {
                w.Appear(new PxPoint(40, 160));
                Click((UIElement)w._closeHost.Child);
                Assert.Equal(1, dismissed);
                Assert.Equal(0, clicked);
                Assert.Same(Brushes.Transparent, w._closeHost.Background);
                Click(w._title);
                Assert.Equal(1, clicked);
                w.BeginClose();
                Click(w._title);
                Assert.Equal(1, clicked);
            }
            finally { w.CloseNow(); }
        });

    [Theory]
    [InlineData(ToastCardKind.Status)]
    [InlineData(ToastCardKind.TaskResult)]
    public void Status_and_explicit_reports_use_the_same_card_body_slot(ToastCardKind kind)
        => Sta.Run(() =>
        {
            var w = Window(Card() with { Kind = kind, Title = "Task cancelled", Body = "Agent reported · Done", Outcome = "cancelled" });
            try
            {
                Assert.Equal(Visibility.Collapsed, w._counterRow.Visibility);
                Assert.Equal(Visibility.Visible, w._summary.Visibility);
                Assert.Contains("Agent reported", w._summary.Text);
                Assert.Equal("Task cancelled", w._title.Text);
                Assert.Equal("Inventor 2027", w._identity.Text);
            }
            finally { w.CloseNow(); }
        });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
}
