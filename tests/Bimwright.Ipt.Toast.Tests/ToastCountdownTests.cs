using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastCountdownTests
{
    [Fact]
    public void Pause_keeps_the_remaining_visible_time()
    {
        var life = new ToastCountdown(3000);
        life.Start(0);

        life.Pause(1000);

        Assert.Equal(2000, life.RemainingMs);
    }

    [Fact]
    public void A_second_pause_while_stopped_does_not_subtract_again()
    {
        var life = new ToastCountdown(3000);
        life.Start(0);
        life.Pause(1000);

        life.Pause(9000);

        Assert.Equal(2000, life.RemainingMs);
    }

    [Fact]
    public void Resume_then_pause_subtracts_only_the_new_slice()
    {
        var life = new ToastCountdown(6000);
        life.Start(0);
        life.Pause(1000);
        life.Start(11000);

        life.Pause(11500);

        Assert.Equal(4500, life.RemainingMs);
    }

    [Fact]
    public void Starting_twice_does_not_move_the_mark()
    {
        var life = new ToastCountdown(3000);
        life.Start(0);
        life.Start(500);

        life.Pause(1000);

        Assert.Equal(2000, life.RemainingMs);
    }

    [Fact]
    public void Running_longer_than_the_lifetime_expires()
    {
        var life = new ToastCountdown(3000);
        life.Start(0);

        life.Pause(5000);

        Assert.Equal(0, life.RemainingMs);
    }
}
