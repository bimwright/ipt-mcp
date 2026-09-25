using System.Windows;
using System.Windows.Input;
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
            finally { w.CloseNow(); }
        });

    [Fact]
    public void Hover_after_the_first_wipe_replays_and_settles_again()
        => Sta.Run(() =>
        {
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
            finally { w.CloseNow(); }
        });
}
