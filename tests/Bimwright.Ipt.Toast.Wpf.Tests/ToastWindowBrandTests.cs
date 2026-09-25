using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bimwright.Ipt.Shared.Views;
using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Wpf.Tests;

/// <summary>
/// IPT-03/04 on the real window: the brand wipe replays on hover, and nothing else
/// (update, retheme, reflow, close) disturbs or re-triggers it. BrandWipeCount +
/// LastWipeDelayMs are the deterministic seam; the sweep-transform assertions prove
/// the clocks actually move.
/// </summary>
public sealed class ToastWindowBrandTests
{
    private static ToastModel Model() =>
        new("extrude", "Extrude", "feature", "Extruded 2 bodies", "", null, ToolActivityKind.Write, true, 120);

    private static void Hover(ToastWindow w) =>
        w.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });

    private static void Leave(ToastWindow w) =>
        w.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });

    [Fact]
    public void Appear_schedules_the_entrance_wipe()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                Assert.Equal(1, w.BrandWipeCount);
                Assert.Equal(BrandMotion.EntranceDelayMs, w.LastWipeDelayMs);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Hover_replays_the_wipe_with_the_hover_delay()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                Hover(w);

                Assert.Equal(2, w.BrandWipeCount);
                Assert.Equal(BrandMotion.HoverDelayMs, w.LastWipeDelayMs);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Repeated_hover_replaces_the_clock_instead_of_queuing()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();
                Hover(w);
                Leave(w);
                Hover(w);
                Leave(w);
                Hover(w);

                // One wipe per enter, each on the same property: BeginAnimation replaces, never queues.
                Assert.Equal(4, w.BrandWipeCount);
                Assert.Equal(BrandMotion.HoverDelayMs, w.LastWipeDelayMs);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void UpdateModel_does_not_replay_the_wipe()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                w.UpdateModel(
                    new ToastModel("fillet", "Fillet", "feature", "Updated summary", "", null, ToolActivityKind.Write, true, 40),
                    ToastPalette.LightElevated);

                Assert.Equal(1, w.BrandWipeCount);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Retheming_does_not_replay_the_wipe()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                w.ApplyPalette(ToastPalette.DarkElevated);
                w.ApplyPalette(ToastPalette.LightElevated);

                Assert.Equal(1, w.BrandWipeCount);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Reflow_does_not_replay_the_wipe()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                w.MoveTo(new PxPoint(1700, 900));
                w.MoveTo(new PxPoint(1700, 800));

                Assert.Equal(1, w.BrandWipeCount);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Hover_while_closing_starts_no_new_wipe()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();
                w.BeginClose();

                Hover(w);

                Assert.Equal(1, w.BrandWipeCount);
                Assert.True(w.IsClosing);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Closing_mid_sweep_disposes_without_throwing()
        => Sta.Run(() =>
        {
            ToastWindow? closed = null;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, x => closed = x);
            w.Appear();
            Sta.Pump(100);               // the wipe clock is live; the fade is not
            Hover(w);

            w.BeginClose();
            Sta.PumpUntil(() => closed == w, 2000);   // 200 ms fade-out → CloseNow

            Assert.Same(w, closed);
            Assert.True(w.IsClosing);
        });

    [Fact]
    public void The_sweep_really_moves_the_mask_and_settles_at_the_end()
        => Sta.Run(() =>
        {
            ToastWindow.MotionOverrideForTests = true;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                // Before the entrance delay the mask still sits at its parked offset.
                Sta.Pump(900);
                Assert.True(w._brandSweep.X < BrandMotion.SweepTo,
                    $"expected sweep not finished, X={w._brandSweep.X}");

                // 1300 ms delay + 800 ms sweep — give the wall clock ample slack.
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X == BrandMotion.SweepTo, 4000),
                    $"brand mask did not settle at {BrandMotion.SweepTo}, X={w._brandSweep.X}");
                Assert.Equal(BrandMotion.SweepTo, w._shineSweep.X);
            }
            finally { w.CloseNow(); ToastWindow.MotionOverrideForTests = null; }
        });

    [Fact]
    public void Hover_after_the_first_wipe_replays_and_settles_again()
        => Sta.Run(() =>
        {
            ToastWindow.MotionOverrideForTests = true;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X == BrandMotion.SweepTo, 4000));

                Hover(w);

                // The replacement clock parks the mask back at the parked offset during its delay.
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X < 0.5, 1000),
                    $"hover did not re-arm the sweep, X={w._brandSweep.X}");
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X == BrandMotion.SweepTo, 3000),
                    $"hover sweep did not settle, X={w._brandSweep.X}");
            }
            finally { w.CloseNow(); ToastWindow.MotionOverrideForTests = null; }
        });

    [Fact]
    public void Suppression_mid_sweep_freezes_the_clock_and_restores_it()
        => Sta.Run(() =>
        {
            ToastWindow.MotionOverrideForTests = true;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X > BrandMotion.SweepFrom, 3000),
                    "sweep never started");

                w.SetSuppressed(true);
                Sta.Pump(100);               // absorb the in-flight tick queued before Pause applied
                var frozen = w._brandSweep.X;
                Sta.Pump(500);
                Assert.Equal(frozen, w._brandSweep.X);   // a hidden sweep must not advance unseen

                w.SetSuppressed(false);
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X == BrandMotion.SweepTo, 3000),
                    $"sweep did not finish after restore, X={w._brandSweep.X}");
            }
            finally { w.CloseNow(); ToastWindow.MotionOverrideForTests = null; }
        });

    [Fact]
    public void Suppression_during_the_delay_never_runs_the_wipe_unseen()
        => Sta.Run(() =>
        {
            ToastWindow.MotionOverrideForTests = true;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();
                Sta.Pump(300);               // still inside the 1300 ms entrance delay

                w.SetSuppressed(true);
                Sta.Pump(2200);              // longer than delay + sweep combined
                Assert.Equal(BrandMotion.SweepFrom, w._brandSweep.X);   // nothing ran while hidden

                w.SetSuppressed(false);
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X == BrandMotion.SweepTo, 4000),
                    $"sweep did not run after restore, X={w._brandSweep.X}");
            }
            finally { w.CloseNow(); ToastWindow.MotionOverrideForTests = null; }
        });

    [Fact]
    public void Hovering_a_suppressed_card_starts_no_wipe()
        => Sta.Run(() =>
        {
            ToastWindow.MotionOverrideForTests = true;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();
                w.SetSuppressed(true);

                Hover(w);   // a hidden HWND takes no real input — pin the guard anyway

                Assert.Equal(1, w.BrandWipeCount);
            }
            finally { w.CloseNow(); ToastWindow.MotionOverrideForTests = null; }
        });

    [Fact]
    public void Motion_disabled_settles_the_brand_without_a_clock()
        => Sta.Run(() =>
        {
            ToastWindow.MotionOverrideForTests = false;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                Assert.Equal(BrandMotion.SweepTo, w._brandSweep.X);
                Assert.Equal(BrandMotion.SweepTo, w._shineSweep.X);
                Assert.Equal(1.0, w.Opacity);            // no entrance fade either
                Assert.Equal(0, w.BrandWipeCount);
            }
            finally { w.CloseNow(); ToastWindow.MotionOverrideForTests = null; }
        });

    [Fact]
    public void Motion_disabled_close_finishes_immediately()
        => Sta.Run(() =>
        {
            ToastWindow.MotionOverrideForTests = false;
            ToastWindow? closed = null;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, x => closed = x);
            w.Appear();

            w.BeginClose();                  // no 200 ms fade — CloseNow runs inline

            Assert.Same(w, closed);
            Assert.True(w.IsClosing);
            ToastWindow.MotionOverrideForTests = null;
        });

    [Fact]
    public void Retheme_mid_sweep_swaps_both_layers_and_keeps_the_phase()
        => Sta.Run(() =>
        {
            ToastWindow.MotionOverrideForTests = true;
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X > BrandMotion.SweepFrom, 3000),
                    "sweep never started");

                var dark = ToastPalette.DarkElevated;
                var mid = w._brandSweep.X;
                w.ApplyPalette(dark);

                // Both layers recolour together, mid-flight.
                var brandFg = Assert.IsAssignableFrom<SolidColorBrush>(w._brandBim.Foreground).Color;
                var shineFg = Assert.IsAssignableFrom<SolidColorBrush>(w._shineBim.Foreground).Color;
                Assert.Equal(Color.FromRgb(dark.BrandBim.R, dark.BrandBim.G, dark.BrandBim.B), brandFg);
                var shine = ToastPaletteChooser.Blend(dark.BrandBim, new Rgb(255, 255, 255), BrandMotion.ShineBlendWeight);
                Assert.Equal(Color.FromRgb(shine.R, shine.G, shine.B), shineFg);

                // …and the in-progress sweep is untouched.
                Assert.Equal(1, w.BrandWipeCount);
                Assert.Equal(mid, w._brandSweep.X);
                Assert.True(Sta.PumpUntil(() => w._brandSweep.X == BrandMotion.SweepTo, 3000),
                    $"sweep did not finish after retheme, X={w._brandSweep.X}");
            }
            finally { w.CloseNow(); ToastWindow.MotionOverrideForTests = null; }
        });

    // ---- IPT-06: close button + RVT-style layout ----

    private static void Click(FrameworkElement target, RoutedEvent routed) =>
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = routed,
            Source = target,
        });

    [Fact]
    public void Close_button_carries_a_tooltip_and_accessible_name()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                Assert.Equal("Close", AutomationProperties.GetName(w._closeHost));
                Assert.Equal("Close", w._closeHost.ToolTip);
                Assert.IsType<DockPanel>(w._closeHost.Parent);   // docked in the header
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Close_click_dismisses_without_reaching_the_card_click_path()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                Click(w._closeHost, UIElement.MouseLeftButtonUpEvent);

                Assert.True(w.IsClosing);
                Assert.Equal(0, w.CardClickCount);   // the thumbnail-open/dismiss path never ran
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Card_click_still_runs_the_dismiss_path_once()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                w.Appear();

                Click(w, Window.MouseLeftButtonUpEvent);

                Assert.True(w.IsClosing);
                Assert.Equal(1, w.CardClickCount);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Category_lives_on_its_own_row_and_duration_in_the_footer()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                // Category is a direct child of the vertical body stack, not glued to the title.
                var categoryParent = Assert.IsType<StackPanel>(w._category.Parent);
                Assert.Equal(Orientation.Vertical, categoryParent.Orientation);

                // Duration shares the footer DockPanel with the right-docked brand cell.
                var footer = Assert.IsType<DockPanel>(w._duration.Parent);
                Assert.NotSame(w._closeHost.Parent, footer);
                Assert.Contains(footer.Children.Cast<UIElement>(), c => c is Grid);   // the brand cell
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Long_content_is_height_capped_with_ellipsis()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model(), ToastPalette.LightElevated, _ => { });
            try
            {
                Assert.Equal(64, w._summary.MaxHeight);
                Assert.Equal(40, w._detail.MaxHeight);
                Assert.Equal(TextTrimming.CharacterEllipsis, w._summary.TextTrimming);
                Assert.Equal(TextTrimming.CharacterEllipsis, w._detail.TextTrimming);
                Assert.Equal(TextWrapping.Wrap, w._summary.TextWrapping);
                Assert.Equal(TextWrapping.Wrap, w._detail.TextWrapping);
            }
            finally { w.CloseNow(); }
        });

    [Fact]
    public void An_empty_category_collapses_instead_of_leaving_a_blank_row()
        => Sta.Run(() =>
        {
            var w = new ToastWindow(Model() with { Category = "" }, ToastPalette.LightElevated, _ => { });
            try
            {
                Assert.Equal(Visibility.Collapsed, w._category.Visibility);

                w.UpdateModel(Model(), ToastPalette.LightElevated);   // back to a real category

                Assert.Equal(Visibility.Visible, w._category.Visibility);
            }
            finally { w.CloseNow(); }
        });
}
